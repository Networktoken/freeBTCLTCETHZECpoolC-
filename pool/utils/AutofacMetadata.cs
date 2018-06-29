

using System;
using System.Collections.Generic;
using XPool.config;
using XPool.core;

namespace XPool
{
    public class CoinMetadataAttribute : Attribute
    {
        public CoinMetadataAttribute(IDictionary<string, object> values)
        {
            if (values.ContainsKey(nameof(SupportedCoins)))
                SupportedCoins = (CoinType[]) values[nameof(SupportedCoins)];
        }

        public CoinMetadataAttribute(params CoinType[] supportedCoins)
        {
            SupportedCoins = supportedCoins;
        }

        public CoinType[] SupportedCoins { get; }
    }
}
