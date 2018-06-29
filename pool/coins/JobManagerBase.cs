

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using XPool.config;
using XPool.extensions;
using XPool.utils;
using NetMQ;
using NetMQ.Sockets;
using NLog;
using Assertion = XPool.utils.Assertion;

namespace XPool.Blockchain
{
    public abstract class JobManagerBase<TJob>
    {
        protected JobManagerBase(IComponentContext ctx)
        {
            Assertion.RequiresNonNull(ctx, nameof(ctx));

            this.ctx = ctx;
        }

        protected readonly IComponentContext ctx;
        protected XPoolConfig clusterConfig;

        protected TJob currentJob;
        private int jobId;
        protected object jobLock = new object();
        protected ILogger logger;
        protected PoolConfig poolConfig;
        protected bool hasInitialBlockTemplate = false;
        protected Subject<Unit> blockSubmissionSubject = new Subject<Unit>();
        protected TimeSpan btStreamReceiveTimeout = TimeSpan.FromSeconds(60 * 10);

        protected virtual string LogCat { get; } = "Job Manager";

        protected abstract void ConfigureDaemons();

        protected virtual async Task StartDaemonAsync(CancellationToken ct)
        {
            while (!await AreDaemonsHealthyAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }

            logger.Info(() => $"[{LogCat}] All daemons online");

            while (!await AreDaemonsConnectedAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to connect to peers ...");

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        protected string NextJobId(string format = null)
        {
            Interlocked.Increment(ref jobId);
            var value = Interlocked.CompareExchange(ref jobId, 0, Int32.MinValue);

            if (format != null)
                return value.ToString(format);

            return value.ToStringHex8();
        }



        protected abstract Task<bool> AreDaemonsHealthyAsync();
        protected abstract Task<bool> AreDaemonsConnectedAsync();
        protected abstract Task EnsureDaemonsSynchedAsync(CancellationToken ct);
        protected abstract Task PostStartInitAsync(CancellationToken ct);

        #region API-Surface

        public virtual void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));
            Assertion.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<TJob>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            ConfigureDaemons();
        }

        public async Task StartAsync(CancellationToken ct)
        {
            Assertion.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            await StartDaemonAsync(ct);
            await EnsureDaemonsSynchedAsync(ct);
            await PostStartInitAsync(ct);

            logger.Info(() => $"[{LogCat}] Online");
        }

        #endregion     }
    }
}
