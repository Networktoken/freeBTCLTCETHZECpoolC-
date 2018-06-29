using System;
using System.Linq;
using System.Threading;
using XPool.extensions;

namespace XPool.Blockchain.ZCash
{
    public class ZCashExtraNonceProvider : ExtraNonceProviderBase
    {
        public ZCashExtraNonceProvider() : base(3)
        {
        }
    }
}
