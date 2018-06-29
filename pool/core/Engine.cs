

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.CommandLineUtils;
using XPool.restful;
using XPool.restful.Responses;
using XPool.config;
using XPool.core.crypto.hash.equihash;
using XPool.pplns;
using XPool.Persistence.Dummy;
using XPool.Persistence.Postgres;
using XPool.Persistence.Postgres.Repositories;
using XPool.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Conditions;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;


namespace XPool.core
{
    public class Engine
    {
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();
        private static ILogger logger;
        private static IContainer container;
        private static CommandOption dumpConfigOption;
        private static CommandOption shareRecoveryOption;
        private static RewardRecorder shareRecorder;
        private static PayoutManager payoutManager;
        private static StatsRecorder statsRecorder;
        private static XPoolConfig clusterConfig;
        private static RestfulApiServer apiServer;

        public static AdminGcStats gcStats = new AdminGcStats();

        private static readonly Regex regexJsonTypeConversionError =
            new Regex("\"([^\"]+)\"[^\']+\'([^\']+)\'.+\\s(\\d+),.+\\s(\\d+)", RegexOptions.Compiled);

        public static void loadConfig(string configFile){
            clusterConfig = ReadConfig(configFile);

            if (dumpConfigOption.HasValue())
            {
                DumpParsedConfig(clusterConfig);
                return;
            }

            ValidateConfig();
        }

        private static void ValidateConfig()
        {
                        foreach (var config in clusterConfig.Pools)
            {
                if (!config.EnableInternalStratum.HasValue)
                    config.EnableInternalStratum = true;
            }

            try
            {
                clusterConfig.Validate();
            }

            catch (ValidationException ex)
            {
                Console.WriteLine($"Configuration is not valid:\n\n{string.Join("\n", ex.Errors.Select(x => "=> " + x.ErrorMessage))}");
                throw new PoolStartupAbortException(string.Empty);
            }
        }

        private static void DumpParsedConfig(XPoolConfig config)
        {
            Console.WriteLine("\nCurrent configuration as parsed from config file:");

            Console.WriteLine(JsonConvert.SerializeObject(config, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            }));
        }

        public static bool HandleCommandLineOptions(string[] args, out string configFile)
        {
            configFile = null;

            var app = new CommandLineApplication(false)
            {
                FullName = "X Pool Engine",
                ShortVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}"
            };

            var versionOption = app.Option("-v|--version", "version info", CommandOptionType.NoValue);
            var configFileOption = app.Option("-c|--config <configfile>", "config File",
                CommandOptionType.SingleValue);
            dumpConfigOption = app.Option("-dc|--dumpconfig",
                "dump the config file",
                CommandOptionType.NoValue);
            shareRecoveryOption = app.Option("-rs", "share recover file",
                CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            app.Execute(args);

            if (versionOption.HasValue())
            {
                app.ShowVersion();
                return false;
            }

            if (!configFileOption.HasValue())
            {
                app.ShowHelp();
                return false;
            }

            configFile = configFileOption.Value();

            return true;
        }

        public static void Bootstrap()
        {
            #if DEBUG
            loadNativeLibs();
            #endif
            Console.WriteLine($"start X pool engine... ");
            var builder = new ContainerBuilder();

            builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);
            builder.RegisterInstance(clusterConfig);

