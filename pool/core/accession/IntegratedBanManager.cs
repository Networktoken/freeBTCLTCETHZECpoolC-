

using System;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Assertion = XPool.utils.Assertion;

namespace XPool.core.accession
{
    public class IntegratedBanManager : IBanManager
    {
        private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(10)
        });

        #region Implementation of IBanManager

        public bool IsBanned(IPAddress address)
        {
            Assertion.RequiresNonNull(address, nameof(address));

            var result = cache.Get(address.ToString());
            return result != null;
        }

        public void Ban(IPAddress address, TimeSpan duration)
        {
            Assertion.RequiresNonNull(address, nameof(address));
            Assertion.Requires<ArgumentException>(duration.TotalMilliseconds > 0, $"{nameof(duration)} must not be empty");

            cache.Set(address.ToString(), string.Empty, duration);
        }

        #endregion
    }
}
