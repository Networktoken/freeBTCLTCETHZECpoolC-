

using System;

namespace XPool.restful.Responses
{
    public partial class AggregatedPoolStats
    {
        public float PoolHashrate { get; set; }
        public int ConnectedMiners { get; set; }
        public int ValidSharesPerSecond { get; set; }

        public DateTime Created { get; set; }
    }

    public class GetPoolStatsResponse
    {
        public AggregatedPoolStats[] Stats { get; set; }
    }
}
