

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XPool.config
{
    public enum CoinType
    {

        BTC = 1,         LTC,         ZEC,         ETH,     }

    public class CoinConfig
    {
        public CoinType Type { get; set; }

                                public string Algorithm { get; set; }
    }

    public enum PayoutScheme
    {
                PPLNS = 1,
        Solo
    }

    public partial class ClusterLoggingConfig
    {
        public string Level { get; set; }
        public bool EnableConsoleLog { get; set; }
        public bool EnableConsoleColors { get; set; }
        public string LogFile { get; set; }
        public bool PerPoolLogFile { get; set; }
        public string LogBaseDirectory { get; set; }
    }

    public partial class NetworkEndpointConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public partial class AuthenticatedNetworkEndpointConfig : NetworkEndpointConfig
    {
        public string User { get; set; }
        public string Password { get; set; }
    }

    public class DaemonEndpointConfig : AuthenticatedNetworkEndpointConfig
    {
                                public bool Ssl { get; set; }

                                public bool Http2 { get; set; }

                                public bool ValidateCert { get; set; }

                                public string Category { get; set; }

                                public string HttpPath { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public class DatabaseConfig : AuthenticatedNetworkEndpointConfig
    {
        public string Database { get; set; }
    }

    public class TcpProxyProtocolConfig
    {
                                public bool Enable { get; set; }

                                public bool Mandatory { get; set; }

                                public string[] ProxyAddresses { get; set; }
    }

    public class PoolEndpoint
    {
        public string ListenAddress { get; set; }
        public string Name { get; set; }
        public double Difficulty { get; set; }
        public TcpProxyProtocolConfig TcpProxyProtocol { get; set; }
        public VarDiffConfig VarDiff { get; set; }
    }

    public partial class VarDiffConfig
    {
                                public double MinDiff { get; set; }

                                public double? MaxDiff { get; set; }

                                public double? MaxDelta { get; set; }

                                public double TargetTime { get; set; }

                                public double RetargetTime { get; set; }

                                public double VariancePercent { get; set; }
    }

    public enum BanManagerKind
    {
        Integrated = 1,
        IpTables
    }

    public class ClusterBanningConfig
    {
        public BanManagerKind? Manager { get; set; }

                                public bool? BanOnJunkReceive { get; set; }

                                public bool? BanOnInvalidShares { get; set; }
    }

    public partial class PoolShareBasedBanningConfig
    {
        public bool Enabled { get; set; }
        public int CheckThreshold { get; set; }         public double InvalidPercent { get; set; }         public int Time { get; set; }     }

    public partial class PoolPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public decimal MinimumPayment { get; set; }         public PayoutScheme PayoutScheme { get; set; }
        public JToken PayoutSchemeConfig { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class ClusterPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public int Interval { get; set; }

        public string ShareRecoveryFile { get; set; }
    }

    public partial class PersistenceConfig
    {
        public DatabaseConfig Postgres { get; set; }
    }

    public class RewardRecipient
    {
        public string Address { get; set; }
        public decimal Percentage { get; set; }

                                public string Type { get; set; }
    }

    public partial class EmailSenderConfig : AuthenticatedNetworkEndpointConfig
    {
        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    public partial class AdminNotifications
    {
        public bool Enabled { get; set; }
        public string EmailAddress { get; set; }
        public bool NotifyBlockFound { get; set; }
        public bool NotifyPaymentSuccess { get; set; }
    }

    public partial class WebhookNotifcations
    {
        public bool Enabled { get; set; }
        public string WebHookUrl { get; set; }
        public bool NotifyBlockFound { get; set; }
        public bool NotifyPaymentSuccess { get; set; }
                                public string BlockFoundUsername { get; set; }
                               public string PaymentSuccessUsername { get; set; }

      
    }

    public partial class NotificationsConfig
    {
        public bool Enabled { get; set; }

        public EmailSenderConfig Email { get; set; }
        public AdminNotifications Admin { get; set; }
    }

    public partial class ApiConfig
    {
        public bool Enabled { get; set; }
        public string ListenAddress { get; set; }
        public int Port { get; set; }

                                public int AdminPort { get; set; }
    }
    /*
    public partial class ZmqPubSubEndpointConfig
    {
        public string Url { get; set; }
        public string Topic { get; set; }
    }*/

  

    public partial class PoolConfig
    {
        public string Id { get; set; }
        public string PoolName { get; set; }
        public bool Enabled { get; set; }
        public CoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public DaemonEndpointConfig[] Daemons { get; set; }
        public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public PoolShareBasedBanningConfig Banning { get; set; }
        public RewardRecipient[] RewardRecipients { get; set; }
        public WebhookNotifcations SlackNotifications { get; set; }
        public string Address { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }

                                public bool? EnableInternalStratum { get; set; }

                               
        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class XPoolConfig
    {
        public string ClusterName { get; set; }
        public ClusterLoggingConfig Logging { get; set; }
        public ClusterBanningConfig Banning { get; set; }
        public PersistenceConfig Persistence { get; set; }
        public ClusterPaymentProcessingConfig PaymentProcessing { get; set; }
        public NotificationsConfig Notifications { get; set; }
        public ApiConfig Api { get; set; }


                                        public int? EquihashMaxThreads { get; set; }

        public PoolConfig[] Pools { get; set; }
    }
}
