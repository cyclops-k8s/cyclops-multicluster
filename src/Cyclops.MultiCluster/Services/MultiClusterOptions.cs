using KubeOps.KubernetesClient;

namespace Cyclops.MultiCluster.Services
{
    public class MultiClusterOptions
    {
        public string ClusterIdentifier { get; set; } = "local";
        public int DNSRefreshInterval { get; set; } = 30; // resync every 30 seconds
        public int HeartbeatTimeout { get; set; } = 30;
        public int HeartbeatCheckInterval { get; set; } = 1;
        public int HeartbeatSetInterval { get; set; } = 10;
        /// <summary>
        /// Interval in seconds between cache synchronization runs in the orchestrator.
        /// Decoupled from HeartbeatCheckInterval to prevent a feedback loop where
        /// SynchronizeCachesAsync saves trigger reconciler watches, which cause further
        /// API calls, leading to thousands of HostnameCache API calls per minute.
        /// This can be configured in the values.yaml of the helm chart, and should be set to a value that balances cache freshness with API call volume.
        /// </summary>
        public int CacheSyncInterval { get; set; } = 30;
        public int ListenGrpcPort { get; set; } = 0;
        public int ListenPort { get; set; } = 0;
        public PeerHosts[] Peers { get; set; } = Array.Empty<PeerHosts>();
        [Sensitive]
        public byte[] ClusterSalt { get; set; } = Array.Empty<byte>();
        public Dictionary<string, string[]> NameserverNames { get; set; } = new Dictionary<string, string[]>();
        public int DefaultRecordTTL { get; set; } = 5;
        public string DNSServerResponsibleEmailAddress { get; set; } = "null.cyclops-k8s.io";
        public string DNSHostname { get; set; } = "dns.cyclops-k8s.io";
        public string Namespace { get; set; } = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? new KubernetesClient().GetCurrentNamespace();
        public int PeriodicRefreshInterval { get; set; } = 60 * 5; //default to 5 minutes
    }
}
