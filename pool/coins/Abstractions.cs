

using System;

namespace XPool.Blockchain
{
    public class BlockchainStats
    {
        public string NetworkType { get; set; }
        public double NetworkHashrate { get; set; }
        public double NetworkDifficulty { get; set; }
        public DateTime? LastNetworkBlockTime { get; set; }
        public long BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

    public interface IExtraNonceProvider
    {
        string Next();
    }
}
