

using System;
using XPool.config;

namespace XPool.Persistence.Model
{
    public class BalanceChange
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }

                                public decimal Amount { get; set; }

        public string Usage { get; set; }

        public DateTime Created { get; set; }
    }
}
