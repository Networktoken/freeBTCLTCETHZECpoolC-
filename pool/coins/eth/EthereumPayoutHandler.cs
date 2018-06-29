

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Ethereum.Configuration;
using XPool.Blockchain.Ethereum.DaemonRequests;
using XPool.Blockchain.Ethereum.DaemonResponses;
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
using Block = XPool.Persistence.Model.Block;
using Assertion = XPool.utils.Assertion;
using EC = XPool.Blockchain.Ethereum.EthCommands;

namespace XPool.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH)]
    public class EthereumPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public EthereumPayoutHandler(
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

        private readonly IComponentContext ctx;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private ParityChainType chainType;
        private const int BlockSearchOffset = 50;
        private EthereumPoolPaymentProcessingConfigExtra extraConfig;

        protected override string LogCategory => "Ethereum Payout Handler";

        #region IPayoutHandler

        public async Task ConfigureAsync(XPoolConfig clusterConfig, PoolConfig poolConfig)
        {
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();

            logger = LogUtil.GetPoolScopedLogger(typeof(EthereumPayoutHandler), poolConfig);

                        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints);

            await DetectChainAsync();
        }

        public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));
            Assertion.RequiresNonNull(blocks, nameof(blocks));

            var pageSize = 100;
            var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
            var blockCache = new Dictionary<long, DaemonResponses.Block>();
            var result = new List<Block>();

            for (var i = 0; i < pageCount; i++)
            {
                                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                                var latestBlockResponses = await daemon.ExecuteCmdAllAsync<DaemonResponses.Block>(EC.GetBlockByNumber, new[] { (object) "latest", true });
                var latestBlockHeight = latestBlockResponses.First(x => x.Error == null && x.Response?.Height != null).Response.Height.Value;

                                var blockInfos = await FetchBlocks(blockCache, page.Select(block=> (long) block.BlockHeight).ToArray());

                for(var j = 0; j < blockInfos.Length; j++)
                {
                    var blockInfo = blockInfos[j];
                    var block = page[j];

                                        var mixHash = block.TransactionConfirmationData.Split(":").First();
                    var nonce = block.TransactionConfirmationData.Split(":").Last();

                                        block.ConfirmationProgress = Math.Min(1.0d, (double) (latestBlockHeight - block.BlockHeight) / EthereumConstants.MinConfimations);
                    result.Add(block);

                                        if (blockInfo.Miner == poolConfig.Address)
                    {
                                                                                                var match = blockInfo.SealFields[0].Substring(4) == mixHash.Substring(2) &&
                            blockInfo.SealFields[1].Substring(4) == nonce.Substring(2);

                                                if (latestBlockHeight - block.BlockHeight >= EthereumConstants.MinConfimations)
                        {
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.Reward = GetBaseBlockReward(chainType, block.BlockHeight); 
                            if (extraConfig?.KeepUncles == false)
                                block.Reward += blockInfo.Uncles.Length * (block.Reward / 32); 
                            if (extraConfig?.KeepTransactionFees == false && blockInfo.Transactions?.Length > 0)
                                block.Reward += await GetTxRewardAsync(blockInfo); 
                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");
                        }

                        continue;
                    }

                                        var heightMin = block.BlockHeight - BlockSearchOffset;
                    var heightMax = Math.Min(block.BlockHeight + BlockSearchOffset, latestBlockHeight);
                    var range = new List<long>();

                    for(var k = heightMin; k < heightMax; k++)
                        range.Add((long) k);

                                        var blockInfo2s = await FetchBlocks(blockCache, range.ToArray());

                    foreach(var blockInfo2 in blockInfo2s)
                    {
                                                if (blockInfo2.Uncles.Length > 0)
                        {
                                                        var uncleBatch = blockInfo2.Uncles.Select((x, index) => new DaemonCmd(EC.GetUncleByBlockNumberAndIndex,
                                    new[] { blockInfo2.Height.Value.ToStringHexWithPrefix(), index.ToStringHexWithPrefix() }))
                                .ToArray();

                            logger.Info(() => $"[{LogCategory}] Fetching {blockInfo2.Uncles.Length} uncles for block {blockInfo2.Height}");

                            var uncleResponses = await daemon.ExecuteBatchAnyAsync(uncleBatch);

                            logger.Info(() => $"[{LogCategory}] Fetched {uncleResponses.Count(x => x.Error == null && x.Response != null)} uncles for block {blockInfo2.Height}");

                            var uncle = uncleResponses.Where(x => x.Error == null && x.Response != null)
                                .Select(x => x.Response.ToObject<DaemonResponses.Block>())
                                .FirstOrDefault(x => x.Miner == poolConfig.Address);

                            if (uncle != null)
                            {
                                                                if (latestBlockHeight - uncle.Height.Value >= EthereumConstants.MinConfimations)
                                {
                                    block.Status = BlockStatus.Confirmed;
                                    block.ConfirmationProgress = 1;
                                    block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);
                                    block.BlockHeight = uncle.Height.Value;
                                    block.Type = EthereumConstants.BlockTypeUncle;

                                    logger.Info(() => $"[{LogCategory}] Unlocked uncle for block {blockInfo2.Height.Value} at height {uncle.Height.Value} worth {FormatAmount(block.Reward)}");
                                }

                                else
                                    logger.Info(() => $"[{LogCategory}] Got immature matching uncle for block {blockInfo2.Height.Value}. Will try again.");

                                break;
                            }
                        }
                    }

                    if (block.Status == BlockStatus.Pending && block.ConfirmationProgress > 0.75)
                    {
                                                block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                    }
                }
            }

            return result.ToArray();
        }

        public Task CalculateBlockEffortAsync(Block block, double accumulatedBlockShareDiff)
        {
            block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

            return Task.FromResult(true);
        }

        public Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            var blockRewardRemaining = block.Reward;

                        foreach(var recipient in poolConfig.RewardRecipients.Where(x => x.Percentage > 0))
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

                        blockRewardRemaining -= EthereumConstants.StaticTransactionFeeReserve;

            return Task.FromResult(blockRewardRemaining);
        }

        public async Task PayoutAsync(Balance[] balances)
        {
                        var infoResponse = await daemon.ExecuteCmdSingleAsync<string>(EC.GetPeerCount);

            if (networkType == EthereumNetworkType.Main &&
                (infoResponse.Error != null || string.IsNullOrEmpty(infoResponse.Response) ||
                infoResponse.Response.IntegralFromHex<int>() < EthereumConstants.MinPayoutPeerCount))
            {
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
                return;
            }

            var txHashes = new List<string>();

            foreach(var balance in balances)
            {
                try
                {
                    var txHash = await Payout(balance);
                    txHashes.Add(txHash);
                }

                catch(Exception ex)
                {
                    logger.Error(ex);

                    NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
                }
            }

            if (txHashes.Any())
                NotifyPayoutSuccess(poolConfig.Id, balances, txHashes.ToArray(), null);
        }

        #endregion 
        private async Task<DaemonResponses.Block[]> FetchBlocks(Dictionary<long, DaemonResponses.Block> blockCache, params long[] blockHeights)
        {
            var cacheMisses = blockHeights.Where(x => !blockCache.ContainsKey(x)).ToArray();

            if (cacheMisses.Any())
            {
                var blockBatch = cacheMisses.Select(height => new DaemonCmd(EC.GetBlockByNumber,
                    new[]
                    {
                        (object) height.ToStringHexWithPrefix(),
                        true
                    })).ToArray();

                var tmp = await daemon.ExecuteBatchAnyAsync(blockBatch);

                var transformed = tmp
                    .Where(x => x.Error == null && x.Response != null)
                    .Select(x => x.Response?.ToObject<DaemonResponses.Block>())
                    .Where(x => x != null)
                    .ToArray();

                foreach(var block in transformed)
                    blockCache[(long) block.Height.Value] = block;
            }

            return blockHeights.Select(x => blockCache[x]).ToArray();
        }

        internal static decimal GetBaseBlockReward(ParityChainType chainType, ulong height)
        {
            switch(chainType)
            {
                case ParityChainType.Mainnet:
                    if (height >= EthereumConstants.ByzantiumHardForkHeight)
                        return EthereumConstants.ByzantiumBlockReward;

                    return EthereumConstants.HomesteadBlockReward;

                case ParityChainType.Classic:
                {
                    var era = Math.Floor(((double) height + 1) / EthereumClassicConstants.BlockPerEra);
                    return (decimal) Math.Pow((double) EthereumClassicConstants.BasePercent, era) * EthereumClassicConstants.BaseRewardInitial;
                }

                case ParityChainType.Expanse:
                    return EthereumConstants.ExpanseBlockReward;

                case ParityChainType.Ellaism:
                    return EthereumConstants.EllaismBlockReward;

                case ParityChainType.Ropsten:
                    return EthereumConstants.ByzantiumBlockReward;

                case ParityChainType.CallistoTestnet:
                case ParityChainType.Callisto:
                    return CallistoConstants.BaseRewardInitial * (1.0m - CallistoConstants.TreasuryPercent);

                default:
                    throw new Exception("Unable to determine block reward: Unsupported chain type");
            }
        }

        private async Task<decimal> GetTxRewardAsync(DaemonResponses.Block blockInfo)
        {
                        var batch = blockInfo.Transactions.Select(tx => new DaemonCmd(EC.GetTxReceipt, new[] { tx.Hash }))
                .ToArray();

            var results = await daemon.ExecuteBatchAnyAsync(batch);

            if (results.Any(x => x.Error != null))
                throw new Exception($"Error fetching tx receipts: {string.Join(", ", results.Where(x => x.Error != null).Select(y => y.Error.Message))}");

                        var gasUsed = results.Select(x => x.Response.ToObject<TransactionReceipt>())
                .ToDictionary(x => x.TransactionHash, x => x.GasUsed);

                        var result = blockInfo.Transactions.Sum(x => (ulong) gasUsed[x.Hash] * ((decimal) x.GasPrice / EthereumConstants.Wei));

            return result;
        }

        internal static decimal GetUncleReward(ParityChainType chainType, ulong uheight, ulong height)
        {
            var reward = GetBaseBlockReward(chainType, height);

            switch (chainType)
            {
                case ParityChainType.Classic:
                    reward *= EthereumClassicConstants.UnclePercent;
                    break;

                default:
                                        reward *= uheight + 8 - height;
                    reward /= 8m;
                    break;
            }

            return reward;
        }

        private async Task DetectChainAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(EC.GetNetVersion),
                new DaemonCmd(EC.ParityChain),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    throw new Exception($"Chain detection failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

                        var netVersion = results[0].Response.ToObject<string>();
            var parityChain = results[1].Response.ToObject<string>();

            EthereumUtils.DetectNetworkAndChain(netVersion, parityChain, out networkType, out chainType);
        }

        private async Task<string> Payout(Balance balance)
        {
                        if (extraConfig.CoinbasePassword != null)
            {
                var unlockResponse = await daemon.ExecuteCmdSingleAsync<object>(EC.UnlockAccount, new[]
                {
                    poolConfig.Address,
                    extraConfig.CoinbasePassword,
                    null
                });

                if (unlockResponse.Error != null || unlockResponse.Response == null || (bool) unlockResponse.Response == false)
                    throw new Exception("Unable to unlock coinbase account for sending transaction");
            }

                        logger.Info(() => $"[{LogCategory}] Sending {FormatAmount(balance.Amount)} to {balance.Address}");

            var request = new SendTransactionRequest
            {
                From = poolConfig.Address,
                To = balance.Address,
                Value = (BigInteger) Math.Floor(balance.Amount * EthereumConstants.Wei),
            };

            var response = await daemon.ExecuteCmdSingleAsync<string>(EC.SendTx, new[] { request });

            if (response.Error != null)
                throw new Exception($"{EC.SendTx} returned error: {response.Error.Message} code {response.Error.Code}");

            if (string.IsNullOrEmpty(response.Response) || EthereumConstants.ZeroHashPattern.IsMatch(response.Response))
                throw new Exception($"{EC.SendTx} did not return a valid transaction hash");

            var txHash = response.Response;
            logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}");

                        PersistPayments(new[] { balance }, txHash);

                        return txHash;
        }
    }
}
