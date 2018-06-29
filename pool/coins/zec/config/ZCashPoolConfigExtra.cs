

using Newtonsoft.Json;

namespace XPool.Blockchain.ZCash.Configuration
{
    public class ZCashPoolConfigExtra
    {
                                [JsonProperty("z-address")]
        public string ZAddress { get; set; }
    }
}
