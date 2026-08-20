# Cyclops MultiCluster Ingress

Cyclops MultiCluster Ingress is a Kubernetes operator-based system for sharing ingress host/IP information across multiple clusters and serving DNS responses that can route traffic between clusters.

## What this project contains

- **Operator** (`--operator`): watches Kubernetes ingress/service/endpoints/GSLB resources and updates cluster host caches.
- **Orchestrator** (`--orchestrator`): synchronizes cluster cache updates between peer clusters.
- **DNS server** (`--dns-server`): serves DNS answers from synchronized hostname caches.
- **API server** (`--front-end`): exposes API endpoints (including auth helper endpoints) and health endpoints.
- **Helm chart** (`charts/multicluster-ingress`): deploys all components, RBAC, config, and CRDs.

## Repository layout

- `/src/Cyclops.MultiCluster` - main .NET service and controllers
- `/src/Cyclops.MultiCluster.Tests` - unit tests
- `/charts/multicluster-ingress` - Helm chart used for deployment
- `/test` - end-to-end Kind-based multi-cluster test harness

## Prerequisites

- Kubernetes cluster(s)
- Helm 3
- `kubectl`
- Container runtime access for pulling `quay.io/cyclops-k8s/multicluster-ingress`

## Quick start (single cluster)

1. Create a values file (for example `values.local.yaml`) with at least:
   - `config.clusterIdentifier`
   - `config.dnsHostname`
2. Install the chart:

```bash
helm upgrade --install multicluster-ingress ./charts/multicluster-ingress \
  --namespace mcingress-operator \
  --create-namespace \
  -f values.local.yaml
```

3. Verify pods:

```bash
kubectl get pods -n mcingress-operator
```

## Multi-cluster setup (peer clusters)

For each cluster, configure `config.apiKeys` (peers allowed to call this cluster) and `config.peers` (remote API servers this cluster calls).  
You can generate bootstrap auth material from:

- `GET /Authentication/Auth?identifier=<cluster-id>`
- `GET /Authentication/Salt`

These endpoints are exposed by the API server and help populate chart secret/config values.

## Health and API docs

- Liveness: `GET /Healthz/Liveness`
- Readiness: `GET /Healthz/Ready`
- OpenAPI document: `/openapi/v1.json`
- Interactive API reference (Scalar): available from the API server root

## Local development

Build and run tests:

```bash
dotnet test /home/runner/work/cyclops-multicluster/cyclops-multicluster/src/Cyclops.MultiCluster.Tests/Cyclops.MultiCluster.Tests.csproj
```

Run full integration tests (creates local Kind clusters):

```bash
cd /home/runner/work/cyclops-multicluster/cyclops-multicluster/test
./test.sh
```

## Notes

- Do not use sample/default keys in production.
- If `config.createSecret` is disabled, you must provide the expected secret keys yourself.
