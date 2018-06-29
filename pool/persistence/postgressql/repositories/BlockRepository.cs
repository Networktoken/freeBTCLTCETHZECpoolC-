

using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using XPool.extensions;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using NLog;

namespace XPool.Persistence.Postgres.Repositories
{
    public class BlockRepository : IBlockRepository
    {
        public BlockRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void Insert(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Block>(block);

            var query =
                "INSERT INTO blocks(poolid, blockheight, networkdifficulty, status, type, transactionconfirmationdata, miner, reward, effort, confirmationprogress, source, hash, created) " +
                "VALUES(@poolid, @blockheight, @networkdifficulty, @status, @type, @transactionconfirmationdata, @miner, @reward, @effort, @confirmationprogress, @source, @hash, @created)";

            con.Execute(query, mapped, tx);
        }

        public void DeleteBlock(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            var query = "DELETE FROM blocks WHERE id = @id";
            con.Execute(query, block, tx);
        }

        public void UpdateBlock(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Block>(block);

            var query = "UPDATE blocks SET blockheight = @blockheight, status = @status, type = @type, reward = @reward, effort = @effort, confirmationprogress = @confirmationprogress WHERE id = @id";
            con.Execute(query, mapped, tx);
        }

        public Block[] PageBlocks(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = ANY(@status) " +
                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Block>(query, new
                {
                    poolId,
                    status = status.Select(x => x.ToString().ToLower()).ToArray(),
                    offset = page * pageSize,
                    pageSize
                })
                .Select(mapper.Map<Block>)
                .ToArray();
        }

        public Block[] GetPendingBlocksForPool(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = @status";

            return con.Query<Entities.Block>(query, new { status = BlockStatus.Pending.ToString().ToLower(), poolid = poolId })
                .Select(mapper.Map<Block>)
                .ToArray();
        }

        public Block GetBlockBefore(IDbConnection con, string poolId, BlockStatus[] status, DateTime before)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = ANY(@status) AND created < @before " +
                "ORDER BY created DESC FETCH NEXT (1) ROWS ONLY";

            return con.Query<Entities.Block>(query, new
                {
                    poolId,
                    before,
                    status = status.Select(x => x.ToString().ToLower()).ToArray()
                })
                .Select(mapper.Map<Block>)
                .FirstOrDefault();
        }
    }
}
