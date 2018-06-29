using System;
using System.Collections.Generic;
using System.Text;

namespace XPool.Blockchain.Ethereum.Configuration
{
    public class EthereumDaemonEndpointConfigExtra
    {
                                public int? PortWs { get; set; }

                                public string HttpPathWs { get; set; }

                                public bool SslWs { get; set; }
    }
}
