

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using XPool.config;

namespace XPool.Blockchain.Bitcoin
{
    public enum BitcoinNetworkType
    {
        Main = 1,
        Test,
        RegTest
    }

    public enum BitcoinTransactionCategory
    {
                                Send = 1,

                                Receive,

                                Generate,

                                Immature,

                                Orphan
    }

    public class BitcoinConstants
    {
        public const int ExtranoncePlaceHolderLength = 8;
        public const decimal SatoshisPerBitcoin = 100000000;
        public static double Pow2x32 = Math.Pow(2, 32);
        public static readonly BigInteger Diff1 = BigInteger.Parse("00ffff0000000000000000000000000000000000000000000000000000", NumberStyles.HexNumber);
        public const int CoinbaseMinConfimations = 102;

        public const string ZmqPublisherTopicBlockHash = "hashblock";
        public const string ZmqPublisherTopicTxHash = "hashtx";
        public const string ZmqPublisherTopicBlockRaw = "rawblock";
        public const string ZmqPublisherTopicTxRaw = "rawtx";
    }

    public enum BitcoinRPCErrorCode
    {
                                RPC_INVALID_REQUEST = -32600,
                        RPC_METHOD_NOT_FOUND = -32601,
        RPC_INVALID_PARAMS = -32602,
                        RPC_INTERNAL_ERROR = -32603,
        RPC_PARSE_ERROR = -32700,

                RPC_MISC_ERROR = -1,          RPC_FORBIDDEN_BY_SAFE_MODE = -2,          RPC_TYPE_ERROR = -3,          RPC_INVALID_ADDRESS_OR_KEY = -5,          RPC_OUT_OF_MEMORY = -7,          RPC_INVALID_PARAMETER = -8,          RPC_DATABASE_ERROR = -20,         RPC_DESERIALIZATION_ERROR = -22,         RPC_VERIFY_ERROR = -25,         RPC_VERIFY_REJECTED = -26,         RPC_VERIFY_ALREADY_IN_CHAIN = -27,         RPC_IN_WARMUP = -28,         RPC_METHOD_DEPRECATED = -32, 
                RPC_TRANSACTION_ERROR = RPC_VERIFY_ERROR,
        RPC_TRANSACTION_REJECTED = RPC_VERIFY_REJECTED,
        RPC_TRANSACTION_ALREADY_IN_CHAIN = RPC_VERIFY_ALREADY_IN_CHAIN,

                RPC_CLIENT_NOT_CONNECTED = -9,          RPC_CLIENT_IN_INITIAL_DOWNLOAD = -10,         RPC_CLIENT_NODE_ALREADY_ADDED = -23,         RPC_CLIENT_NODE_NOT_ADDED = -24,         RPC_CLIENT_NODE_NOT_CONNECTED = -29,         RPC_CLIENT_INVALID_IP_OR_SUBNET = -30,         RPC_CLIENT_P2P_DISABLED = -31, 
                RPC_WALLET_ERROR = -4,          RPC_WALLET_INSUFFICIENT_FUNDS = -6,          RPC_WALLET_INVALID_ACCOUNT_NAME = -11,         RPC_WALLET_KEYPOOL_RAN_OUT = -12,         RPC_WALLET_UNLOCK_NEEDED = -13,         RPC_WALLET_PASSPHRASE_INCORRECT = -14,         RPC_WALLET_WRONG_ENC_STATE = -15,         RPC_WALLET_ENCRYPTION_FAILED = -16,         RPC_WALLET_ALREADY_UNLOCKED = -17,         RPC_WALLET_NOT_FOUND = -18,         RPC_WALLET_NOT_SPECIFIED = -19,     }

    public class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<CoinType, string> Addresses = new Dictionary<CoinType, string>
        {
            {CoinType.BTC, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm"},
       
            {CoinType.LTC, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC"},
         
            {CoinType.ETH, "0xcb55abBfe361B12323eb952110cE33d5F28BeeE1"},
          
            {CoinType.ZEC, "t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7"},
           
        };
    }

    public static class BitcoinCommands
    {
        public const string GetBalance = "getbalance";
        public const string ListUnspent = "listunspent";
        public const string GetNetworkInfo = "getnetworkinfo";
        public const string GetMiningInfo = "getmininginfo";
        public const string GetPeerInfo = "getpeerinfo";
        public const string ValidateAddress = "validateaddress";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string GetBlockSubsidy = "getblocksubsidy";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockchainInfo = "getblockchaininfo";
        public const string GetBlock = "getblock";
        public const string GetTransaction = "gettransaction";
        public const string SendMany = "sendmany";
        public const string WalletPassphrase = "walletpassphrase";
        public const string WalletLock = "walletlock";

                public const string GetInfo = "getinfo";
        public const string GetDifficulty = "getdifficulty";
        public const string GetConnectionCount = "getconnectioncount";
    }
}
