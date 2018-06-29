

using System;
using XPool.config;
using XPool.utils;

namespace XPool.core.diffadjust
{
    public class VarDiffContext
    {
        public double? LastTs { get; set; }
        public double LastRtc { get; set; }
        public CircularDoubleBuffer TimeBuffer { get; set; }
        public DateTime? LastUpdate { get; set; }
        public VarDiffConfig Config { get; set; }
    }
}
