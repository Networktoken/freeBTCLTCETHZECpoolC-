

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using AutoMapper;
using XPool.config;
using XPool.extensions;
using XPool.core.bus;
using XPool.Persistence;
using XPool.Persistence.Model;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Assertion = XPool.utils.Assertion;
using Share = XPool.Blockchain.Share;

namespace XPool.core
{
   
    public class RewardRecorder
    {
        public RewardRecorder(IConnectionFactory cf, IMapper mapper,
            JsonSerializerSettings jsonSerializerSettings,
            IShareRepository shareRepo, IBlockRepository blockRepo,
            IMasterClock clock,
            IMessageBus messageBus,
            WebhookNotificationService notificationService)
        {
            Assertion.RequiresNonNull(cf, nameof(cf));
            Assertion.RequiresNonNull(mapper, nameof(mapper));
            Assertion.RequiresNonNull(shareRepo, nameof(shareRepo));
            Assertion.RequiresNonNull(blockRepo, nameof(blockRepo));
            Assertion.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));
            Assertion.RequiresNonNull(clock, nameof(clock));
            Assertion.RequiresNonNull(messageBus, nameof(messageBus));
            Assertion.RequiresNonNull(notificationService, nameof(notificationService));

            this.cf = cf;
            this.mapper = mapper;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.clock = clock;
            this.messageBus = messageBus;
            this.notificationService = notificationService;

            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;

            BuildFaultHandlingPolicy();
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IShareRepository shareRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly IMasterClock clock;
        private readonly IMessageBus messageBus;
        private readonly WebhookNotificationService notificationService;
        private XPoolConfig clusterConfig;
        private readonly IMapper mapper;
        private readonly BlockingCollection<Share> queue = new BlockingCollection<Share>();

        private readonly int QueueSizeWarningThreshold = 1024;
        private readonly TimeSpan relayReceiveTimeout = TimeSpan.FromSeconds(60);
        private Policy faultPolicy;
        private bool hasLoggedPolicyFallbackFailure;
        private bool hasWarnedAboutBacklogSize;
        private IDisposable queueSub;
        private string recoveryFilename;
        private const int RetryCount = 3;
        private const string PolicyContextKeyShares = "share";
        private bool notifiedAdminOnPolicyFallback = false;

        private void PersistSharesFaulTolerant(IList<Share> shares)
        {
            var context = new Dictionary<string, object> { { PolicyContextKeyShares, shares } };

            faultPolicy.Execute(() => { PersistShares(shares); }, context);
        }

        private void PersistShares(IList<Share> shares)
        {
            cf.RunTx((con, tx) =>
            {
                foreach(var share in shares)
                {
                    var shareEntity = mapper.Map<Persistence.Model.Share>(share);
                    shareRepo.Insert(con, tx, shareEntity);

                    if (share.IsBlockCandidate)
                    {
                        var blockEntity = mapper.Map<Block>(share);
                        blockEntity.Status = BlockStatus.Pending;
                        blockRepo.Insert(con, tx, blockEntity);

                        messageBus.SendMessage(new BlockNotification(share.PoolId, share.BlockHeight));
                    }
                }
            });
        }

