using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using XPool.extensions;

namespace XPool.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider() : base(2)
        {
        }
    }
}
