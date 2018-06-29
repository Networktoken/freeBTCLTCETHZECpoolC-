

using AutoMapper;
using XPool.Blockchain;
using XPool.config;
using XPool.Persistence.Model;
using XPool.Persistence.Model.Projections;
using MinerStats = XPool.Persistence.Model.Projections.MinerStats;

namespace XPool
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
                        
            CreateMap<Blockchain.Share, Persistence.Model.Share>();

            CreateMap<Blockchain.Share, Block>()
                .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
                .ForMember(dest => dest.Hash, opt => opt.MapFrom(src => src.BlockHash))
                .ForMember(dest => dest.Status, opt => opt.Ignore());

            CreateMap<BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

            CreateMap<core.PoolStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            CreateMap<BlockchainStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

                        CreateMap<PoolConfig, restful.Responses.PoolInfo>();
            CreateMap<PoolStats, restful.Responses.PoolInfo>();
            CreateMap<PoolStats, restful.Responses.AggregatedPoolStats>();
            CreateMap<Block, restful.Responses.Block>();
            CreateMap<Payment, restful.Responses.Payment>();
            CreateMap<BalanceChange, restful.Responses.BalanceChange>();
            CreateMap<CoinConfig, restful.Responses.ApiCoinConfig>();
            CreateMap<PoolPaymentProcessingConfig, restful.Responses.ApiPoolPaymentProcessingConfig>();

            CreateMap<MinerStats, restful.Responses.MinerStats>()
                .ForMember(dest => dest.LastPayment, opt => opt.Ignore())
                .ForMember(dest => dest.LastPaymentLink, opt => opt.Ignore());

            CreateMap<WorkerPerformanceStats, restful.Responses.WorkerPerformanceStats>();
            CreateMap<WorkerPerformanceStatsContainer, restful.Responses.WorkerPerformanceStatsContainer>();

                        CreateMap<Persistence.Model.Share, Persistence.Postgres.Entities.Share>();
            CreateMap<Block, Persistence.Postgres.Entities.Block>();
            CreateMap<Balance, Persistence.Postgres.Entities.Balance>();
            CreateMap<Payment, Persistence.Postgres.Entities.Payment>();
            CreateMap<PoolStats, Persistence.Postgres.Entities.PoolStats>();

            CreateMap<MinerWorkerPerformanceStats, Persistence.Postgres.Entities.MinerWorkerPerformanceStats>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());

                        
                        CreateMap<Persistence.Postgres.Entities.Share, Persistence.Model.Share>();
            CreateMap<Persistence.Postgres.Entities.Block, Block>();
            CreateMap<Persistence.Postgres.Entities.Balance, Balance>();
            CreateMap<Persistence.Postgres.Entities.Payment, Payment>();
            CreateMap<Persistence.Postgres.Entities.BalanceChange, BalanceChange>();
            CreateMap<Persistence.Postgres.Entities.PoolStats, PoolStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, MinerWorkerPerformanceStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, restful.Responses.MinerPerformanceStats>();

            CreateMap<PoolStats, core.PoolStats>();
            CreateMap<BlockchainStats, core.PoolStats>();

            CreateMap<PoolStats, BlockchainStats>()
                .ForMember(dest => dest.RewardType, opt => opt.Ignore())
                .ForMember(dest => dest.NetworkType, opt => opt.Ignore());
        }
    }
}
