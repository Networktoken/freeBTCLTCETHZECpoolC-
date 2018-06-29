

using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Ethereum.Configuration;
using XPool.config;
using XPool.extensions;
using XPool.core.jsonrpc;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Repositories;
using XPool.core.stratumproto;
using XPool.utils;
using Newtonsoft.Json;

namespace XPool.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH)]
    public class EthereumPool : PoolBase
    {
        public EthereumPool(IComponentContext ctx,
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

        private object currentJobParams;
        private EthereumJobManager manager;

        private void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            if (requestParams == null || requestParams.Length < 2 || requestParams.Any(string.IsNullOrEmpty))
            {
                client.RespondError(StratumError.MinusOne, "invalid request", request.Id);
                return;
            }

            manager.PrepareWorker(client);

            var data = new object[]
                {
                    new object[]
                    {
                        EthereumStratumMethods.MiningNotify,
                        client.ConnectionId,
                        EthereumConstants.EthereumStratumVersion
                    },
                    context.ExtraNonce1
                }
                .ToArray();

            client.Respond(data, request.Id);

            context.IsSubscribed = true;
            context.UserAgent = requestParams[0].Trim();
        }

        private void OnAuthorize(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);

            var workerParts = workerValue?.Split('.');
            var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
            var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : null;

            context.IsAuthorized = !string.IsNullOrEmpty(minerName) && manager.ValidateAddress(minerName);
            context.MinerName = minerName;
            context.WorkerName = workerName;

            client.Respond(context.IsAuthorized, request.Id);

            var staticDiff = GetStaticDiffFromPassparts(passParts);
            if (staticDiff.HasValue &&
                (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; context.SetDifficulty(staticDiff.Value);
            }

            EnsureInitialWorkSent(client);

            logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] = {workerValue} = {client.RemoteEndpoint.Address}");
        }

        private async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<EthereumWorkerContext>();

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

                if (!context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                var submitRequest = request.ParamsAs<string[]>();

                if (submitRequest.Length != 3 ||
                    submitRequest.Any(string.IsNullOrEmpty))
                    throw new StratumException(StratumError.MinusOne, "malformed PoW result");

                context.LastActivity = clock.Now;

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, context.Difficulty,
                    poolEndpoint.Difficulty);

                client.Respond(true, request.Id);
                messageBus.SendMessage(new ClientShare(client, share));

                EnsureInitialWorkSent(client);

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty / EthereumConstants.Pow2x32, 3)}");

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

        private void EnsureInitialWorkSent(StratumClient client)
        {
            var context = client.GetContextAs<EthereumWorkerContext>();

            lock (context)
            {
                if (context.IsAuthorized && context.IsAuthorized && !context.IsInitialWorkSent)
                {
                    context.IsInitialWorkSent = true;

                    client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                    client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
                }
            }
        }

        private void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => $"[{LogCat}] Broadcasting job");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<EthereumWorkerContext>();

                if (context.IsSubscribed && context.IsAuthorized && context.IsInitialWorkSent)
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
                        client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<EthereumJobManager>();
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
            return new EthereumWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch (request.Method)
            {
                case EthereumStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case EthereumStratumMethods.Authorize:
                    OnAuthorize(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case EthereumStratumMethods.ExtraNonceSubscribe:
                    break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var result = shares / interval;
            return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            base.OnVarDiffUpdate(client, newDiff);

            var context = client.GetContextAs<EthereumWorkerContext>();

            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
            }
        }

        public override void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            var extraConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();
            if (clusterConfig.PaymentProcessing?.Enabled == true && extraConfig?.CoinbasePassword == null)
                logger.ThrowLogPoolStartupException("\"paymentProcessing.coinbasePassword\" pool-configuration property missing or empty (required for unlocking wallet during payment processing)");
        }

        #endregion     }
    }
}
