

using System;
using System.Data;
using XPool.Persistence.Model;

namespace XPool.Persistence.Repositories
{
    public interface IBlockRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Block block);
        void DeleteBlock(IDbConnection con, IDbTransaction tx, Block block);
        void UpdateBlock(IDbConnection con, IDbTransaction tx, Block block);

        Block[] PageBlocks(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize);
        Block[] GetPendingBlocksForPool(IDbConnection con, string poolId);
        Block GetBlockBefore(IDbConnection con, string poolId, BlockStatus[] status, DateTime before);
    }
}
