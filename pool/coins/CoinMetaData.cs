using System;
using System.Collections.Generic;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.Ethereum;
using XPool.config;

namespace XPool.Blockchain
{
    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";

        public static readonly Dictionary<CoinType, Dictionary<string, string>> BlockInfoLinks = new Dictionary<CoinType, Dictionary<string, string>>
        {
            { CoinType.ETH, new Dictionary<string, string>
            {
                { string.Empty, $"https://etherscan.io/block/{BlockHeightPH}" },
                { EthereumConstants.BlockTypeUncle, $"https://etherscan.io/uncle/{BlockHeightPH}" },
            }},

            { CoinType.LTC, new Dictionary<string, string> { { string.Empty, $"https://chainz.cryptoid.info/ltc/block.dws?{BlockHeightPH}.htm" } }},

            { CoinType.BTC, new Dictionary<string, string> { { string.Empty, $"https://blockchain.info/block/{BlockHeightPH}" }}},

            { CoinType.ZEC, new Dictionary<string, string> { { string.Empty, $"https://explorer.zcha.in/blocks/{BlockHashPH}" } }},

        };

        public static readonly Dictionary<CoinType, string> TxInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.ETH, "https://etherscan.io/tx/{0}" },
            { CoinType.LTC, "https://chainz.cryptoid.info/ltc/tx.dws?{0}.htm" },
            { CoinType.BTC, "https://blockchain.info/tx/{0}" },
            { CoinType.ZEC, "https://explorer.zcha.in/transactions/{0}" },

        };

        public static readonly Dictionary<CoinType, string> AddressInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.ETH, "https://etherscan.io/address/{0}" },

            { CoinType.LTC, "https://chainz.cryptoid.info/ltc/address.dws?{0}.htm" },

            { CoinType.BTC, "https://blockchain.info/address/{0}" },

            { CoinType.ZEC, "https://explorer.zcha.in/accounts/{0}" },

        };

        private const string Ethash = "Ethash";
        private const string Cryptonight = "Cryptonight";
        private const string CryptonightLight = "Cryptonight-Light";

        public static readonly Dictionary<CoinType, Func<CoinType, string, string>> CoinAlgorithm = new Dictionary<CoinType, Func<CoinType, string, string>>
        {
            { CoinType.ETH, (coin, alg)=> Ethash },

            { CoinType.LTC, BitcoinProperties.GetAlgorithm },

            { CoinType.BTC, BitcoinProperties.GetAlgorithm },

            { CoinType.ZEC, BitcoinProperties.GetAlgorithm },

        };
    }
}
