

namespace XPool.core.bus
{
    public class BlockNotification
    {
        public BlockNotification(string poolId, long blockHeight)
        {
            PoolId = poolId;
            BlockHeight = blockHeight;
        }

        public string PoolId { get; }
        public long BlockHeight { get; }
    }
}
