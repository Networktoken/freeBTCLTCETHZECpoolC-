using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Autofac;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.Blockchain.ZCash.Configuration;
using XPool.Blockchain.ZCash.DaemonResponses;
using XPool.config;
using XPool.utils;
using XPool.core.coindintf;
using XPool.extensions;
using XPool.core;
using XPool.core.stratumproto;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace XPool.Blockchain.ZCash
{
    public class ZCashJobManager<TJob> : BitcoinJobManager<TJob, ZCashBlockTemplate>
        where TJob : ZCashJob, new()
    {
        public ZCashJobManager(
            IComponentContext ctx,
            WebhookNotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) : base(ctx, notificationService, clock, extraNonceProvider)
        {
            getBlockTemplateParams = new object[]
            {
                new
                {
                    capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                }
            };
        }

        private ZCashPoolConfigExtra zcashExtraPoolConfig;

        #region Overrides of JobManagerBase<TJob>

        public override void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig)
        {
            zcashExtraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();

            base.Configure(poolConfig, clusterConfig);
        }

        #endregion

        public override async Task<bool> ValidateAddressAsync(string address)
        {
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

                        if (await base.ValidateAddressAsync(address))
                return true;

                        var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(
                ZCashCommands.ZValidateAddress, new[] { address });

            return result.Response != null && result.Response.IsValid;
        }

        protected override async Task<DaemonResponse<ZCashBlockTemplate>> GetBlockTemplateAsync()
        {
            logger.LogInvoke(LogCat);

            var subsidyResponse = await daemon.ExecuteCmdAnyAsync<ZCashBlockSubsidy>(BitcoinCommands.GetBlockSubsidy);

            var result = await daemon.ExecuteCmdAnyAsync<ZCashBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            if (subsidyResponse.Error == null && result.Error == null && result.Response != null)
                result.Response.Subsidy = subsidyResponse.Response;

            return result;
        }

        public override object[] GetSubscriberData(StratumClient worker)
        {
            Assertion.RequiresNonNull(worker, nameof(worker));

            var context = worker.GetContextAs<BitcoinWorkerContext>();

                        context.ExtraNonce1 = extraNonceProvider.Next();

                        var responseData = new object[]
            {
                context.ExtraNonce1
            };

            return responseData;
        }

        protected override IDestination AddressToDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(2).Take(20).ToArray();
            var result = new KeyId(hash);
            return result;
        }

        public override async Task<Share> SubmitShareAsync(StratumClient worker, object submission,
            double stratumDifficultyBase)
        {
            Assertion.RequiresNonNull(worker, nameof(worker));
            Assertion.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(LogCat, new[] { worker.ConnectionId });

            if (!(submission is object[] submitParams))
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.GetContextAs<BitcoinWorkerContext>();

                        var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var nTime = submitParams[2] as string;
            var extraNonce2 = submitParams[3] as string;
            var solution = submitParams[4] as string;

            if (string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            if (string.IsNullOrEmpty(solution))
                throw new StratumException(StratumError.Other, "missing or invalid solution");

            ZCashJob job;

            lock(jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

                        var split = workerValue.Split('.');
            var minerName = split[0];
            var workerName = split.Length > 1 ? split[1] : null;

                        var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, solution);

                        if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share, blockHex);

                                share.IsBlockCandidate = acceptResponse.Accepted;

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {minerName}");

                    blockSubmissionSubject.OnNext(Unit.Default);

                                                            share.TransactionConfirmationData = acceptResponse.CoinbaseTransaction;
                }

                else
                {
                                        share.TransactionConfirmationData = null;
                }
            }

                        share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = minerName;
            share.Worker = workerName;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.NetworkDifficulty = job.Difficulty;
            share.Difficulty = share.Difficulty / ShareMultiplier;
            share.Created = clock.Now;

            return share;
        }
    }
}