                        var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });
            builder.Register((ctx, parms) => amConf.CreateMapper());

            ConfigurePersistence(builder);
            container = builder.Build();
            ConfigureLogging();
            ConfigureMisc();
            ValidateRuntimeEnvironment();
            MonitorGc();
            logger.Info(() => $"{RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");
            if (!shareRecoveryOption.HasValue())
            {
                if (!cts.IsCancellationRequested)
                    Start().Wait(cts.Token);
            }

            else
            {
                RecoverShares(shareRecoveryOption.Value());
            }

        }

        private static XPoolConfig ReadConfig(string file)
        {
            try
            {
                Console.WriteLine($"Using configuration file {file}\n");

                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using (var reader = new StreamReader(file, Encoding.UTF8))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<XPoolConfig>(jsonReader);
                    }
                }
            }

            catch (JsonSerializationException ex)
            {
                HumanizeJsonParseException(ex);
                throw;
            }

            catch (JsonException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }

            catch (IOException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        private static void HumanizeJsonParseException(JsonSerializationException ex)
        {
            var m = regexJsonTypeConversionError.Match(ex.Message);

            if (m.Success)
            {
                var value = m.Groups[1].Value;
                var type = Type.GetType(m.Groups[2].Value);
                var line = m.Groups[3].Value;
                var col = m.Groups[4].Value;

                if (type == typeof(CoinType))
                    Console.WriteLine($"Error: Coin '{value}' is not (yet) supported (line {line}, column {col})");
                else if (type == typeof(PayoutScheme))
                    Console.WriteLine(
                        $"Error: Payout scheme '{value}' is not (yet) supported (line {line}, column {col})");
            }

            else
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void ValidateRuntimeEnvironment()
        {
                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
                logger.Warn(() => "Running as root is discouraged!");
        }

        private static void MonitorGc()
        {
            var thread = new Thread(() =>
            {
                var sw = new Stopwatch();

                while (true)
                {
                    var s = GC.WaitForFullGCApproach();
                    if (s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "FullGC soon");
                        sw.Start();
                    }

                    s = GC.WaitForFullGCComplete();

                    if (s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "FullGC completed");

                        sw.Stop();

                        if (sw.Elapsed.TotalSeconds > gcStats.MaxFullGcDuration)
                            gcStats.MaxFullGcDuration = sw.Elapsed.TotalSeconds;

                        sw.Reset();
                    }
                }
            });

            GC.RegisterForFullGCNotification(1, 1);
            thread.Start();
        }


        private static void ConfigureLogging()
        {
            var config = clusterConfig.Logging;
            var loggingConfig = new LoggingConfiguration();

            if (config != null)
            {
                                var level = !string.IsNullOrEmpty(config.Level)
                    ? LogLevel.FromString(config.Level)
                    : LogLevel.Info;

                var layout = "[${longdate}] [${level:format=FirstCharacter:uppercase=true}] [${logger:shortName=true}] ${message} ${exception:format=ToString,StackTrace}";

                if (config.EnableConsoleLog)
                {
                    if (config.EnableConsoleColors)
                    {
                        var target = new ColoredConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Trace"),
                            ConsoleOutputColor.DarkMagenta, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Debug"),
                            ConsoleOutputColor.Gray, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Info"),
                            ConsoleOutputColor.White, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Warn"),
                            ConsoleOutputColor.Yellow, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Error"),
                            ConsoleOutputColor.Red, ConsoleOutputColor.NoChange));

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Fatal"),
                            ConsoleOutputColor.DarkRed, ConsoleOutputColor.White));

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, LogLevel.Fatal, target);
                    }

                    else
                    {
                        var target = new ConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, LogLevel.Fatal, target);
                    }
                }

                if (!string.IsNullOrEmpty(config.LogFile))
                {
                    var target = new FileTarget("file")
                    {
                        FileName = GetLogPath(config, config.LogFile),
                        FileNameKind = FilePathKind.Unknown,
                        Layout = layout
                    };

                    loggingConfig.AddTarget(target);
                    loggingConfig.AddRule(level, LogLevel.Fatal, target);
                }

                if (config.PerPoolLogFile)
                {
                    foreach (var poolConfig in clusterConfig.Pools)
                    {
                        var target = new FileTarget(poolConfig.Id)
                        {
                            FileName = GetLogPath(config, poolConfig.Id + ".log"),
                            FileNameKind = FilePathKind.Unknown,
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, LogLevel.Fatal, target, poolConfig.Id);
                    }
                }
            }

            LogManager.Configuration = loggingConfig;

            logger = LogManager.GetCurrentClassLogger();
        }

        private static Layout GetLogPath(ClusterLoggingConfig config, string name)
        {
            if (string.IsNullOrEmpty(config.LogBaseDirectory))
                return name;

            return Path.Combine(config.LogBaseDirectory, name);
        }

        private static void ConfigureMisc()
        {
                        if (clusterConfig.EquihashMaxThreads.HasValue)
                EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads.Value;
        }

        private static void ConfigurePersistence(ContainerBuilder builder)
        {
            if (clusterConfig.Persistence == null &&
                clusterConfig.PaymentProcessing?.Enabled == true)
                logger.ThrowLogPoolStartupException("Persistence is not configured!");

            if (clusterConfig.Persistence?.Postgres != null)
                ConfigurePostgres(clusterConfig.Persistence.Postgres, builder);
            else
                ConfigureDummyPersistence(builder);
        }

        private static void ConfigurePostgres(DatabaseConfig pgConfig, ContainerBuilder builder)
        {
                        if (string.IsNullOrEmpty(pgConfig.Host))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'host'");

            if (pgConfig.Port == 0)
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'port'");

            if (string.IsNullOrEmpty(pgConfig.Database))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'database'");

            if (string.IsNullOrEmpty(pgConfig.User))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'user'");

                        var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};CommandTimeout=900;";

                        builder.RegisterInstance(new PgConnectionFactory(connectionString))
                .AsImplementedInterfaces();

                        builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private static void ConfigureDummyPersistence(ContainerBuilder builder)
        {
                        builder.RegisterInstance(new DummyConnectionFactory(string.Empty))
                .AsImplementedInterfaces();

                        builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private static async Task Start()
        {
         
                         shareRecorder = container.Resolve<RewardRecorder>();
            shareRecorder.Start(clusterConfig);

                        if (clusterConfig.Api == null || clusterConfig.Api.Enabled)
            {
                apiServer = container.Resolve<RestfulApiServer>();
                apiServer.Start(clusterConfig);
            }

                        if (clusterConfig.PaymentProcessing?.Enabled == true &&
                clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
            {
                payoutManager = container.Resolve<PayoutManager>();
                payoutManager.Configure(clusterConfig);

                payoutManager.Start();
            }

            else
                logger.Info("Payment processing is not enabled");

          
                                statsRecorder = container.Resolve<StatsRecorder>();
                statsRecorder.Configure(clusterConfig);
                statsRecorder.Start();

                        await Task.WhenAll(clusterConfig.Pools.Where(x => x.Enabled).Select(async poolConfig =>
            {
                                var poolImpl = container.Resolve<IEnumerable<Meta<Lazy<IMiningPool, CoinMetadataAttribute>>>>()
                    .First(x => x.Value.Metadata.SupportedCoins.Contains(poolConfig.Coin.Type)).Value;

                                var pool = poolImpl.Value;
                pool.Configure(poolConfig, clusterConfig);

                                statsRecorder?.AttachPool(pool);

                await pool.StartAsync(cts.Token);
            }));

                        await Observable.Never<Unit>().ToTask(cts.Token);
        }

        private static void RecoverShares(string recoveryFilename)
        {
            shareRecorder = container.Resolve<RewardRecorder>();
            shareRecorder.RecoverShares(clusterConfig, recoveryFilename);
        }

        public static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (logger != null)
            {
                logger.Error(e.ExceptionObject);
                LogManager.Flush(TimeSpan.Zero);
            }

            Console.WriteLine("** AppDomain unhandled exception: {0}", e.ExceptionObject);
        }

        public static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            logger?.Info(() => "SIGINT received. Exiting.");
            Console.WriteLine("SIGINT received. Exiting.");

            try
            {
                cts?.Cancel();
            }
            catch { }

            e.Cancel = true;
        }

        public static void OnProcessExit(object sender, EventArgs e)
        {
            logger?.Info(() => "SIGTERM received. Exiting.");
            Console.WriteLine("SIGTERM received. Exiting.");

            try
            {
                cts?.Cancel();
            }
            catch { }
        }

        public static void Shutdown()
        {
            logger.Info(() => "Shutdown Xpool ...");
            Console.WriteLine("Shutdown Xpool...");
            shareRecorder?.Stop();
            statsRecorder?.Stop();
        }


        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        private static readonly string[] NativeLibs =
        {
            "libmultihash.dll",
            "libcryptonote.dll"
        };

       
        private static void loadNativeLibs()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
               
            }

            var runtime = "win-x64";
            var appRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (var nativeLib in NativeLibs)
            {
                var path = Path.Combine(appRoot, "runtimes", runtime, "native", nativeLib);
                var result = LoadLibraryEx(path, IntPtr.Zero, 0);

                if (result == IntPtr.Zero)
                    Console.WriteLine($"Unable to load {path}");
            }
        }
    }
}
