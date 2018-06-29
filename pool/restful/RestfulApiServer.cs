using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using XPool.restful.extensions;
using XPool.restful.Responses;
using XPool.Blockchain;
using XPool.config;
using XPool.extensions;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Assertion = XPool.utils.Assertion;

namespace XPool.restful
{
    public class RestfulApiServer
    {
        public RestfulApiServer(
            IMapper mapper,
            IConnectionFactory cf,
            IBlockRepository blocksRepo,
            IPaymentRepository paymentsRepo,
            IStatsRepository statsRepo,
            IMasterClock clock,
            IMessageBus messageBus)
        {
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(statsRepo, nameof(statsRepo));
            Assertion.RequiresNonNull(blocksRepo, nameof(blocksRepo));
            Assertion.RequiresNonNull(paymentsRepo, nameof(paymentsRepo));
            Assertion.RequiresNonNull(mapper, nameof(mapper));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(messageBus, nameof(messageBus));

            this.cf = cf;
            this.statsRepo = statsRepo;
            this.blocksRepo = blocksRepo;
            this.paymentsRepo = paymentsRepo;
            this.mapper = mapper;
            this.clock = clock;

            messageBus.Listen<BlockNotification>().Subscribe(OnBlockNotification);

            requestMap = new Dictionary<Regex, Func<HttpContext, Match, Task>>
            {
                { new Regex("^/api/pools$", RegexOptions.Compiled), GetPoolInfosAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/performance$", RegexOptions.Compiled), GetPoolPerformanceAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/miners$", RegexOptions.Compiled), PagePoolMinersAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/blocks$", RegexOptions.Compiled), PagePoolBlocksPagedAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/payments$", RegexOptions.Compiled), PagePoolPaymentsAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)$", RegexOptions.Compiled), GetPoolInfoAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/miners/(?<address>[^/]+)/payments$", RegexOptions.Compiled), PageMinerPaymentsAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/miners/(?<address>[^/]+)/balancechanges$", RegexOptions.Compiled), PageMinerBalanceChangesAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/miners/(?<address>[^/]+)/performance$", RegexOptions.Compiled), GetMinerPerformanceAsync },
                { new Regex("^/api/pools/(?<poolId>[^/]+)/miners/(?<address>[^/]+)$", RegexOptions.Compiled), GetMinerInfoAsync },
            };

            requestMapAdmin = new Dictionary<Regex, Func<HttpContext, Match, Task>>
            {
                { new Regex("^/api/admin/forcegc$", RegexOptions.Compiled), HandleForceGcAsync },
                { new Regex("^/api/admin/stats/gc$", RegexOptions.Compiled), HandleGcStatsAsync },
            };
        }

        private readonly IConnectionFactory cf;
        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IMapper mapper;
        private readonly IMasterClock clock;

        private XPoolConfig clusterConfig;
        private IWebHost webHost;
        private IWebHost webHostAdmin;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private static readonly Encoding encoding = new UTF8Encoding(false);

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Dictionary<Regex, Func<HttpContext, Match, Task>> requestMap;
        private readonly Dictionary<Regex, Func<HttpContext, Match, Task>> requestMapAdmin;

        private PoolConfig GetPool(HttpContext context, Match m)
        {
            var poolId = m.Groups["poolId"]?.Value;

            if (!string.IsNullOrEmpty(poolId))
            {
                var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

                if (pool != null)
                    return pool;
            }

            context.Response.StatusCode = 404;
            return null;
        }

        private async Task SendJsonAsync(HttpContext context, object response)
        {
            context.Response.ContentType = "application/json";

                        context.Response.Headers.Add("Access-Control-Allow-Origin", new StringValues("*"));
            context.Response.Headers.Add("Access-Control-Allow-Methods", new StringValues("GET, POST, DELETE, PUT, OPTIONS, HEAD"));

            using (var stream = context.Response.Body)
            {
                using (var writer = new StreamWriter(stream, encoding))
                {
                    serializer.Serialize(writer, response);

                                        await writer.WriteLineAsync();
                    await writer.FlushAsync();
                }
            }
        }

        private void OnBlockNotification(BlockNotification notification)
        {
        }

        #region API

        private async Task HandleRequest(HttpContext context)
        {
            var request = context.Request;

            try
            {
                logger.Debug(() => $"Processing request {request.GetEncodedPathAndQuery()}");

                foreach (var path in requestMap.Keys)
                {
                    var m = path.Match(request.Path);

                    if (m.Success)
                    {
                        var handler = requestMap[path];
                        await handler(context, m);
                        return;
                    }
                }

                context.Response.StatusCode = 404;
            }

            catch(Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        private WorkerPerformanceStatsContainer[] GetMinerPerformanceInternal(string mode, PoolConfig pool, string address)
        {
            Persistence.Model.Projections.WorkerPerformanceStatsContainer[] stats;
            var end = clock.Now;

            if (mode == "day" || mode != "month")
            {
                                if(end.Minute < 30)
                    end = end.AddHours(-1);

                end = end.AddMinutes(-end.Minute);
                end = end.AddSeconds(-end.Second);

                var start = end.AddDays(-1);

                stats = cf.Run(con => statsRepo.GetMinerPerformanceBetweenHourly(
                    con, pool.Id, address, start, end));
            }

            else
            {
                if(end.Hour < 12)
                    end = end.AddDays(-1);

                end = end.Date;

                                var start = end.AddMonths(-1);

                stats = cf.Run(con => statsRepo.GetMinerPerformanceBetweenDaily(
                    con, pool.Id, address, start, end));
            }

                        var result = mapper.Map<WorkerPerformanceStatsContainer[]>(stats);
            return result;
        }

        private async Task GetPoolInfosAsync(HttpContext context, Match m)
        {
            var response = new GetPoolsResponse
            {
                Pools = clusterConfig.Pools.Where(x=> x.Enabled).Select(config =>
                {
                                        var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, config.Id));

                                        var result = config.ToPoolInfo(mapper, stats);

                                        result.TotalPaid = cf.Run(con => statsRepo.GetTotalPoolPayments(con, config.Id));
#if DEBUG
                    var from = new DateTime(2018, 1, 6, 16, 0, 0);
#else
                    var from = clock.Now.AddDays(-1);
#endif
                    result.TopMiners = cf.Run(con => statsRepo.PagePoolMinersByHashrate(
                            con, config.Id, from, 0, 15))
                        .Select(mapper.Map<MinerPerformanceStats>)
                        .ToArray();

                    return result;
                }).ToArray()
            };

            await SendJsonAsync(context, response);
        }

        private async Task GetPoolInfoAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

                        var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, pool.Id));

            var response = new GetPoolResponse
            {
                Pool = pool.ToPoolInfo(mapper, stats)
            };

                        response.Pool.TotalPaid = cf.Run(con => statsRepo.GetTotalPoolPayments(con, pool.Id));
