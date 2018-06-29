

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using XPool.config;
using XPool.extensions;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using NLog;
using Assertion = XPool.utils.Assertion;

namespace XPool.pplns
{
    public class PayoutManager
    {
        public PayoutManager(IComponentContext ctx,
            IConnectionFactory cf,
            IBlockRepository blockRepo,
            IShareRepository shareRepo,
            IBalanceRepository balanceRepo,
            WebhookNotificationService notificationService)
        {
            Assertion.RequiresNonNull(ctx, nameof(ctx));
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(blockRepo, nameof(blockRepo));
            Assertion.RequiresNonNull(shareRepo, nameof(shareRepo));
            Assertion.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Assertion.RequiresNonNull(notificationService, nameof(notificationService));

            this.ctx = ctx;
            this.cf = cf;
            this.blockRepo = blockRepo;
            this.shareRepo = shareRepo;
            this.balanceRepo = balanceRepo;
            this.notificationService = notificationService;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly WebhookNotificationService notificationService;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private XPoolConfig clusterConfig;
        private Thread thread;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private async Task ProcessPoolsAsync()
        {
            foreach (var pool in clusterConfig.Pools.Where(x => x.Enabled && x.PaymentProcessing.Enabled))
            {
                logger.Info(() => $"Processing payments for pool {pool.Id}");

                try
                {
                    var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinMetadataAttribute>>>>()
    .First(x => x.Value.Metadata.SupportedCoins.Contains(pool.Coin.Type)).Value;
                    var handler = handlerImpl.Value;
                    await handler.ConfigureAsync(clusterConfig, pool);

                    var scheme = ctx.ResolveKeyed<IPayoutScheme>(pool.PaymentProcessing.PayoutScheme);

                    await UpdatePoolBalancesAsync(pool, handler, scheme);
                    await PayoutPoolBalancesAsync(pool, handler);
                }

                catch (InvalidOperationException ex)
                {
                    logger.Error(ex.InnerException ?? ex, () => $"[{pool.Id}] Payment processing failed");
                }

                catch (Exception ex)
                {
                    logger.Error(ex, () => $"[{pool.Id}] Payment processing failed");
                }
            }
        }

        private async Task UpdatePoolBalancesAsync(PoolConfig pool, IPayoutHandler handler, IPayoutScheme scheme)
        {
            var pendingBlocks = cf.Run(con => blockRepo.GetPendingBlocksForPool(con, pool.Id));

            var updatedBlocks = await handler.ClassifyBlocksAsync(pendingBlocks);

            if (updatedBlocks.Any())
            {
                foreach (var block in updatedBlocks.OrderBy(x => x.Created))
                {
                    logger.Info(() => $"Processing payments for pool {pool.Id}, block {block.BlockHeight}");

                    await cf.RunTxAsync(async (con, tx) =>
                    {
                        if (!block.Effort.HasValue)
                            await CalculateBlockEffort(pool, block, handler);

                        switch (block.Status)
                        {
                            case BlockStatus.Confirmed:
                                var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, block, pool);

                                await scheme.UpdateBalancesAsync(con, tx, pool, handler, block, blockReward);

                                blockRepo.UpdateBlock(con, tx, block);
                                break;

                            case BlockStatus.Orphaned:
                            case BlockStatus.Pending:
                                blockRepo.UpdateBlock(con, tx, block);
                                break;
                        }
                    });
                }
            }

            else
                logger.Info(() => $"No updated blocks for pool {pool.Id}");
        }

        private async Task PayoutPoolBalancesAsync(PoolConfig pool, IPayoutHandler handler)
        {
            var poolBalancesOverMinimum = cf.Run(con =>
                balanceRepo.GetPoolBalancesOverThreshold(con, pool.Id, pool.PaymentProcessing.MinimumPayment));

            if (poolBalancesOverMinimum.Length > 0)
            {
                try
                {
                    await handler.PayoutAsync(poolBalancesOverMinimum);
                }

                catch (Exception ex)
                {
                    await NotifyPayoutFailureAsync(poolBalancesOverMinimum, pool, ex);
                    throw;
                }
            }

            else
                logger.Info(() => $"No balances over configured minimum payout for pool {pool.Id}");
        }

        private Task NotifyPayoutFailureAsync(Balance[] balances, PoolConfig pool, Exception ex)
        {
            notificationService.NotifyPaymentFailure(pool.Id, balances.Sum(x => x.Amount), ex.Message);

            return Task.FromResult(true);
        }

        private async Task CalculateBlockEffort(PoolConfig pool, Block block, IPayoutHandler handler)
        {
            var from = DateTime.MinValue;
            var to = block.Created;

            var lastBlock = cf.Run(con => blockRepo.GetBlockBefore(con, pool.Id, new[]
{
                BlockStatus.Confirmed,
                BlockStatus.Orphaned,
                BlockStatus.Pending,
            }, block.Created));

            if (lastBlock != null)
                from = lastBlock.Created;

            var accumulatedShareDiffForBlock = cf.Run(con =>
    shareRepo.GetAccumulatedShareDifficultyBetweenCreated(con, pool.Id, from, to));

            if (accumulatedShareDiffForBlock.HasValue)
                await handler.CalculateBlockEffortAsync(block, accumulatedShareDiffForBlock.Value);
        }

        #region API-Surface

        public void Configure(XPoolConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;
        }

        public void Start()
        {
            thread = new Thread(async () =>
            {
                logger.Info(() => "Online");

                var interval = TimeSpan.FromSeconds(
                    clusterConfig.PaymentProcessing.Interval > 0 ? clusterConfig.PaymentProcessing.Interval : 600);

                while (true)
                {
                    try
                    {
                        await ProcessPoolsAsync();
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }

                    var waitResult = stopEvent.WaitOne(interval);

                    if (waitResult)
                        break;
                }
            });

            thread.Priority = ThreadPriority.Highest;
            thread.Name = "Payment Processing";
            thread.Start();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            stopEvent.Set();
            thread.Join();

            logger.Info(() => "Stopped");
        }

        #endregion     }
    }
}
