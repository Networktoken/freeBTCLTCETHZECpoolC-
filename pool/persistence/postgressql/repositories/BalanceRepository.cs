

using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using XPool.config;
using XPool.extensions;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using NLog;

namespace XPool.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
        public BalanceRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address, decimal amount, string usage)
        {
            logger.LogInvoke();

            var now = DateTime.UtcNow;

                        var query = "INSERT INTO balance_changes(poolid, coin, address, amount, usage, created) " +
                    "VALUES(@poolid, @coin, @address, @amount, @usage, @created)";

            var balanceChange = new Entities.BalanceChange
            {
                PoolId = poolId,
                Coin = coin.ToString(),
                Created = now,
                Address = address,
                Amount = amount,
                Usage = usage,
            };

            con.Execute(query, balanceChange, tx);

                        query = "SELECT * FROM balances WHERE poolid = @poolId AND coin = @coin AND address = @address";

            var balance = con.Query<Entities.Balance>(query, new { poolId, coin = coin.ToString(), address }, tx)
                .FirstOrDefault();

            if (balance == null)
            {
                balance = new Entities.Balance
                {
                    PoolId = poolId,
                    Coin = coin.ToString(),
                    Created = now,
                    Address = address,
                    Amount = amount,
                    Updated = now
                };

                query = "INSERT INTO balances(poolid, coin, address, amount, created, updated) " +
                    "VALUES(@poolid, @coin, @address, @amount, @created, @updated)";

                con.Execute(query, balance, tx);
            }

            else
            {
                query = "UPDATE balances SET amount = amount + @amount, updated = now() at time zone 'utc' " +
                    "WHERE poolid = @poolId AND coin = @coin AND address = @address";

                con.Execute(query, new
                {
                    poolId,
                    address,
                    coin = coin.ToString().ToUpper(),
                    amount
                }, tx);
            }
        }

        public Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, decimal minimum)
        {
            logger.LogInvoke();

            var query = "SELECT * FROM balances WHERE poolid = @poolId AND amount >= @minimum";

            return con.Query<Entities.Balance>(query, new { poolId, minimum })
                .Select(mapper.Map<Balance>)
                .ToArray();
        }
    }
}
