//TODO: Remove this in June 2026 after its use is fully deprecated and replaced by V1Gslb.
//This is only here to provide a migration path for users of the old API group and to avoid
//breaking changes for them.
//It will be removed in June 2026 after its use is fully deprecated and replaced by V1Gslb.
using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using System.Text.Json.Serialization;

namespace Cyclops.MultiCluster.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group = "multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "GSLB")]
    [KubernetesEntityShortNames("veccgslb")]
    [Description("GSLB object to expose services or ingresses across clusters")]
    public class V1VeccGslb : CustomKubernetesEntity
    {
        public V1VeccGslb()
        {
            Kind = "GSLB";
            ApiVersion = "multicluster.veccsolutions.io/v1alpha";
        }

        /// <summary>
        /// Reference to the ingress or service
        /// </summary>
        [Required]
        [Description("Reference to the ingress or service")]
        public V1ObjectReference ObjectReference { get; set; } = new V1ObjectReference();

        /// <summary>
        /// Hostnames to expose the ingress or service as
        /// </summary>
        [Description("Hostnames to expose the ingress or service as")]
        [Required]
        public string[] Hostnames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// External IP to return instead of what is in the ingress or service
        /// </summary>
        [Description("External IP to return instead of what is in the ingress or service")]
        [JsonPropertyName("ipOverrides")]
        public string[]? IPOverrides { get; set; }

        /// <summary>
        /// Priority to assign this GSLB object. Highest priority is chosen first.
        /// </summary>
        [Description("Priority to assign this GSLB object. Highest priority is chosen first.")]
        [RangeMinimum(0)]
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Weight to assign this GSLB object when doing round robin load balancing type. Defaults to 50.
        /// The calculation to determine the final weighting of all objects is (weight / sum of all weights) * 100.
        /// </summary>
        [Description("Weight to assign this GSLB object when doing round robin load balancing type. Defaults to 50. The calculation to determine the final weighting of all objects is (weight / sum of all weights) * 100.")]
        [RangeMinimum(0)]
        public int Weight { get; set; } = 50;

        public class V1ObjectReference
        {
            [Required]
            [Length(minLength: 1)]
            public string Name { get; set; } = string.Empty;

            [Required]
            public ReferenceType Kind { get; set; }

            public enum ReferenceType
            {
                Ingress,
                Service
            }
        }
    }
}