        private static void OnPolicyRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} in {timeSpan} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private void OnPolicyFallback(Exception ex, Context context)
        {
            logger.Warn(() => $"Fallback due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private void OnExecutePolicyFallback(Context context)
        {
            var shares = (IList<Share>) context[PolicyContextKeyShares];

            try
            {
                using(var stream = new FileStream(recoveryFilename, FileMode.Append, FileAccess.Write))
                {
                    using(var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        if (stream.Length == 0)
                            WriteRecoveryFileheader(writer);

                        foreach(var share in shares)
                        {
                            var json = JsonConvert.SerializeObject(share, jsonSerializerSettings);
                            writer.WriteLine(json);
                        }
                    }
                }

                NotifyAdminOnPolicyFallback();
            }

            catch(Exception ex)
            {
                if (!hasLoggedPolicyFallbackFailure)
                {
                    logger.Fatal(ex, "Fatal error during policy fallback execution. Share(s) will be lost!");
                    hasLoggedPolicyFallbackFailure = true;
                }
            }
        }

        private static void WriteRecoveryFileheader(StreamWriter writer)
        {
            writer.WriteLine("# The existence of this file means shares could not be committed to the database.");
            writer.WriteLine("# You should stop the pool cluster and run the following command:");
            writer.WriteLine("# MiningCore -c <path-to-config> -rs <path-to-this-file>\n");
        }

        public void RecoverShares(XPoolConfig clusterConfig, string recoveryFilename)
        {
            logger.Info(() => $"Recovering shares using {recoveryFilename} ...");

            try
            {
                var successCount = 0;
                var failCount = 0;
                const int bufferSize = 20;

                using(var stream = new FileStream(recoveryFilename, FileMode.Open, FileAccess.Read))
                {
                    using(var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        var shares = new List<Share>();
                        var lastProgressUpdate = DateTime.UtcNow;

                        while(!reader.EndOfStream)
                        {
                            var line = reader.ReadLine().Trim();

                                                        if (line.Length == 0)
                                continue;

                                                        if (line.StartsWith("#"))
                                continue;

                                                        try
                            {
                                var share = JsonConvert.DeserializeObject<Share>(line, jsonSerializerSettings);
                                shares.Add(share);
                            }

                            catch(JsonException ex)
                            {
                                logger.Error(ex, () => $"Unable to parse share record: {line}");
                                failCount++;
                            }

                                                        try
                            {
                                if (shares.Count == bufferSize)
                                {
                                    PersistShares(shares);

                                    shares.Clear();
                                    successCount += shares.Count;
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex, () => $"Unable to import shares");
                                failCount++;
                            }

                                                        var now = DateTime.UtcNow;
                            if (now - lastProgressUpdate > TimeSpan.FromMinutes(1))
                            {
                                logger.Info($"{successCount} shares imported");
                                lastProgressUpdate = now;
                            }
                        }

                                                try
                        {
                            if (shares.Count > 0)
                            {
                                PersistShares(shares);

                                successCount += shares.Count;
                            }
                        }

                        catch(Exception ex)
                        {
                            logger.Error(ex, () => $"Unable to import shares");
                            failCount++;
                        }
                    }
                }

                if (failCount == 0)
                    logger.Info(() => $"Successfully recovered {successCount} shares");
                else
                    logger.Warn(() => $"Successfully {successCount} shares with {failCount} failures");
            }

            catch(FileNotFoundException)
            {
                logger.Error(() => $"Recovery file {recoveryFilename} was not found");
            }
        }

        private void NotifyAdminOnPolicyFallback()
        {
            if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true &&
                !notifiedAdminOnPolicyFallback)
            {
                notifiedAdminOnPolicyFallback = true;

                notificationService.NotifyAdmin(
                    "Share Recorder Policy Fallback",
                    $"The Share Recorder's Policy Fallback has been engaged. Check share recovery file {recoveryFilename}.");
            }
        }

        #region API-Surface

        public void Start(XPoolConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            ConfigureRecovery();
            InitializeQueue();

            logger.Info(() => "Online");
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            queueSub.Dispose();
            queue.Dispose();

            logger.Info(() => "Stopped");
        }

        #endregion 
        private void InitializeQueue()
        {
            messageBus.Listen<ClientShare>().Subscribe(x => queue.Add(x.Share));

            queueSub = queue.GetConsumingEnumerable()
                .ToObservable(TaskPoolScheduler.Default)
                .Do(_ => CheckQueueBacklog())
                .Buffer(TimeSpan.FromSeconds(1), 100)
                .Where(shares => shares.Any())
                .Subscribe(shares =>
                {
                    try
                    {
                        PersistSharesFaulTolerant(shares);
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }
                });
        }

        private void ConfigureRecovery()
        {
            recoveryFilename = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.ShareRecoveryFile)
                ? clusterConfig.PaymentProcessing.ShareRecoveryFile
                : "recovered-shares.txt";
        }

        private void CheckQueueBacklog()
        {
            if (queue.Count > QueueSizeWarningThreshold)
            {
                if (!hasWarnedAboutBacklogSize)
                {
                    logger.Warn(() => $"Share persistence queue backlog has crossed {QueueSizeWarningThreshold}");
                    hasWarnedAboutBacklogSize = true;
                }
            }

            else if (hasWarnedAboutBacklogSize && queue.Count <= QueueSizeWarningThreshold / 2)
            {
                hasWarnedAboutBacklogSize = false;
            }
        }

        private void BuildFaultHandlingPolicy()
        {
                        var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    OnPolicyRetry);

                                    var breaker = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .CircuitBreaker(2, TimeSpan.FromMinutes(1));

            var fallback = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .Fallback(OnExecutePolicyFallback, OnPolicyFallback);

            var fallbackOnBrokenCircuit = Policy
                .Handle<BrokenCircuitException>()
                .Fallback(OnExecutePolicyFallback, (ex, context) => { });

            faultPolicy = Policy.Wrap(
                fallbackOnBrokenCircuit,
                Policy.Wrap(fallback, breaker, retry));
        }
    }
}
