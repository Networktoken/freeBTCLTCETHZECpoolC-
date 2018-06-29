

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.config;
using XPool.core.crypto;
using XPool.extensions;
using XPool.core.stratumproto;
using XPool.utils;
using NBitcoin;
using NBitcoin.DataEncoders;
using Assertion = XPool.utils.Assertion;
using Transaction = NBitcoin.Transaction;

namespace XPool.Blockchain.Bitcoin
{
    public class BitcoinJob<TBlockTemplate>
        where TBlockTemplate : BlockTemplate
    {
        protected IHashAlgorithm blockHasher;
        protected XPoolConfig clusterConfig;
        protected IMasterClock clock;
        protected IHashAlgorithm coinbaseHasher;
        protected double shareMultiplier;
        protected decimal blockRewardMultiplier;
        protected int extraNoncePlaceHolderLength;
        protected IHashAlgorithm headerHasher;
        protected bool isPoS;

        protected BitcoinNetworkType networkType;
        protected IDestination poolAddressDestination;
        protected PoolConfig poolConfig;
        protected HashSet<string> submissions = new HashSet<string>();
        protected uint256 blockTargetValue;
        protected byte[] coinbaseFinal;
        protected string coinbaseFinalHex;
        protected byte[] coinbaseInitial;
        protected string coinbaseInitialHex;
        protected string[] merkleBranchesHex;
        protected MerkleTree mt;


        protected object[] jobParams;
        protected string previousBlockHashReversedHex;
        protected Money rewardToPool;
        protected Transaction txOut;

        protected static byte[] scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes("/MiningCore/"))).ToBytes();

        protected static byte[] sha256Empty = Enumerable.Repeat((byte)0, 32).ToArray();
        protected static uint txVersion = 1u;
        protected static uint txInputCount = 1u;
        protected static uint txInPrevOutIndex = (uint)(Math.Pow(2, 32) - 1);
        protected static uint txInSequence;
        protected static uint txLockTime;

        protected virtual void BuildMerkleBranches()
        {
            var transactionHashes = BlockTemplate.Transactions
                .Select(tx => (tx.TxId ?? tx.Hash)
                    .HexToByteArray()
                    .ReverseArray())
                .ToArray();

            mt = new MerkleTree(transactionHashes);

            merkleBranchesHex = mt.Steps
                .Select(x => x.ToHexString())
                .ToArray();
        }

        protected virtual void BuildCoinbase()
        {
            var sigScriptInitial = GenerateScriptSigInitial();
            var sigScriptInitialBytes = sigScriptInitial.ToBytes();

            var sigScriptLength = (uint)(
                sigScriptInitial.Length +
                extraNoncePlaceHolderLength +
                scriptSigFinalBytes.Length);

            txOut = CreateOutputTransaction();

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref txVersion);

                if (isPoS)
                {
                    var timestamp = BlockTemplate.CurTime;
                    bs.ReadWrite(ref timestamp);
                }

                bs.ReadWriteAsVarInt(ref txInputCount);
                bs.ReadWrite(ref sha256Empty);
                bs.ReadWrite(ref txInPrevOutIndex);

                bs.ReadWriteAsVarInt(ref sigScriptLength);
                bs.ReadWrite(ref sigScriptInitialBytes);

                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
            }

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref scriptSigFinalBytes);

                bs.ReadWrite(ref txInSequence);

                var txOutBytes = SerializeOutputTransaction(txOut);
                bs.ReadWrite(ref txOutBytes);

                bs.ReadWrite(ref txLockTime);

                coinbaseFinal = stream.ToArray();
                coinbaseFinalHex = coinbaseFinal.ToHexString();
            }
        }

        protected virtual byte[] SerializeOutputTransaction(Transaction tx)
        {
            var withDefaultWitnessCommitment = !string.IsNullOrEmpty(BlockTemplate.DefaultWitnessCommitment);

            var outputCount = (uint)tx.Outputs.Count;
            if (withDefaultWitnessCommitment)
                outputCount++;

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWriteAsVarInt(ref outputCount);

                long amount;
                byte[] raw;
                uint rawLength;

                if (withDefaultWitnessCommitment)
                {
                    amount = 0;
                    raw = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                    rawLength = (uint)raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                foreach (var output in tx.Outputs)
                {
                    amount = output.Value.Satoshi;
                    var outScript = output.ScriptPubKey;
                    raw = outScript.ToBytes(true);
                    rawLength = (uint)raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                return stream.ToArray();
            }
        }

        protected virtual Script GenerateScriptSigInitial()
        {
            var now = ((DateTimeOffset)clock.Now).ToUnixTimeSeconds();

            var ops = new List<Op>();

            ops.Add(Op.GetPushOp(BlockTemplate.Height));

            if (!string.IsNullOrEmpty(BlockTemplate.CoinbaseAux?.Flags))
                ops.Add(Op.GetPushOp(BlockTemplate.CoinbaseAux.Flags.HexToByteArray()));

            ops.Add(Op.GetPushOp(now));

            ops.Add(Op.GetPushOp((uint)0));

            return new Script(ops);
        }

        protected virtual Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue * blockRewardMultiplier, MoneyUnit.Satoshi);

            var tx = new Transaction();

            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination)
            {
                Value = rewardToPool
            });

            return tx;
        }

        protected bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(extraNonce1)
                .Append(extraNonce2.ToLower()).Append(nTime)
                .Append(nonce.ToLower()).ToString();

            lock (submissions)
            {
                if (submissions.Contains(key))
                    return false;

                submissions.Add(key);
                return true;
            }
        }

        protected virtual byte[] SerializeHeader(byte[] coinbaseHash, uint nTime, uint nonce)
        {
            var merkleRoot = mt.WithFirst(coinbaseHash);

#pragma warning disable 618
            var blockHeader = new BlockHeader
#pragma warning restore 618
            {
                Version = (int)BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        protected virtual (Share Share, string BlockHex) ProcessShareInternal(StratumClient worker, string extraNonce2, uint nTime, uint nonce)
        {
            var context = worker.GetContextAs<BitcoinWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;

            var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
            var coinbaseHash = coinbaseHasher.Digest(coinbase);

            var headerBytes = SerializeHeader(coinbaseHash, nTime, nonce);
            var headerHash = headerHasher.Digest(headerBytes, (ulong)nTime);
            var headerValue = new uint256(headerHash);

            var shareDiff = (double)new BigRational(BitcoinConstants.Diff1, headerHash.ToBigInteger()) * shareMultiplier;
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
                NetworkDifficulty = Difficulty * shareMultiplier,
                Difficulty = stratumDifficulty,
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;
                result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
                result.BlockHash = blockHasher.Digest(headerBytes, nTime).ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbase);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }

        protected virtual byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
        {
            var extraNonce1Bytes = extraNonce1.HexToByteArray();
            var extraNonce2Bytes = extraNonce2.HexToByteArray();

            using (var stream = new MemoryStream())
            {
                stream.Write(coinbaseInitial);
                stream.Write(extraNonce1Bytes);
                stream.Write(extraNonce2Bytes);
                stream.Write(coinbaseFinal);

                return stream.ToArray();
            }
        }

        protected virtual byte[] SerializeBlock(byte[] header, byte[] coinbase)
        {
            var transactionCount = (uint)BlockTemplate.Transactions.Length + 1; var rawTransactionBuffer = BuildRawTransactionBuffer();

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref header);
                bs.ReadWriteAsVarInt(ref transactionCount);
                bs.ReadWrite(ref coinbase);
                bs.ReadWrite(ref rawTransactionBuffer);

                if (isPoS)
                    bs.ReadWrite((byte)0);

                return stream.ToArray();
            }
        }

        protected virtual byte[] BuildRawTransactionBuffer()
        {
            using (var stream = new MemoryStream())
            {
                foreach (var tx in BlockTemplate.Transactions)
                {
                    var txRaw = tx.Data.HexToByteArray();
                    stream.Write(txRaw);
                }

                return stream.ToArray();
            }
        }

        #region API-Surface

        public TBlockTemplate BlockTemplate { get; protected set; }
        public double Difficulty { get; protected set; }

        public string JobId { get; protected set; }

        public virtual void Init(TBlockTemplate blockTemplate, string jobId,
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
            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = new Target(new NBitcoin.BouncyCastle.Math.BigInteger(BlockTemplate.Target, 16)).Difficulty;

            extraNoncePlaceHolderLength = BitcoinConstants.ExtranoncePlaceHolderLength;
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;
            this.blockRewardMultiplier = blockrewardMultiplier;

            this.coinbaseHasher = coinbaseHasher;
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
                .ReverseByteOrder()
                .ToHexString();

            BuildMerkleBranches();
            BuildCoinbase();

            jobParams = new object[]
            {
                JobId,
                previousBlockHashReversedHex,
                coinbaseInitialHex,
                coinbaseFinalHex,
                merkleBranchesHex,
                BlockTemplate.Version.ToStringHex8(),
                BlockTemplate.Bits,
                BlockTemplate.CurTime.ToStringHex8(),
                false
            };
        }

        public virtual object GetJobParams(bool isNew)
        {
            jobParams[jobParams.Length - 1] = isNew;
            return jobParams;
        }

        public virtual (Share Share, string BlockHex) ProcessShare(StratumClient worker,
            string extraNonce2, string nTime, string nonce)
        {
            Assertion.RequiresNonNull(worker, nameof(worker));
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            var context = worker.GetContextAs<BitcoinWorkerContext>();

            if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset)clock.Now).ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            if (nonce.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

            if (!RegisterSubmit(context.ExtraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, extraNonce2, nTimeInt, nonceInt);
        }

        #endregion     }
    }
}
