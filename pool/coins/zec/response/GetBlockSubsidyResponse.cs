

using Newtonsoft.Json;

namespace XPool.Blockchain.ZCash.DaemonResponses
{
    public class ZCashBlockSubsidy
    {
        public decimal Miner { get; set; }
        public decimal? Founders { get; set; }
        public decimal? Community { get; set; }
    }
}
