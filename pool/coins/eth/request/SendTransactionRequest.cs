

using System.Numerics;
using XPool.utils;
using Newtonsoft.Json;

namespace XPool.Blockchain.Ethereum.DaemonRequests
{
    public class SendTransactionRequest
    {
                                public string From { get; set; }

                                public string To { get; set; }

                                [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? Gas { get; set; }

                                [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? GasPrice { get; set; }

                                [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public BigInteger Value { get; set; }

                                [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }
    }
}
