

using XPool.core;

namespace XPool.Blockchain.Bitcoin
{
    public class BitcoinWorkerContext : WorkerContextBase
    {
        public string MinerName { get; set; }
        public string WorkerName { get; set; }

        public string ExtraNonce1 { get; set; }
    }
}
