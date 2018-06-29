

using System;
using XPool.config;

namespace XPool.restful.Responses
{
    public class BalanceChange
    {
        public string PoolId { get; set; }
        public string Coin { get; set; }
        public string Address { get; set; }
        public decimal Amount { get; set; }
        public string Usage { get; set; }
        public DateTime Created { get; set; }
    }
}
