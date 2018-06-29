

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Bitcoin.Configuration;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.config;
using XPool.core.coindintf;
using XPool.extensions;
using XPool.core;
using XPool.pplns;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = XPool.Persistence.Model.Block;
using Assertion = XPool.utils.Assertion;

namespace XPool.Blockchain.Bitcoin
{
    [CoinMetadata(
        CoinType.BTC,
        CoinType.LTC)]
    public class BitcoinPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public BitcoinPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            WebhookNotificationService notificationService) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, notificationService)
        {
            Assertion.RequiresNonNull(ctx, nameof(ctx));
            Assertion.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Assertion.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.ctx = ctx;
        }

        protected readonly IComponentContext ctx;
        protected DaemonClient daemon;
        protected BitcoinCoinProperties coinProperties;
        protected BitcoinDaemonEndpointConfigExtra extraPoolConfig;
        protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

        protected override string LogCategory => "Bitcoin Payout Handler";

        #region IPayoutHandler

        public virtual Task ConfigureAsync(XPoolConfig clusterConfig, PoolConfig poolConfig)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();
            coinProperties = BitcoinProperties.GetCoinProperties(poolConfig.Coin.Type, poolConfig.Coin.Algorithm);

            logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), poolConfig);

            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(poolConfig.Daemons);

            return Task.FromResult(true);
        }

        public virtual async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));
            Assertion.RequiresNonNull(blocks, nameof(blocks));

            var pageSize = 100;
            var pageCount = (int)Math.Ceiling(blocks.Length / (double)pageSize);
            var result = new List<Block>();

            for (var i = 0; i < pageCount; i++)
            {
                var page = blocks
    .Skip(i * pageSize)
    .Take(pageSize)
    .ToArray();

                var batch = page.Select(block => new DaemonCmd(BitcoinCommands.GetTransaction,
    new[] { block.TransactionConfirmationData })).ToArray();

                var results = await daemon.ExecuteBatchAnyAsync(batch);

                for (var j = 0; j < results.Length; j++)
                {
                    var cmdResult = results[j];

                    var transactionInfo = cmdResult.Response?.ToObject<Transaction>();
                    var block = page[j];

                    if (cmdResult.Error != null)
                    {
                        if (cmdResult.Error.Code == -5)
                        {
                            block.Status = BlockStatus.Orphaned;
                            result.Add(block);
                        }

                        else
                        {
                            logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
                        }
                    }

                    else if (transactionInfo?.Details == null || transactionInfo.Details.Length == 0)
                    {
                        block.Status = BlockStatus.Orphaned;
                        result.Add(block);
                    }

                    else
                    {
                        switch (transactionInfo.Details[0].Category)
                        {
                            case "immature":
                                var minConfirmations = extraPoolConfig?.MinimumConfirmations ?? BitcoinConstants.CoinbaseMinConfimations;
                                block.ConfirmationProgress = Math.Min(1.0d, (double)transactionInfo.Confirmations / minConfirmations);
                                result.Add(block);
                                break;

                            case "generate":
                                block.Status = BlockStatus.Confirmed;
                                block.ConfirmationProgress = 1;
                                result.Add(block);

                                logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");
                                break;

                            default:
                                logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned. Category: {transactionInfo.Details[0].Category}");

                                block.Status = BlockStatus.Orphaned;
                                block.Reward = 0;
                                result.Add(block);
                                break;
                        }
                    }
                }
            }

            return result.ToArray();
        }

        public virtual Task CalculateBlockEffortAsync(Block block, double accumulatedBlockShareDiff)
        {
            block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

            return Task.FromResult(true);
        }
        public virtual Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            var blockRewardRemaining = block.Reward;

            foreach (var recipient in poolConfig.RewardRecipients.Where(x => x.Percentage > 0))
            {
                var amount = block.Reward * (recipient.Percentage / 100.0m);
                var address = recipient.Address;

                blockRewardRemaining -= amount;

                if (address != poolConfig.Address)
                {
                    logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
                    balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount, $"Reward for block {block.BlockHeight}");
                }
            }

            return Task.FromResult(blockRewardRemaining);
        }

        public virtual async Task PayoutAsync(Balance[] balances)
        {
            Assertion.RequiresNonNull(balances, nameof(balances));

            var amounts = balances
    .Where(x => x.Amount > 0)
    .ToDictionary(x => x.Address, x => Math.Round(x.Amount, 8));

            if (amounts.Count == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            object[] args;

            if (extraPoolPaymentProcessingConfig?.MinersPayTxFees == true)
            {
                var comment = (poolConfig.PoolName ?? clusterConfig.ClusterName ?? "MiningCore").Trim() + " Payment";
                var subtractFeesFrom = amounts.Keys.ToArray();

                args = new object[]
                {
                    string.Empty,                               amounts,                                    1,                                          comment,                                    subtractFeesFrom                        };
            }

            else
            {
                args = new object[]
                {
                    string.Empty,                               amounts,                                };
            }

            var didUnlockWallet = false;

        tryTransfer:
            var result = await daemon.ExecuteCmdSingleAsync<string>(BitcoinCommands.SendMany, args, new JsonSerializerSettings());

            if (result.Error == null)
            {
                if (didUnlockWallet)
                {
                    logger.Info(() => $"[{LogCategory}] Locking wallet");
                    await daemon.ExecuteCmdSingleAsync<JToken>(BitcoinCommands.WalletLock);
                }

                var txId = result.Response;

                if (string.IsNullOrEmpty(txId))
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payout transaction id: {txId}");

                PersistPayments(balances, txId);

                NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txId }, null);
            }

            else
            {
                if (result.Error.Code == (int)BitcoinRPCErrorCode.RPC_WALLET_UNLOCK_NEEDED && !didUnlockWallet)
                {
                    if (!string.IsNullOrEmpty(extraPoolPaymentProcessingConfig?.WalletPassword))
                    {
                        logger.Info(() => $"[{LogCategory}] Unlocking wallet");

                        var unlockResult = await daemon.ExecuteCmdSingleAsync<JToken>(BitcoinCommands.WalletPassphrase, new[]
                        {
                            (object) extraPoolPaymentProcessingConfig.WalletPassword,
                            (object) 5                          });

                        if (unlockResult.Error == null)
                        {
                            didUnlockWallet = true;
                            goto tryTransfer;
                        }

                        else
                            logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                    }

                    else
                        logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                }

                else
                {
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                    NotifyPayoutFailure(poolConfig.Id, balances, $"{BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                }
            }
        }

        #endregion     }
    }
}
