using k8s.Models;
using KubeOps.KubernetesClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cyclops.MultiCluster.Controllers
{
    /// <summary>
    /// Health related operations happen here
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("[Controller]")]
    public class HealthzController : Controller
    {
        private readonly IKubernetesClient _kubernetesClient;
        private static bool _ready;

        /// <summary>
        /// Health related operations happen here
        /// </summary>
        /// <param name="kubernetesClient"></param>
        public HealthzController(IKubernetesClient kubernetesClient)
        {
            _kubernetesClient = kubernetesClient;
        }

        /// <summary>
        /// Check if the pod is ready to accept requests
        /// </summary>
        /// <returns></returns>
        [HttpGet("Ready")]
        public async Task<IActionResult> ReadyAsync()
        {
            if (_ready)
            {
                return Ok("Already checked and ready to go");
            }
            V1Namespace? ns = null;
            try
            {
                ns = await _kubernetesClient.GetAsync<V1Namespace>(await _kubernetesClient.GetCurrentNamespaceAsync());
                if (ns == null)
                {
                    return StatusCode(500, "Namespace result was null");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex);
            }

            _ready = true;
            return Ok(new { Status = "Kubernetes API returned something", Namespace = ns });
        }

        /// <summary>
        /// Check if the pod is alive.
        /// This intentionally does NOT call the Kubernetes API. Liveness probes
        /// should only verify the process is responsive. The previous implementation
        /// called GetAsync&gt;V1Namespace&lt;; which competed with reconciler traffic and
        /// timed out under load, causing unnecessary pod restarts across all components.
        /// </summary>
        /// <returns></returns>
        [HttpGet("Liveness")]
        public IActionResult Liveness()
        {
            return Ok("Alive");
        }
    }
}