#if DEBUG
            var from = new DateTime(2018, 1, 7, 16, 0, 0);
#else
            var from = clock.Now.AddDays(-1);
#endif

            response.Pool.TopMiners = cf.Run(con => statsRepo.PagePoolMinersByHashrate(
                    con, pool.Id, from, 0, 15))
                .Select(mapper.Map<MinerPerformanceStats>)
                .ToArray();

            await SendJsonAsync(context, response);
        }

        private async Task GetPoolPerformanceAsync(HttpContext context, Match m)
        {

            var pool = GetPool(context, m);
            if (pool == null)
                return;

                        var end = clock.Now;
            var start = end.AddDays(-1);

            var stats = cf.Run(con => statsRepo.GetPoolPerformanceBetweenHourly(
                con, pool.Id, start, end));

            var response = new GetPoolStatsResponse
            {
                Stats = stats.Select(mapper.Map<AggregatedPoolStats>).ToArray()
            };

            await SendJsonAsync(context, response);
        }

        private async Task PagePoolMinersAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

                        var end = clock.Now;
            var start = end.AddDays(-1);

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var miners = cf.Run(con => statsRepo.PagePoolMinersByHashrate(
                    con, pool.Id, start, page, pageSize))
                .Select(mapper.Map<MinerPerformanceStats>)
                .ToArray();

            await SendJsonAsync(context, miners);
        }

        private async Task PagePoolBlocksPagedAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var blocks = cf.Run(con => blocksRepo.PageBlocks(con, pool.Id,
                    new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned }, page, pageSize))
                .Select(mapper.Map<Responses.Block>)
                .ToArray();

                        CoinMetaData.BlockInfoLinks.TryGetValue(pool.Coin.Type, out var blockInfobaseDict);

            foreach(var block in blocks)
            {
                                if (blockInfobaseDict != null)
                {
                    blockInfobaseDict.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : string.Empty, out var blockInfobaseUrl);

                    if (!string.IsNullOrEmpty(blockInfobaseUrl))
                    {
                        if(blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                        else if(blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                    }
                }
            }

            await SendJsonAsync(context, blocks);
        }

        private async Task PagePoolPaymentsAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var payments = cf.Run(con => paymentsRepo.PagePayments(
                    con, pool.Id, null, page, pageSize))
                .Select(mapper.Map<Responses.Payment>)
                .ToArray();

                        CoinMetaData.TxInfoLinks.TryGetValue(pool.Coin.Type, out var txInfobaseUrl);
            CoinMetaData.AddressInfoLinks.TryGetValue(pool.Coin.Type, out var addressInfobaseUrl);

            foreach (var payment in payments)
            {
                                if (!string.IsNullOrEmpty(txInfobaseUrl))
                    payment.TransactionInfoLink = string.Format(txInfobaseUrl, payment.TransactionConfirmationData);

                                if (!string.IsNullOrEmpty(addressInfobaseUrl))
                    payment.AddressInfoLink = string.Format(addressInfobaseUrl, payment.Address);
            }

            await SendJsonAsync(context, payments);
        }

        private async Task GetMinerInfoAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var address = m.Groups["address"]?.Value;
            if (string.IsNullOrEmpty(address))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var perfMode = context.GetQueryParameter<string>("perfMode", "day");

            var statsResult = cf.RunTx((con, tx) =>
                statsRepo.GetMinerStats(con, tx, pool.Id, address), true, IsolationLevel.Serializable);

            MinerStats stats = null;

            if (statsResult != null)
            {
                stats = mapper.Map<MinerStats>(statsResult);

                                if (statsResult.LastPayment != null)
                {
                                        stats.LastPayment = statsResult.LastPayment.Created;

                                        if (CoinMetaData.TxInfoLinks.TryGetValue(pool.Coin.Type, out var baseUrl))
                        stats.LastPaymentLink = string.Format(baseUrl, statsResult.LastPayment.TransactionConfirmationData);
                }

                stats.PerformanceSamples = GetMinerPerformanceInternal(perfMode, pool, address);
            }

            await SendJsonAsync(context, stats);
        }

        private async Task PageMinerPaymentsAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var address = m.Groups["address"]?.Value;
            if (string.IsNullOrEmpty(address))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var payments = cf.Run(con => paymentsRepo.PagePayments(
                    con, pool.Id, address, page, pageSize))
                .Select(mapper.Map<Responses.Payment>)
                .ToArray();

                        CoinMetaData.TxInfoLinks.TryGetValue(pool.Coin.Type, out var txInfobaseUrl);
            CoinMetaData.AddressInfoLinks.TryGetValue(pool.Coin.Type, out var addressInfobaseUrl);

            foreach (var payment in payments)
            {
                                if (!string.IsNullOrEmpty(txInfobaseUrl))
                    payment.TransactionInfoLink = string.Format(txInfobaseUrl, payment.TransactionConfirmationData);

                                if (!string.IsNullOrEmpty(addressInfobaseUrl))
                    payment.AddressInfoLink = string.Format(addressInfobaseUrl, payment.Address);
            }

            await SendJsonAsync(context, payments);
        }

        private async Task PageMinerBalanceChangesAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var address = m.Groups["address"]?.Value;
            if (string.IsNullOrEmpty(address))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var balanceChanges = cf.Run(con => paymentsRepo.PageBalanceChanges(
                    con, pool.Id, address, page, pageSize))
                .Select(mapper.Map<Responses.BalanceChange>)
                .ToArray();

            await SendJsonAsync(context, balanceChanges);
        }

        private async Task GetMinerPerformanceAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var address = m.Groups["address"]?.Value;
            if (string.IsNullOrEmpty(address))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var mode = context.GetQueryParameter<string>("mode", "day").ToLower();             var result = GetMinerPerformanceInternal(mode, pool, address);

            await SendJsonAsync(context, result);
        }

        private async Task HandleForceGcAsync(HttpContext context, Match m)
        {
            GC.Collect(2, GCCollectionMode.Forced);

            await SendJsonAsync(context, true);
        }

        private async Task HandleGcStatsAsync(HttpContext context, Match m)
        {
                        Engine.gcStats.GcGen0 = GC.CollectionCount(0);
            Engine.gcStats.GcGen1 = GC.CollectionCount(1);
            Engine.gcStats.GcGen2 = GC.CollectionCount(2);
            Engine.gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

            await SendJsonAsync(context, Engine.gcStats);
        }

        private void StartApi(XPoolConfig clusterConfig)
        {
            var address = clusterConfig.Api?.ListenAddress != null
                ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                : IPAddress.Parse("127.0.0.1");

            var port = clusterConfig.Api?.Port ?? 4000;

            webHost = new WebHostBuilder()
                .Configure(app => { app.Run(HandleRequest); })
                .UseKestrel(options => { options.Listen(address, port); })
                .Build();

            webHost.Start();

            logger.Info(() => $"API Online @ {address}:{port}");
        }

        #endregion 
        #region Admin API

        private async Task HandleRequestAdmin(HttpContext context)
        {
            var request = context.Request;

            try
            {
                logger.Debug(() => $"Processing request {request.GetEncodedPathAndQuery()}");

                foreach (var path in requestMapAdmin.Keys)
                {
                    var m = path.Match(request.Path);

                    if (m.Success)
                    {
                        var handler = requestMapAdmin[path];
                        await handler(context, m);
                        return;
                    }
                }

                context.Response.StatusCode = 404;
            }

            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        private void StartAdminApi(XPoolConfig clusterConfig)
        {
            var address = clusterConfig.Api?.ListenAddress != null
                ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                : IPAddress.Parse("127.0.0.1");

            var port = clusterConfig.Api?.AdminPort ?? 4001;

            webHostAdmin = new WebHostBuilder()
                .Configure(app => { app.Run(HandleRequestAdmin); })
                .UseKestrel(options => { options.Listen(address, port); })
                .Build();

            webHostAdmin.Start();

            logger.Info(() => $"Admin API Online @ {address}:{port}");
        }

        #endregion 
        #region API-Surface

        public void Start(XPoolConfig clusterConfig)
        {
            Assertion.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            this.clusterConfig = clusterConfig;

            logger.Info(() => $"Launching ...");
            StartApi(clusterConfig);
            StartAdminApi(clusterConfig);
        }

        #endregion 
    }
}
