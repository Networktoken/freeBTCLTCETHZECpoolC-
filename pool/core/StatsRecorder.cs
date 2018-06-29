﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Autofac;
using AutoMapper;
using XPool.config;
using XPool.utils;
using XPool.extensions;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using NLog;
using Polly;
using Assertion = XPool.utils.Assertion;

namespace XPool.core
{
    public class StatsRecorder
    {
        public StatsRecorder(IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IStatsRepository statsRepo)
        {
            Assertion.RequiresNonNull(ctx, nameof(ctx));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(mapper, nameof(mapper));
            Assertion.RequiresNonNull(shareRepo, nameof(shareRepo));
            Assertion.RequiresNonNull(statsRepo, nameof(statsRepo));

            this.ctx = ctx;
            this.clock = clock;
            this.cf = cf;
            this.mapper = mapper;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IStatsRepository statsRepo;
        private readonly IConnectionFactory cf;
        private readonly IMapper mapper;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new ConcurrentDictionary<string, IMiningPool>();
        private const int HashrateCalculationWindow = 1200;          private const int MinHashrateCalculationWindow = 300;          private const double HashrateBoostFactor = 1.07d;
        private XPoolConfig clusterConfig;
        private Thread thread1;
        private const int RetryCount = 4;
        private Policy readFaultPolicy;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        #region API-Surface

        public void Configure(XPoolConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;
        }

        public void AttachPool(IMiningPool pool)
        {
            pools[pool.Config.Id] = pool;
        }

        public void Start()
        {
            logger.Info(() => "Online");

            thread1 = new Thread(() =>
            {
                                Thread.Sleep(TimeSpan.FromSeconds(10));

                var interval = TimeSpan.FromMinutes(5);

                while (true)
                {
                    try
                    {
                        UpdatePoolHashrates();
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }

                    var waitResult = stopEvent.WaitOne(interval);

                                        if (waitResult)
                        break;
                }
            });

            thread1.Name = "StatsRecorder";
            thread1.Start();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            stopEvent.Set();
            thread1.Join();

            logger.Info(() => "Stopped");
        }

        #endregion 
        private void UpdatePoolHashrates()
        {
            var start = clock.Now;
            var target = start.AddSeconds(-HashrateCalculationWindow);

            var stats = new MinerWorkerPerformanceStats
            {
                Created = start
            };

            var poolIds = pools.Keys;

            foreach (var poolId in poolIds)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var pool = pools[poolId];

                                var result = readFaultPolicy.Execute(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreated(con, poolId, target, start)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                if (result.Length > 0)
                {
                                        var windowActual = (result.Max(x => x.LastShare) - result.Min(x => x.FirstShare)).TotalSeconds;

                    if (windowActual >= MinHashrateCalculationWindow)
                    {
                        var poolHashesAccumulated = result.Sum(x => x.Sum);
                        var poolHashesCountAccumulated = result.Sum(x => x.Count);
                        var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, windowActual) * HashrateBoostFactor;

                                                pool.PoolStats.ConnectedMiners = byMiner.Length;
                        pool.PoolStats.PoolHashrate = (ulong) Math.Ceiling(poolHashrate);
                        pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / windowActual);
                    }
                }

                                cf.RunTx((con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = start
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);

                    statsRepo.InsertPoolStats(con, tx, mapped);
                });

                if (result.Length == 0)
                    continue;

                                foreach (var minerHashes in byMiner)
                {
                    cf.RunTx((con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        foreach (var item in minerHashes)
                        {
                                                        var windowActual = (minerHashes.Max(x => x.LastShare) - minerHashes.Min(x => x.FirstShare)).TotalSeconds;

                            if (windowActual >= MinHashrateCalculationWindow)
                            {
                                var hashrate = pool.HashrateFromShares(item.Sum, windowActual) * HashrateBoostFactor;

                                                                stats.Hashrate = hashrate;
                                stats.Worker = item.Worker;
                                stats.SharesPerSecond = (double) item.Count / windowActual;

                                                                statsRepo.InsertMinerWorkerPerformanceStats(con, tx, stats);
                            }
                        }
                    });
                }
            }
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .Retry(RetryCount, OnPolicyRetry);

            readFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
