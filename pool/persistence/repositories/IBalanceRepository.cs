

using System.Data;
using XPool.config;
using XPool.Persistence.Model;

namespace XPool.Persistence.Repositories
{
    public interface IBalanceRepository
    {
        void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address, decimal amount, string usage);

        Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, decimal minimum);
    }
}
