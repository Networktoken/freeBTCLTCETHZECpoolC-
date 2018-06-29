

using System;
using System.Data;
using XPool.Persistence.Model;
using XPool.Persistence.Model.Projections;
using MinerStats = XPool.Persistence.Model.Projections.MinerStats;

namespace XPool.Persistence.Repositories
{
    public interface IStatsRepository
    {
        void InsertPoolStats(IDbConnection con, IDbTransaction tx, PoolStats stats);
        void InsertMinerWorkerPerformanceStats(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats);
        PoolStats GetLastPoolStats(IDbConnection con, string poolId);
        decimal GetTotalPoolPayments(IDbConnection con, string poolId);
        PoolStats[] GetPoolPerformanceBetweenHourly(IDbConnection con, string poolId, DateTime start, DateTime end);
        MinerStats GetMinerStats(IDbConnection con, IDbTransaction tx, string poolId, string miner);
        MinerWorkerPerformanceStats[] PagePoolMinersByHashrate(IDbConnection con, string poolId, DateTime from, int page, int pageSize);
        WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenHourly(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
        WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenDaily(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
    }
}
