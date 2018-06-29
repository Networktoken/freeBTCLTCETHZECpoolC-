

using XPool.core;

namespace XPool.Blockchain.Ethereum
{
    public class EthereumWorkerContext : WorkerContextBase
    {
        public string MinerName { get; set; }
        public string WorkerName { get; set; }
        public bool IsInitialWorkSent { get; set; } = false;

        public string ExtraNonce1 { get; set; }
    }
}
