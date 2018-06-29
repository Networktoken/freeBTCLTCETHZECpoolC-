

using Autofac;
using AutoMapper;
using XPool.Blockchain.ZCash.Configuration;
using XPool.config;
using XPool.extensions;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;

namespace XPool.Blockchain.ZCash
{
    [CoinMetadata(CoinType.ZEC)]
    public class ZCashPool : ZCashPoolBase<ZCashJob>
    {
        public ZCashPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            WebhookNotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, notificationService)
        {
        }

        private ZCashPoolConfigExtra extraConfig;

        public override void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            extraConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();

            if (string.IsNullOrEmpty(extraConfig?.ZAddress))
                logger.ThrowLogPoolStartupException($"Pool z-address is not configured", LogCat);
        }
    }
}
