using System;

namespace XPool.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, string parityChainResponse,
            out EthereumNetworkType networkType, out ParityChainType chainType)
        {
                        if (int.TryParse(netVersionResponse, out var netWorkTypeInt))
            {
                networkType = (EthereumNetworkType) netWorkTypeInt;

                if (!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                    networkType = EthereumNetworkType.Unknown;
            }

            else
                networkType = EthereumNetworkType.Unknown;

                        if (!Enum.TryParse(parityChainResponse, true, out chainType))
            {
                if (parityChainResponse.ToLower() == "ethereum classic")
                    chainType = ParityChainType.Classic;
                else
                    chainType = ParityChainType.Unknown;
            }

            if (chainType == ParityChainType.Foundation)
                chainType = ParityChainType.Mainnet;
        }
    }
}
