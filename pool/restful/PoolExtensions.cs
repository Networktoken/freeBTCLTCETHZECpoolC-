using System.Linq;
using AutoMapper;
using XPool.restful.Responses;
using XPool.Blockchain;
using XPool.Blockchain.Ethereum.Configuration;
using XPool.config;
using XPool.extensions;
using XPool.core;

namespace XPool.restful.extensions
{
    public static class PoolExtensions
    {
        public static PoolInfo ToPoolInfo(this PoolConfig pool, IMapper mapper, Persistence.Model.PoolStats stats)
        {
            var poolInfo = mapper.Map<PoolInfo>(pool);

                        poolInfo.Coin.Algorithm = GetPoolAlgorithm(pool);

            poolInfo.PoolStats = mapper.Map<PoolStats>(stats);
            poolInfo.NetworkStats = mapper.Map<BlockchainStats>(stats);

                        CoinMetaData.AddressInfoLinks.TryGetValue(pool.Coin.Type, out var addressInfobaseUrl);
            if (!string.IsNullOrEmpty(addressInfobaseUrl))
                poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

                        poolInfo.PoolFeePercent = (float)pool.RewardRecipients.Sum(x => x.Percentage);

                        if (poolInfo.PaymentProcessing.Extra != null)
            {
                var extra = poolInfo.PaymentProcessing.Extra;

                extra.StripValue(nameof(EthereumPoolPaymentProcessingConfigExtra.CoinbasePassword));
            }

            return poolInfo;
        }

        private static string GetPoolAlgorithm(PoolConfig pool)
        {
            string result = null;

            if (CoinMetaData.CoinAlgorithm.TryGetValue(pool.Coin.Type, out var getter))
                result = getter(pool.Coin.Type, pool.Coin.Algorithm);

                        if (!string.IsNullOrEmpty(result) && result.Length > 1)
                result = result.Substring(0, 1).ToUpper() + result.Substring(1);

            return result;
        }
    }
}