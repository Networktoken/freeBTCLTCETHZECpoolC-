

using System;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace XPool.Blockchain.Bitcoin
{
    public static class BitcoinUtils
    {
                                                                                public static IDestination AddressToDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var pubKeyHash = decoded.Skip(1).Take(20).ToArray();
            var result = new KeyId(pubKeyHash);
            return result;
        }
    }
}
