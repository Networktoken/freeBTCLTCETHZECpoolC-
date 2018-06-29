

using System;

namespace XPool.core
{
    public class PoolStats
    {
        public DateTime? LastPoolBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
        public ulong PoolHashrate { get; set; }
        public int SharesPerSecond { get; set; }
    }
}
