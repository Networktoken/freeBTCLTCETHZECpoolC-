

using System.Linq;
using System.Reflection;
using Autofac;
using XPool.restful;
using XPool.core.accession;
using XPool.Blockchain.Bitcoin;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.Blockchain.Ethereum;
using XPool.Blockchain.ZCash;
using XPool.Blockchain.ZCash.DaemonResponses;
using XPool.config;
using XPool.core.bus;
using XPool.core;
using XPool.pplns;
using XPool.pplns.scheme;
using XPool.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = Autofac.Module;

namespace XPool
{
    public class AutofacModule : Module
    {
                                                                protected override void Load(ContainerBuilder builder)
        {
            var thisAssembly = typeof(AutofacModule).GetTypeInfo().Assembly;

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            builder.RegisterType<MessageBus>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<PayoutManager>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<StandardClock>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterType<RewardRecorder>()
                .SingleInstance();

            builder.RegisterType<RestfulApiServer>()
                .SingleInstance();

            builder.RegisterType<StatsRecorder>()
                .AsSelf();

            builder.RegisterType<WebhookNotificationService>()
                .SingleInstance();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetCustomAttributes<CoinMetadataAttribute>().Any() && t.GetInterfaces()
                    .Any(i =>
                        i.IsAssignableFrom(typeof(IMiningPool)) ||
                        i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                        i.IsAssignableFrom(typeof(IPayoutScheme))))
                .WithMetadataFrom<CoinMetadataAttribute>()
                .AsImplementedInterfaces();

                        
            builder.RegisterType<PPLNSPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();


                        
            builder.RegisterType<BitcoinJobManager<BitcoinJob<BlockTemplate>, BlockTemplate>>()
                .AsSelf();


            builder.RegisterType<BitcoinJobManager<ZCashJob, ZCashBlockTemplate>>()
                .AsSelf();
               
       
                        
            builder.RegisterType<EthereumJobManager>()
                .AsSelf();

                        
            builder.RegisterType<ZCashJobManager<ZCashJob>>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
