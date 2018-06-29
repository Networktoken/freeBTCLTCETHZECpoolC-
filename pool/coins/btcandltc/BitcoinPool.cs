

using Autofac;
using AutoMapper;
using XPool.Blockchain.Bitcoin.DaemonResponses;
using XPool.config;
using XPool.core.bus;
using XPool.core;
using XPool.Persistence;
using XPool.Persistence.Repositories;
using XPool.utils;
using Newtonsoft.Json;

namespace XPool.Blockchain.Bitcoin
{
    [CoinMetadata(
        CoinType.BTC,
        CoinType.LTC)]
    public class BitcoinPool : BitcoinPoolBase<BitcoinJob<BlockTemplate>, BlockTemplate>
    {
        public BitcoinPool(IComponentContext ctx,
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
    }
}
