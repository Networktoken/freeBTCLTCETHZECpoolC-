

using System.Collections.Generic;
using System.Globalization;
using XPool.Blockchain.Bitcoin;
using XPool.config;
using NBitcoin.BouncyCastle.Math;

namespace XPool.Blockchain.ZCash
{
    public class ZCashCoinbaseTxConfig
    {
        public BigInteger Diff1 { get; set; }
        public System.Numerics.BigInteger Diff1b { get; set; }

        public bool PayFoundersReward { get; set; }
        public decimal PercentFoundersReward { get; set; }
        public string[] FoundersRewardAddresses { get; set; }
        public ulong FoundersRewardSubsidySlowStartInterval { get; set; }
        public ulong FoundersRewardSubsidyHalvingInterval { get; set; }
        public ulong FoundersRewardSubsidySlowStartShift => FoundersRewardSubsidySlowStartInterval / 2;
        public ulong LastFoundersRewardBlockHeight => FoundersRewardSubsidyHalvingInterval + FoundersRewardSubsidySlowStartShift - 1;

        public decimal PercentTreasuryReward { get; set; }
        public ulong TreasuryRewardStartBlockHeight { get; set; }
        public string[] TreasuryRewardAddresses { get; set; }
        public double TreasuryRewardAddressChangeInterval { get; set; }
    }

    public class ZCashConstants
    {
        public const int TargetPaddingLength = 32;

        private static readonly Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig> ZCashCoinbaseTxConfig = new Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>
        {
            {
                BitcoinNetworkType.Main, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

               
                }
            },
            {
                BitcoinNetworkType.Test, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                 
                }
            },
            {
                BitcoinNetworkType.RegTest, new ZCashCoinbaseTxConfig
                {
                    Diff1 = new BigInteger("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", 16),
                    Diff1b = System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber),

                }
            },
        };

      

      

        public static Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>> CoinbaseTxConfig =
            new Dictionary<CoinType, Dictionary<BitcoinNetworkType, ZCashCoinbaseTxConfig>>
            {
                { CoinType.ZEC, ZCashCoinbaseTxConfig },
             
            };
    }

    public enum ZOperationStatus
    {
        Queued,
        Executing,
        Success,
        Cancelled,
        Failed
    }

    public static class ZCashCommands
    {
        public const string ZGetBalance = "z_getbalance";
        public const string ZGetTotalBalance = "z_gettotalbalance";
        public const string ZGetListAddresses = "z_listaddresses";
        public const string ZValidateAddress = "z_validateaddress";
        public const string ZShieldCoinbase = "z_shieldcoinbase";

                                        public const string ZSendMany = "z_sendmany";

        public const string ZGetOperationStatus = "z_getoperationstatus";
        public const string ZGetOperationResult = "z_getoperationresult";
    }
}
