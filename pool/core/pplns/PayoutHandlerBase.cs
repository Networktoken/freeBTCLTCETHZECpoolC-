

using System;
using System.Data.Common;
using System.Linq;
using AutoMapper;
using XPool.Blockchain;
using XPool.config;
using XPool.extensions;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;
using NLog;
using Polly;
using Assertion = XPool.utils.Assertion;

namespace XPool.pplns
{
    public abstract class PayoutHandlerBase
    {
        protected PayoutHandlerBase(IConnectionFactory cf, IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            WebhookNotificationService notificationService)
        {
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(mapper, nameof(mapper));
            Assertion.RequiresNonNull(shareRepo, nameof(shareRepo));
            Assertion.RequiresNonNull(blockRepo, nameof(blockRepo));
            Assertion.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Assertion.RequiresNonNull(paymentRepo, nameof(paymentRepo));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(notificationService, nameof(notificationService));

            this.cf = cf;
            this.mapper = mapper;
            this.clock = clock;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
            this.paymentRepo = paymentRepo;
            this.notificationService = notificationService;

            BuildFaultHandlingPolicy();
        }

        protected readonly IBalanceRepository balanceRepo;
        protected readonly IBlockRepository blockRepo;
        protected readonly IConnectionFactory cf;
        protected readonly IMapper mapper;
        protected readonly IPaymentRepository paymentRepo;
        protected readonly IShareRepository shareRepo;
        protected readonly IMasterClock clock;
        protected readonly WebhookNotificationService notificationService;
        protected XPoolConfig clusterConfig;
        private Policy faultPolicy;

        protected ILogger logger;
        protected PoolConfig poolConfig;
        private const int RetryCount = 8;

        protected abstract string LogCategory { get; }

        protected void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<TimeoutException>()
                .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), OnRetry);

            faultPolicy = retry;
        }

        protected virtual void OnRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"[{LogCategory}] Retry {1} in {timeSpan} due to: {ex}");
        }

        protected virtual void PersistPayments(Balance[] balances, string transactionConfirmation)
        {
            try
            {
                faultPolicy.Execute(() =>
                {
                    cf.RunTx((con, tx) =>
                    {
                        foreach(var balance in balances)
                        {
                            if (!string.IsNullOrEmpty(transactionConfirmation) &&
                                !poolConfig.RewardRecipients.Any(x=> x.Address == balance.Address))
                            {
                                                                var payment = new Payment
                                {
                                    PoolId = poolConfig.Id,
                                    Coin = poolConfig.Coin.Type,
                                    Address = balance.Address,
                                    Amount = balance.Amount,
                                    Created = clock.Now,
                                    TransactionConfirmationData = transactionConfirmation
                                };

                                paymentRepo.Insert(con, tx, payment);
                            }

                                                        logger.Debug(() => $"[{LogCategory}] Resetting balance of {balance.Address}");
                            balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, balance.Address, -balance.Amount, $"Balance reset after payment");
                        }
                    });
                });
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCategory}] Failed to persist the following payments: " +
                    $"{JsonConvert.SerializeObject(balances.Where(x => x.Amount > 0).ToDictionary(x => x.Address, x => x.Amount))}");
                throw;
            }
        }

        public string FormatAmount(decimal amount)
        {
            return $"{amount:0.#####} {poolConfig.Coin.Type}";
        }

        protected virtual void NotifyPayoutSuccess(string poolId, Balance[] balances, string[] txHashes, decimal? txFee)
        {
                        if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
            {
                                var txInfo = string.Join(", ", txHashes);

                if (CoinMetaData.TxInfoLinks.TryGetValue(poolConfig.Coin.Type, out var baseUrl))
                    txInfo = string.Join(", ", txHashes.Select(txHash => $"<a href=\"{string.Format(baseUrl, txHash)}\">{txHash}</a>"));

                notificationService.NotifyPaymentSuccess(poolId, balances.Sum(x => x.Amount), balances.Length, txInfo, txFee);
            }
        }

        protected virtual void NotifyPayoutFailure(string poolId, Balance[] balances, string error, Exception ex)
        {
            notificationService.NotifyPaymentFailure(poolId, balances.Sum(x => x.Amount), error ?? ex?.Message);
        }
    }
}
