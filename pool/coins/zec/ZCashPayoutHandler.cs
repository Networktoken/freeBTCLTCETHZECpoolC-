

using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.Blockchain.ZCash.Configuration;
using XPool.Blockchain.ZCash.DaemonRequests;
using XPool.Blockchain.ZCash.DaemonResponses;
using XPool.config;
using XPool.extensions;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json.Linq;
using Block = XPool.Persistence.Model.Block;
using Assertion = XPool.utils.Assertion;

namespace XPool.Blockchain.ZCash
{
    [CoinMetadata(CoinType.ZEC)]
    public class ZCashPayoutHandler : BitcoinPayoutHandler
    {
        public ZCashPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            WebhookNotificationService notificationService) :
            base(ctx, cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, notificationService)
        {
        }

        protected ZCashPoolConfigExtra poolExtraConfig;
        protected bool supportsNativeShielding;
        protected BitcoinNetworkType networkType;
        protected ZCashCoinbaseTxConfig coinbaseTxConfig;
        protected override string LogCategory => "ZCash Payout Handler";
        protected const decimal TransferFee = 0.0001m;
        protected const int ZMinConfirmations = 8;

        #region IPayoutHandler

        public override async Task ConfigureAsync(XPoolConfig clusterConfig, PoolConfig poolConfig)
        {
            await base.ConfigureAsync(clusterConfig, poolConfig);

            poolExtraConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();

                        var blockchainInfoResponse = await daemon.ExecuteCmdSingleAsync<BlockchainInfo>(BitcoinCommands.GetBlockchainInfo);

            if (blockchainInfoResponse.Response.Chain.ToLower() == "test")
                networkType = BitcoinNetworkType.Test;
            else if (blockchainInfoResponse.Response.Chain.ToLower() == "regtest")
                networkType = BitcoinNetworkType.RegTest;
            else
                networkType = BitcoinNetworkType.Main;

                        if (ZCashConstants.CoinbaseTxConfig.TryGetValue(poolConfig.Coin.Type, out var coinbaseTx))
                coinbaseTx.TryGetValue(networkType, out coinbaseTxConfig);

                        var response = await daemon.ExecuteCmdSingleAsync<JObject>(ZCashCommands.ZShieldCoinbase);
            supportsNativeShielding = response.Error.Code != (int) BitcoinRPCErrorCode.RPC_METHOD_NOT_FOUND;
        }

        public override async Task PayoutAsync(Balance[] balances)
        {
            Assertion.RequiresNonNull(balances, nameof(balances));

                        if (supportsNativeShielding)
                await ShieldCoinbaseAsync();
            else
                await ShieldCoinbaseEmulatedAsync();

            var didUnlockWallet = false;

                        var pageSize = 50;
            var pageCount = (int)Math.Ceiling(balances.Length / (double)pageSize);

            for (var i = 0; i < pageCount; i++)
            {
                didUnlockWallet = false;

                                var page = balances
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                                var amounts = page
                    .Where(x => x.Amount > 0)
                    .Select(x => new ZSendManyRecipient { Address = x.Address, Amount = Math.Round(x.Amount, 8) })
                    .ToList();

                if (amounts.Count == 0)
                    return;

                var pageAmount = amounts.Sum(x => x.Amount);

                                var balanceResult = await daemon.ExecuteCmdSingleAsync<object>(ZCashCommands.ZGetBalance, new object[]
                {
                    poolExtraConfig.ZAddress,                       ZMinConfirmations,                          });

                if (balanceResult.Error != null || (decimal) (double) balanceResult.Response - TransferFee < pageAmount)
                {
                    logger.Info(() => $"[{LogCategory}] Insufficient shielded balance for payment of {FormatAmount(pageAmount)}");
                    return;
                }

                logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(pageAmount)} to {page.Length} addresses");

                var args = new object[]
                {
                    poolExtraConfig.ZAddress,                       amounts,                                        ZMinConfirmations,                              TransferFee
                };

                                tryTransfer:
                var result = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZSendMany, args);

