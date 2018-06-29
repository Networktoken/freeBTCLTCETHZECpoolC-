

using System;

namespace XPool.Persistence.Model.Projections
{
    public class MinerWorkerHashes
    {
        public double Sum { get; set; }
        public long Count { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
        public DateTime FirstShare { get; set; }
        public DateTime LastShare { get; set; }
    }
}
