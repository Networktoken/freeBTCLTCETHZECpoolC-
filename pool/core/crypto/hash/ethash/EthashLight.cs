using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using XPool.Blockchain.Ethereum;
using XPool.utils;
using XPool.extensions;
using NLog;

namespace XPool.core.crypto.hash.ethash
{
    public class EthashLight : IDisposable
    {
        public EthashLight(int numCaches)
        {
            this.numCaches = numCaches;
        }

        private int numCaches;         private readonly object cacheLock = new object();
        private readonly Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>();
        private Cache future;

        public void Dispose()
        {
            foreach(var value in caches.Values)
                value.Dispose();
        }

        public async Task<bool> VerifyBlockAsync(Block block, ILogger logger)
        {
            Assertion.RequiresNonNull(block, nameof(block));

            if (block.Height > EthereumConstants.EpochLength * 2048)
            {
                logger.Debug(() => $"Block height {block.Height} exceeds limit of {EthereumConstants.EpochLength * 2048}");
                return false;
            }

            if (block.Difficulty.CompareTo(BigInteger.Zero) == 0)
            {
                logger.Debug(() => $"Invalid block diff");
                return false;
            }

                        var cache = await GetCacheAsync(block.Height, logger);

                        if (!cache.Compute(logger, block.HashNoNonce, block.Nonce, out var mixDigest, out var resultBytes))
                return false;

                        if (!block.MixDigest.SequenceEqual(mixDigest))
                return false;

                        var target = BigInteger.Divide(EthereumConstants.BigMaxValue, block.Difficulty);
            var resultValue = new BigInteger(resultBytes.ReverseArray());
            var result = resultValue.CompareTo(target) <= 0;
            return result;
        }

        private async Task<Cache> GetCacheAsync(ulong block, ILogger logger)
        {
            var epoch = block / EthereumConstants.EpochLength;
            Cache result;

            lock(cacheLock)
            {
                if (numCaches == 0)
                    numCaches = 3;

                if (!caches.TryGetValue(epoch, out result))
                {
                                        while(caches.Count >= numCaches)
                    {
                        var toEvict = caches.Values.OrderBy(x => x.LastUsed).First();
                        var key = caches.First(pair => pair.Value == toEvict).Key;
                        var epochToEvict = toEvict.Epoch;

                        logger.Debug(() => $"Evicting DAG for epoch {epochToEvict} in favour of epoch {epoch}");
                        toEvict.Dispose();
                        caches.Remove(key);
                    }

                                        if (future != null && future.Epoch == epoch)
                    {
                        logger.Debug(() => $"Using pre-generated DAG for epoch {epoch}");

                        result = future;
                        future = null;
                    }

                    else
                    {
                        logger.Debug(() => $"No pre-generated DAG available, creating new for epoch {epoch}");
                        result = new Cache(epoch);
                    }

                    caches[epoch] = result;

                                        if (future == null || future.Epoch <= epoch)
                    {
                        logger.Debug(() => $"Pre-generating DAG for epoch {epoch + 1}");
                        future = new Cache(epoch + 1);

#pragma warning disable 4014
                        future.GenerateAsync(logger);
#pragma warning restore 4014
                    }
                }

                result.LastUsed = DateTime.Now;
            }

            await result.GenerateAsync(logger);
            return result;
        }
    }
}