                if (result.Error == null)
                {
                    var operationId = result.Response;

                                        if (string.IsNullOrEmpty(operationId))
                        logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} did not return a operation id!");
                    else
                    {
                        logger.Info(() => $"[{LogCategory}] Tracking payout operation id: {operationId}");

                        var continueWaiting = true;

                        while(continueWaiting)
                        {
                            var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                                ZCashCommands.ZGetOperationResult, new object[] { new object[] { operationId }});

                            if (operationResultResponse.Error == null &&
                                operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                            {
                                var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                                if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                                {
                                    logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                                    break;
                                }

                                switch (status)
                                {
                                    case ZOperationStatus.Success:
                                        var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                                        logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} completed with transaction id: {txId}");

                                        PersistPayments(page, txId);
                                        NotifyPayoutSuccess(poolConfig.Id, page, new[] {txId}, null);

                                        continueWaiting = false;
                                        continue;

                                    case ZOperationStatus.Cancelled:
                                    case ZOperationStatus.Failed:
                                        logger.Error(() => $"{ZCashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");
                                        NotifyPayoutFailure(poolConfig.Id, page, $"{ZCashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}", null);

                                        continueWaiting = false;
                                        continue;
                                }
                            }

                            logger.Info(() => $"[{LogCategory}] Waiting for completion: {operationId}");
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
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
                                (object) 5                             });

                            if (unlockResult.Error == null)
                            {
                                didUnlockWallet = true;
                                goto tryTransfer;
                            }

                            else
                            {
                                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                                NotifyPayoutFailure(poolConfig.Id, page, $"{BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}", null);
                                break;
                            }
                        }

                        else
                        {
                            logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                            NotifyPayoutFailure(poolConfig.Id, page, $"Wallet is locked but walletPassword was not configured. Unable to send funds.", null);
                            break;
                        }
                    }

                    else
                    {
                        logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                        NotifyPayoutFailure(poolConfig.Id, page, $"{ZCashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                    }
                }
            }

                        logger.Info(() => $"[{LogCategory}] Locking wallet");
            await daemon.ExecuteCmdSingleAsync<JToken>(BitcoinCommands.WalletLock);
        }

        #endregion 
                                                private async Task ShieldCoinbaseAsync()
        {
            logger.Info(() => $"[{LogCategory}] Shielding ZCash Coinbase funds");

            var args = new object[]
            {
                poolConfig.Address,                         poolExtraConfig.ZAddress,               };

            var result = await daemon.ExecuteCmdSingleAsync<ZCashShieldingResponse>(ZCashCommands.ZShieldCoinbase, args);

            if (result.Error != null)
            {
                if(result.Error.Code == -6)
                    logger.Info(() => $"[{LogCategory}] No funds to shield");
                else
                    logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} returned error: {result.Error.Message} code {result.Error.Code}");

                return;
            }

            var operationId = result.Response.OperationId;

            logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} operation id: {operationId}");

            var continueWaiting = true;

            while (continueWaiting)
            {
                var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                    ZCashCommands.ZGetOperationResult, new object[] { new object[] { operationId } });

                if (operationResultResponse.Error == null &&
                    operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                {
                    var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                    if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                    {
                        logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                        break;
                    }

                    switch (status)
                    {
                        case ZOperationStatus.Success:
                            logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} successful");

                            continueWaiting = false;
                            continue;

                        case ZOperationStatus.Cancelled:
                        case ZOperationStatus.Failed:
                            logger.Error(() => $"{ZCashCommands.ZShieldCoinbase} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");

                            continueWaiting = false;
                            continue;
                    }
                }

                logger.Info(() => $"[{LogCategory}] Waiting for shielding operation completion: {operationId}");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        private async Task ShieldCoinbaseEmulatedAsync()
        {
            logger.Info(() => $"[{LogCategory}] Shielding ZCash Coinbase funds (emulated)");

                        var unspentResult = await daemon.ExecuteCmdSingleAsync<Utxo[]>(BitcoinCommands.ListUnspent);

            if (unspentResult.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.ListUnspent} returned error: {unspentResult.Error.Message} code {unspentResult.Error.Code}");
                return;
            }

            var balance = unspentResult.Response
                .Where(x=> x.Spendable && x.Address == poolConfig.Address)
                .Sum(x=> x.Amount);

                        if (balance - TransferFee <= TransferFee)
            {
                logger.Info(() => $"[{LogCategory}] Balance {FormatAmount(balance)} too small for emulated shielding");
                return;
            }

            logger.Info(() => $"[{LogCategory}] Transferring {FormatAmount(balance - TransferFee)} to pool's z-addr");

                        var recipient = new ZSendManyRecipient
            {
                Address = poolExtraConfig.ZAddress,
                Amount = balance - TransferFee
            };

            var args = new object[]
            {
                poolConfig.Address,                 new object[]                 {
                    recipient
                },
                1,
                TransferFee
            };

                        var sendResult = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZSendMany, args);

            if (sendResult.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} returned error: {unspentResult.Error.Message} code {unspentResult.Error.Code}");
                return;
            }

            var operationId = sendResult.Response;

            logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} operation id: {operationId}");

            var continueWaiting = true;

            while (continueWaiting)
            {
                var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                    ZCashCommands.ZGetOperationResult, new object[] { new object[] { operationId } });

                if (operationResultResponse.Error == null &&
                    operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                {
                    var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                    if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                    {
                        logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                        break;
                    }

                    switch (status)
                    {
                        case ZOperationStatus.Success:
                            var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                            logger.Info(() => $"[{LogCategory}] Transfer completed with transaction id: {txId}");

                            continueWaiting = false;
                            continue;

                        case ZOperationStatus.Cancelled:
                        case ZOperationStatus.Failed:
                            logger.Error(() => $"{ZCashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");

                            continueWaiting = false;
                            continue;
                    }
                }

                logger.Info(() => $"[{LogCategory}] Waiting for shielding transfer completion: {operationId}");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }
}
