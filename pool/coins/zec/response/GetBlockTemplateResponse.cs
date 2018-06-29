

using Newtonsoft.Json;

namespace XPool.Blockchain.ZCash.DaemonResponses
{
    public class ZCashCoinbaseTransaction
    {
        public string Data { get; set; }
        public string Hash { get; set; }
        public decimal Fee { get; set; }
        public int SigOps { get; set; }
        public ulong FoundersReward { get; set; }
        public bool Required { get; set; }

            }

    public class ZCashBlockTemplate : Bitcoin.DaemonResponses.BlockTemplate
    {
        public string[] Capabilities { get; set; }

        [JsonProperty("coinbasetxn")]
        public ZCashCoinbaseTransaction CoinbaseTx { get; set; }

        public string LongPollId { get; set; }
        public ulong MinTime { get; set; }
        public ulong SigOpLimit { get; set; }
        public ulong SizeLimit { get; set; }
        public string[] Mutable { get; set; }

        public ZCashBlockSubsidy Subsidy { get; set; }
    }
}
