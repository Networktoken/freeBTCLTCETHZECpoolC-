

using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.config;
using XPool.core.jsonrpc;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Repositories;
using XPool.core.stratumproto;
using XPool.utils;
using Newtonsoft.Json;
using NLog;

namespace XPool.Blockchain.Bitcoin
{
    public class BitcoinPoolBase<TJob, TBlockTemplate> : PoolBase
        where TBlockTemplate : BlockTemplate
        where TJob : BitcoinJob<TBlockTemplate>, new()
    {
        public BitcoinPoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            WebhookNotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, notificationService)
        {
        }

        protected object currentJobParams;
        protected BitcoinJobManager<TJob, TBlockTemplate> manager;

        protected virtual void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var context = client.GetContextAs<BitcoinWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
                {
                    new object[]
                    {
                        new object[] { BitcoinStratumMethods.SetDifficulty, client.ConnectionId },
                        new object[] { BitcoinStratumMethods.MiningNotify, client.ConnectionId }
                    }
                }
                .Concat(manager.GetSubscriberData(client))
                .ToArray();

            client.Respond(data, request.Id);

            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;

            client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        protected virtual async Task OnAuthorizeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var context = client.GetContextAs<BitcoinWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);

            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault()?.Trim();
            var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

            context.IsAuthorized = !string.IsNullOrEmpty(minerName) && await manager.ValidateAddressAsync(minerName);
            context.MinerName = minerName;
            context.WorkerName = workerName;

            if (context.IsAuthorized)
            {
                client.Respond(context.IsAuthorized, request.Id);

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] = {workerValue} = {client.RemoteEndpoint.Address}");

                var staticDiff = GetStaticDiffFromPassparts(passParts);
                if (staticDiff.HasValue &&
                    (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                        context.VarDiff == null && staticDiff.Value > context.Difficulty))
                {
                    context.VarDiff = null; context.SetDifficulty(staticDiff.Value);

                    client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                }
            }

            else
            {
                client.RespondError(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning unauthorized worker for 60 sec");

                banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(60));

                DisconnectClient(client);
            }
        }

        protected virtual async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                    return;
                }

                context.LastActivity = clock.Now;

                if (!context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                var requestParams = request.ParamsAs<string[]>();
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, requestParams, poolEndpoint.Difficulty);

                client.Respond(true, request.Id);
                messageBus.SendMessage(new ClientShare(client, share));

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                context.Stats.ValidShares++;
                UpdateVarDiff(client);
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                context.Stats.InvalidShares++;
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected: {ex.Code}");

                ConsiderBan(client, context, poolConfig.Banning);
            }
        }

        private void OnSuggestDifficulty(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            client.Respond(true, request.Id);

            try
            {
                var requestedDiff = (double)Convert.ChangeType(request.Params, TypeCode.Double);

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                if (requestedDiff > poolEndpoint.Difficulty)
                {
                    context.SetDifficulty(requestedDiff);
                    client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to convert suggested difficulty {request.Params}");
            }
        }

        protected void OnGetTransactions(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            try
            {
                var transactions = manager.GetTransactions(client, request.ParamsAs<object[]>());

                client.Respond(transactions, request.Id);
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to convert suggested difficulty {request.Params}");
            }
        }

        protected virtual void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => $"[{LogCat}] Broadcasting job");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<BitcoinWorkerContext>();

                if (context.IsSubscribed && context.IsAuthorized)
                {
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    if (context.ApplyPendingDifficulty())
                        client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        #region Overrides

        protected virtual BitcoinJobManager<TJob, TBlockTemplate> CreateJobManager()
        {
            return ctx.Resolve<BitcoinJobManager<TJob, TBlockTemplate>>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider()));
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = CreateJobManager();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs.Subscribe(OnNewJob));

                await manager.Jobs.Take(1).ToTask(ct);
            }
        }

        protected override void InitStats()
        {
            base.InitStats();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new BitcoinWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch (request.Method)
            {
                case BitcoinStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case BitcoinStratumMethods.Authorize:
                    await OnAuthorizeAsync(client, tsRequest);
                    break;

                case BitcoinStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case BitcoinStratumMethods.SuggestDifficulty:
                    OnSuggestDifficulty(client, tsRequest);
                    break;

                case BitcoinStratumMethods.GetTransactions:
                    break;

                case BitcoinStratumMethods.ExtraNonceSubscribe:
                    break;

                case BitcoinStratumMethods.MiningMultiVersion:
                    break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = shares * multiplier / interval;



            return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<BitcoinWorkerContext>();
            context.EnqueueNewDifficulty(newDiff);

            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        #endregion     }
    }
}
