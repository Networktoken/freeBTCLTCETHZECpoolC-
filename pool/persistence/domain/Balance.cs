

using System;
using XPool.config;

namespace XPool.Persistence.Model
{
    public class Balance
    {
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }

                                public decimal Amount { get; set; }

        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
    }
}
