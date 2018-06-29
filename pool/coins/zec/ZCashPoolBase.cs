

using System;
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.ZCash.DaemonResponses;
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
using BigInteger = NBitcoin.BouncyCastle.Math.BigInteger;

namespace XPool.Blockchain.ZCash
{
    public class ZCashPoolBase<TJob> : BitcoinPoolBase<TJob, ZCashBlockTemplate>
        where TJob : ZCashJob, new()
    {
        public ZCashPoolBase(IComponentContext ctx,
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

        private ZCashCoinbaseTxConfig coinbaseTxConfig;
        private double hashrateDivisor;

        protected override BitcoinJobManager<TJob, ZCashBlockTemplate> CreateJobManager()
        {
            return ctx.Resolve<ZCashJobManager<TJob>>(
                new TypedParameter(typeof(IExtraNonceProvider), new ZCashExtraNonceProvider()));
        }

        #region Overrides of BitcoinPoolBase<TJob,ZCashBlockTemplate>

                        protected override async Task SetupJobManager(CancellationToken ct)
        {
            await base.SetupJobManager(ct);

            if (ZCashConstants.CoinbaseTxConfig.TryGetValue(poolConfig.Coin.Type, out var coinbaseTx))
                coinbaseTx.TryGetValue(manager.NetworkType, out coinbaseTxConfig);

            hashrateDivisor = (double)new BigRational(coinbaseTxConfig.Diff1b,
                ZCashConstants.CoinbaseTxConfig[CoinType.ZEC][manager.NetworkType].Diff1b);
        }

        #endregion

        protected override void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
                {
                    client.ConnectionId,
                }
                .Concat(manager.GetSubscriberData(client))
                .ToArray();

            client.Respond(data, request.Id);

                        context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected override async Task OnAuthorizeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            await base.OnAuthorizeAsync(client, tsRequest);

            var context = client.GetContextAs<BitcoinWorkerContext>();

            if (context.IsAuthorized)
            {
                                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        private void OnSuggestTarget(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();
            var target = requestParams.FirstOrDefault();

            if (!string.IsNullOrEmpty(target))
            {
                if (System.Numerics.BigInteger.TryParse(target, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var targetBig))
                {
                    var newDiff = (double) new BigRational(coinbaseTxConfig.Diff1b, targetBig);
                    var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                    if (newDiff >= poolEndpoint.Difficulty)
                    {
                        context.EnqueueNewDifficulty(newDiff);
                        context.ApplyPendingDifficulty();

                        client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                    }

                    else
                        client.RespondError(StratumError.Other, "suggested difficulty too low", request.Id);
                }

                else
                    client.RespondError(StratumError.Other, "invalid target", request.Id);
            }

            else
                client.RespondError(StratumError.Other, "invalid target", request.Id);
        }

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch(request.Method)
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

                case ZCashStratumMethods.SuggestTarget:
                    OnSuggestTarget(client, tsRequest);
                    break;

                case BitcoinStratumMethods.ExtraNonceSubscribe:
                                        break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        protected override void OnNewJob(object jobParams)
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
                        client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });

                                        client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = shares * multiplier / interval / 1000000 * 2;

            result /= hashrateDivisor;
            return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<BitcoinWorkerContext>();

            context.EnqueueNewDifficulty(newDiff);

                        if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        private string EncodeTarget(double difficulty)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = coinbaseTxConfig.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray();
            var padded = ArrayPool<byte>.Shared.Rent(ZCashConstants.TargetPaddingLength);

            try
            {
                Array.Clear(padded, 0, ZCashConstants.TargetPaddingLength);
                var padLength = ZCashConstants.TargetPaddingLength - bytes.Length;

                if (padLength > 0)
                {
                    Array.Copy(bytes, 0, padded, padLength, bytes.Length);
                    bytes = padded;
                }

                var result = bytes.ToHexString(0, ZCashConstants.TargetPaddingLength);
                return result;
            }

            finally
            {
                ArrayPool<byte>.Shared.Return(padded);
            }
        }
    }
}
