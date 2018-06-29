

using System.Collections.Generic;
using XPool.Blockchain;
using XPool.config;
using XPool.core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XPool.restful.Responses
{
    public class ApiCoinConfig
    {
        public string Type { get; set; }
        public string Algorithm { get; set; }
    }

    public class ApiPoolPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public decimal MinimumPayment { get; set; }         public string PayoutScheme { get; set; }
        public JToken PayoutSchemeConfig { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class PoolInfo
    {
                public string Id { get; set; }

        public ApiCoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public ApiPoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public PoolShareBasedBanningConfig ShareBasedBanning { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }
        public float PoolFeePercent { get; set; }
        public string Address { get; set; }
        public string AddressInfoLink { get; set; }

                public PoolStats PoolStats { get; set; }
        public BlockchainStats NetworkStats { get; set; }
        public MinerPerformanceStats[] TopMiners { get; set; }
        public decimal TotalPaid { get; set; }
    }

    public class GetPoolsResponse
    {
        public PoolInfo[] Pools { get; set; }
    }
}
