﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using NBitcoin;

namespace XPool.Blockchain.Ethereum
{
    public class EthereumBlockTemplate
    {
                                public ulong Height { get; set; }

                                public string Header { get; set; }

                                public string Seed { get; set; }

                                public string Target { get; set; }

                                public string ParentHash { get; set; }

                                public ulong Difficulty { get; set; }
    }
}
