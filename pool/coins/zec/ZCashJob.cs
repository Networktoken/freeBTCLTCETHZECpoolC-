

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.ZCash.DaemonResponses;
using XPool.config;
using XPool.utils;
using XPool.core.crypto;
using XPool.core.crypto.hash.algorithm;
using XPool.core.crypto.hash.equihash;
using XPool.extensions;
using XPool.core.stratumproto;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace XPool.Blockchain.ZCash
{
    public class ZCashJob : BitcoinJob<ZCashBlockTemplate>
    {
        protected ZCashCoinbaseTxConfig coinbaseTxConfig;
        protected decimal blockReward;
        protected decimal rewardFees;

        protected uint coinbaseIndex = 4294967295u;
        protected uint coinbaseSequence = 4294967295u;
        protected readonly IHashAlgorithm sha256D = new Sha256D();
        protected byte[] coinbaseInitialHash;
        protected byte[] merkleRoot;
        protected byte[] merkleRootReversed;
        protected string merkleRootReversedHex;
        protected EquihashSolver equihash = EquihashSolver.Instance.Value;

        #region Overrides of BitcoinJob<ZCashBlockTemplate>

        protected override Transaction CreateOutputTransaction()
        {
            var tx = new Transaction();

            if (coinbaseTxConfig.PayFoundersReward &&
                (coinbaseTxConfig.LastFoundersRewardBlockHeight >= BlockTemplate.Height ||
                    coinbaseTxConfig.TreasuryRewardStartBlockHeight > 0))
            {
                                if (coinbaseTxConfig.TreasuryRewardStartBlockHeight > 0 &&
                    BlockTemplate.Height >= coinbaseTxConfig.TreasuryRewardStartBlockHeight)
                {
                                        rewardToPool = new Money(Math.Round(blockReward * (1m - (coinbaseTxConfig.PercentTreasuryReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                    tx.AddOutput(rewardToPool, poolAddressDestination);

                                        var destination = FoundersAddressToScriptDestination(GetTreasuryRewardAddress());
                    var amount = new Money(Math.Round(blockReward * (coinbaseTxConfig.PercentTreasuryReward / 100m)), MoneyUnit.Satoshi);
                    tx.AddOutput(amount, destination);
                }

                else
                {
                                        rewardToPool = new Money(Math.Round(blockReward * (1m - (coinbaseTxConfig.PercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                    tx.AddOutput(rewardToPool, poolAddressDestination);

           
                 
                }
            }

            else
            {
                                                rewardToPool = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
                tx.AddOutput(rewardToPool, poolAddressDestination);
            }

            return tx;
        }

        protected override void BuildCoinbase()
        {
            var script = TxIn.CreateCoinbase((int) BlockTemplate.Height).ScriptSig;

                        txOut = CreateOutputTransaction();

            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                                bs.ReadWrite(ref txVersion);

                                bs.ReadWriteAsVarInt(ref txInputCount);
                bs.ReadWrite(ref sha256Empty);
                bs.ReadWrite(ref coinbaseIndex);
                bs.ReadWrite(ref script);
                bs.ReadWrite(ref coinbaseSequence);

                                var txOutBytes = SerializeOutputTransaction(txOut);
                bs.ReadWrite(ref txOutBytes);

                                bs.ReadWrite(ref txLockTime);

                                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
                coinbaseInitialHash = sha256D.Digest(coinbaseInitial);
            }
        }

        public override void Init(ZCashBlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, XPoolConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, BitcoinNetworkType networkType,
            bool isPoS, double shareMultiplier, decimal blockrewardMultiplier,
            IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Assertion.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));
            Assertion.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Assertion.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
            Assertion.RequiresNonNull(headerHasher, nameof(headerHasher));
            Assertion.RequiresNonNull(blockHasher, nameof(blockHasher));
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            this.networkType = networkType;

            if (ZCashConstants.CoinbaseTxConfig.TryGetValue(poolConfig.Coin.Type, out var coinbaseTx))
                coinbaseTx.TryGetValue(networkType, out coinbaseTxConfig);

            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = (double) new BigRational(coinbaseTxConfig.Diff1b, BlockTemplate.Target.HexToByteArray().ReverseArray().ToBigInteger());

            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;

            if (!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseArray()
                .ToHexString();

            blockReward = blockTemplate.Subsidy.Miner * BitcoinConstants.SatoshisPerBitcoin;

            if (coinbaseTxConfig?.PayFoundersReward == true)
            {
                var founders = blockTemplate.Subsidy.Founders ?? blockTemplate.Subsidy.Community;

                if (!founders.HasValue)
                    throw new Exception("Error, founders reward missing for block template");

                blockReward = (blockTemplate.Subsidy.Miner + founders.Value) * BitcoinConstants.SatoshisPerBitcoin;
            }

            rewardFees = blockTemplate.Transactions.Sum(x => x.Fee);

            BuildCoinbase();

                        var txHashes = new List<uint256> { new uint256(coinbaseInitialHash) };
            txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.Hash.HexToByteArray().ReverseArray())));

                        merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseArray();
            merkleRootReversed = merkleRoot.ReverseArray();
            merkleRootReversedHex = merkleRootReversed.ToHexString();

            jobParams = new object[]
            {
                JobId,
                BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
                previousBlockHashReversedHex,
                merkleRootReversedHex,
                sha256Empty.ToHexString(),                 BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
                BlockTemplate.Bits.HexToByteArray().ReverseArray().ToHexString(),
                false
            };
        }

        #endregion

        public override (Share Share, string BlockHex) ProcessShare(StratumClient worker, string extraNonce2, string nTime, string solution)
        {
            Assertion.RequiresNonNull(worker, nameof(worker));
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(solution), $"{nameof(solution)} must not be empty");

            var context = worker.GetContextAs<BitcoinWorkerContext>();

                        if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime.HexToByteArray().ReverseArray().ToHexString(), NumberStyles.HexNumber);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset) clock.Now).ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            var nonce = context.ExtraNonce1 + extraNonce2;

                        if (nonce.Length != 64)
                throw new StratumException(StratumError.Other, "incorrect size of extraNonce2");

                        if (solution.Length != 2694)
                throw new StratumException(StratumError.Other, "incorrect size of solution");

                        if (!RegisterSubmit(nonce, solution))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, nonce, nTimeInt, solution);
        }

        protected virtual byte[] SerializeHeader(uint nTime, string nonce)
        {
            var blockHeader = new ZCashBlockHeader
            {
                Version = (int) BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                NTime = nTime,
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        protected byte[] SerializeBlock(byte[] header, byte[] coinbase, byte[] solution)
        {
            var transactionCount = (uint) BlockTemplate.Transactions.Length + 1;             var rawTransactionBuffer = BuildRawTransactionBuffer();

            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref header);
                bs.ReadWrite(ref solution);
                bs.ReadWriteAsVarInt(ref transactionCount);
                bs.ReadWrite(ref coinbase);
                bs.ReadWrite(ref rawTransactionBuffer);

                return stream.ToArray();
            }
        }

        protected virtual (Share Share, string BlockHex) ProcessShareInternal(StratumClient worker, string nonce,
            uint nTime, string solution)
        {
            var context = worker.GetContextAs<BitcoinWorkerContext>();
            var solutionBytes = solution.HexToByteArray();

                        var headerBytes = SerializeHeader(nTime, nonce); 
                        if (!equihash.Verify(headerBytes, solutionBytes.Skip(3).ToArray()))                 throw new StratumException(StratumError.Other, "invalid solution");

                        var headerSolutionBytes = headerBytes.Concat(solutionBytes).ToArray();
            var headerHash = headerHasher.Digest(headerSolutionBytes, (ulong) nTime);
            var headerHashReversed = headerHash.ToReverseArray();
            var headerValue = new uint256(headerHash);

                        var shareDiff = (double) new BigRational(coinbaseTxConfig.Diff1b, headerHash.ToBigInteger()) * shareMultiplier;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

                        var isBlockCandidate = headerValue <= blockTargetValue;

                        if (!isBlockCandidate && ratio < 0.99)
            {
                                if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                                        stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty,
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;
                result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
                result.BlockHash = headerHashReversed.ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbaseInitial, solutionBytes);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }

        protected bool RegisterSubmit(string nonce, string solution)
        {
            lock(submissions)
            {
                var key = nonce.ToLower() + solution.ToLower();
                if (submissions.Contains(key))
                    return false;

                submissions.Add(key);
                return true;
            }
        }

        public string GetFoundersRewardAddress()
        {
            var maxHeight = coinbaseTxConfig.LastFoundersRewardBlockHeight;

            var addressChangeInterval = (maxHeight + (ulong) coinbaseTxConfig.FoundersRewardAddresses.Length) / (ulong) coinbaseTxConfig.FoundersRewardAddresses.Length;
            var index = BlockTemplate.Height / addressChangeInterval;

            var address = coinbaseTxConfig.FoundersRewardAddresses[index];
            return address;
        }

        protected string GetTreasuryRewardAddress()
        {
            var index = (int) Math.Floor((BlockTemplate.Height - coinbaseTxConfig.TreasuryRewardStartBlockHeight) /
                coinbaseTxConfig.TreasuryRewardAddressChangeInterval % coinbaseTxConfig.TreasuryRewardAddresses.Length);

            var address = coinbaseTxConfig.TreasuryRewardAddresses[index];
            return address;
        }

        public static IDestination FoundersAddressToScriptDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(2).Take(20).ToArray();
            var result = new ScriptId(hash);
            return result;
        }
    }
}
