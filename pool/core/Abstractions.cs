

using System;
using System.Threading;
using System.Threading.Tasks;
using XPool.Blockchain;
using XPool.config;
using XPool.core.stratumproto;

namespace XPool.core
{
	public struct ClientShare
	{
		public ClientShare(StratumClient client, Share share)
		{
			Client = client;
			Share = share;
		}

		public StratumClient Client;
		public Share Share;
	}

	public interface IMiningPool
    {
        PoolConfig Config { get; }
        PoolStats PoolStats { get; }
        BlockchainStats NetworkStats { get; }
        void Configure(PoolConfig poolConfig, XPoolConfig clusterConfig);
        double HashrateFromShares(double shares, double interval);
        Task StartAsync(CancellationToken ctsToken);
    }
}
