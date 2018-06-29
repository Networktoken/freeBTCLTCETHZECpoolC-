

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using XPool.core.accession;
using XPool.Blockchain;
using XPool.config;
using XPool.extensions;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Repositories;
using XPool.core.stratumproto;
using XPool.utils;
using XPool.core.diffadjust;
using Newtonsoft.Json;
using NLog;
using Assertion = XPool.utils.Assertion;

namespace XPool.core
{
    public abstract class PoolBase : StratumServer,
        IMiningPool
    {
        protected PoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            WebhookNotificationService notificationService) : base(ctx, clock)
        {
            Assertion.RequiresNonNull(ctx, nameof(ctx));
            Assertion.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(statsRepo, nameof(statsRepo));
            Assertion.RequiresNonNull(mapper, nameof(mapper));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(messageBus, nameof(messageBus));
            Assertion.RequiresNonNull(notificationService, nameof(notificationService));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.notificationService = notificationService;
        }

        protected PoolStats poolStats = new PoolStats();
        protected readonly JsonSerializerSettings serializerSettings;
        protected readonly WebhookNotificationService notificationService;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        protected readonly IMapper mapper;
        protected readonly IMessageBus messageBus;
        protected readonly CompositeDisposable disposables = new CompositeDisposable();
        protected BlockchainStats blockchainStats;
        protected PoolConfig poolConfig;
        protected const int VarDiffSampleCount = 32;
        protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
        protected static readonly Regex regexStaticDiff = new Regex(@"d=(\d*(\.\d+)?)", RegexOptions.Compiled);
        protected const string PasswordControlVarsSeparator = ";";

        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers =
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected override string LogCat => "Pool";

        protected abstract Task SetupJobManager(CancellationToken ct);
        protected abstract WorkerContextBase CreateClientContext();

        protected double? GetStaticDiffFromPassparts(string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return null;

            foreach (var part in parts)
            {
                var m = regexStaticDiff.Match(part);

                if (m.Success)
                {
                    var str = m.Groups[1].Value.Trim();
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var diff))
                        return diff;
                }
            }

            return null;
        }

        protected override void OnConnect(StratumClient client)
        {
            var context = CreateClientContext();

            var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, poolConfig.EnableInternalStratum == true ? poolEndpoint.VarDiff : null, clock);
            client.SetContext(context);

            if (context.VarDiff != null)
            {
                lock (context.VarDiff)
                {
                    StartVarDiffIdleUpdate(client, poolEndpoint);
                }
            }

            EnsureNoZombieClient(client);
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            Observable.Timer(clock.Now.AddSeconds(10))
                .Take(1)
                .Subscribe(_ =>
                {
                    if (!client.LastReceive.HasValue)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        #region VarDiff

        protected void UpdateVarDiff(StratumClient client, bool isIdleUpdate = false)
        {
            var context = client.GetContextAs<WorkerContextBase>();

            if (context.VarDiff != null)
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Updating VarDiff" + (isIdleUpdate ? " [idle]" : string.Empty));

                VarDiffManager varDiffManager;
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                lock (varDiffManagers)
                {
                    if (!varDiffManagers.TryGetValue(poolEndpoint, out varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(poolEndpoint.VarDiff, clock);
                        varDiffManagers[poolEndpoint] = varDiffManager;
                    }
                }

                lock (context.VarDiff)
                {
                    StartVarDiffIdleUpdate(client, poolEndpoint);

                    var newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty, isIdleUpdate);

                    if (newDiff != null)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {Math.Round(newDiff.Value, 2)}");

                        OnVarDiffUpdate(client, newDiff.Value);
                    }
                }
            }
        }

        private void StartVarDiffIdleUpdate(StratumClient client, PoolEndpoint poolEndpoint)
        {
            var interval = poolEndpoint.VarDiff.TargetTime;
            var shareReceivedFromClient = messageBus.Listen<ClientShare>().Where(x => x.Share.PoolId == poolConfig.Id && x.Client == client);

            Observable
                .Timer(TimeSpan.FromSeconds(interval))
                .TakeUntil(shareReceivedFromClient)
                .Take(1)
                .Where(x => client.IsAlive)
                .Subscribe(_ => UpdateVarDiff(client, true));
        }

        protected virtual void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<WorkerContextBase>();
            context.EnqueueNewDifficulty(newDiff);
        }

        #endregion 
        protected void SetupBanning(XPoolConfig clusterConfig)
        {
            if (poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual void InitStats()
        {

            LoadStats();
        }

        private void LoadStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Loading pool stats");

                var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, poolConfig.Id));

                if (stats != null)
                {
                    poolStats = mapper.Map<PoolStats>(stats);
                    blockchainStats = mapper.Map<BlockchainStats>(stats);
                }
            }

            catch (Exception ex)
            {
                logger.Warn(ex, () => $"[{LogCat}] Unable to load pool stats");
            }
        }

        protected void ConsiderBan(StratumClient client, WorkerContextBase context, PoolShareBasedBanningConfig config)
        {
            var totalShares = context.Stats.ValidShares + context.Stats.InvalidShares;

            if (totalShares > config.CheckThreshold)
            {
                var ratioBad = (double)context.Stats.InvalidShares / totalShares;

                if (ratioBad < config.InvalidPercent / 100.0)
                {
                    context.Stats.ValidShares = 0;
                    context.Stats.InvalidShares = 0;
                }

                else
                {
                    if (poolConfig.Banning?.Enabled == true &&
                        (clusterConfig.Banning?.BanOnInvalidShares.HasValue == false ||
                         clusterConfig.Banning?.BanOnInvalidShares == true))
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                        banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                        DisconnectClient(client);
                    }
                }
            }
        }

        private (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if (!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return (new IPEndPoint(listenAddress, port), pep.TcpProxyProtocol);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

 XPool Id:              {poolConfig.Id}
Coin Type:              {poolConfig.Coin.Type}
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashrate(blockchainStats.NetworkHashrate)}
Stratum Port(s):        {(poolConfig.Ports?.Any() == true ? string.Join(", ", poolConfig.Ports.Keys) : string.Empty)}
Pool Fee:               {(poolConfig.RewardRecipients?.Any() == true ? poolConfig.RewardRecipients.Sum(x => x.Percentage) : 0)}%
";

            logger.Info(() => msg);
        }

        #region API-Surface

        public PoolConfig Config => poolConfig;
        public PoolStats PoolStats => poolStats;
        public BlockchainStats NetworkStats => blockchainStats;

        public virtual void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));
            Assertion.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
        }

        public abstract double HashrateFromShares(double shares, double interval);

        public virtual async Task StartAsync(CancellationToken ct)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            try
            {
                SetupBanning(clusterConfig);
                await SetupJobManager(ct);
                InitStats();

                if (poolConfig.EnableInternalStratum == true)
                {
                    var ipEndpoints = poolConfig.Ports.Keys
                        .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
                        .ToArray();

                    StartListeners(poolConfig.Id, ipEndpoints);
                }

                logger.Info(() => $"[{LogCat}] Online");
                OutputPoolInfo();
            }

            catch (PoolStartupAbortException)
            {
                throw;
            }

            catch (TaskCanceledException)
            {
                throw;
            }

            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        #endregion     }
    }
}
