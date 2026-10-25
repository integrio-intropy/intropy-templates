# gateway

A .NET gateway integration: a resident HTTP service that accepts items pushed
to it, runs each request body through the Intropy extractor pipeline, and
publishes the result as a CloudEvent. It is an extractor whose source is its
callers — the same steps and rules, with the caller as the retry mechanism.

Built on `Intropy.Framework.Components`: the component class declares its identity in code —
a `Manifest` property, mirroring the template manifest's computed values, with the payload as
the base class's type parameter — declares only its rules, the steps live in `src/Process/`, and
the framework wires the web host, the endpoint, the probes, telemetry, the platform clients and
the publisher from the manifest.
`int create` writes the same identity into `.intropy/scaffold.json`, which `intropy sys create`
reads; the build never touches the record. An identity change edits both.

## Parameters

| Parameter | Required | Default | Meaning |
|---|---|---|---|
| `name` | yes | — | PascalCase project name and root namespace |
| `organization` | yes | — | Telemetry service namespace and incident source URN |
| `publishes` | yes | — | The message published; the CloudEvents type |
| `path` | no | `/<publishes>` | Path callers `POST` items to |
| `localPort` | no | `5080` | Port the system host serves the gateway on locally (`src/Properties/launchSettings.json`, and `local` in `src/http-client.env.json`); unique per gateway in a system |
| `topic` | no | the message | Declared channel, recorded for `intropy sys create` |
| `idempotencyAppId` | no | `idempotency-service.services` | Dapr app-id of the Idempotency Service |
| `businessIncidentsAppId` | no | `business-incident-service.services` | Dapr app-id of the Business Incident Service |
| `eventSource` | no | `urn:<organization>:<app-id>` | CloudEvent `source` override |
| `empty` | no | `true` | Strip sample step bodies for a migration agent to fill in (idempotency and output-event lambdas throw) |

## The response contract

| Outcome | Status |
|---|---|
| Published | `202` with `{ requestId, eventId }` |
| Duplicate (idempotency check) | `200` |
| Business failure (routed to the Business Incident Service) | `422` problem details |
| Body larger than `Gateway:MaxRequestBodyBytes` | `413` |
| Not ready, or the sidecar, pub/sub or a platform service down | `503` + `Retry-After` |
| A technical failure a resend cannot cure (e.g. a step throwing on the item) | `500` |

Only `503` asks the caller to resend. Every response carries `X-Request-Id`.

## Environment

In production a gateway is a Kubernetes Deployment (it stays resident) behind
a Service and an environment-owned ingress, which owns authentication. The
system host runs it locally under Aspire. Its readiness probe is `/readyz`,
liveness `/healthz`. `deploy-component` renders the private Service, configures HTTP
for the Dapr app protocol, and wires those probes. The Service's
`intropy.io/endpoints` annotation records the endpoint paths; the environment's
TLS-enabled, authenticated ingress routes them unchanged to its `http` port.
No public ingress is enabled automatically. See
[the deployment contract](../deploy-component/README.md#gateways-private-service-and-environment-owned-ingress).

`intropy sys create` declares the gateway in the system host with
`AddGateway(...).Exposes(...).Publishes(...)`; its endpoint (`port`, `endpointPath`)
becomes an `EndpointRef` in the host's `Endpoints.cs`.

## Render locally

```bash
intropy int create gateway -o /tmp/order-gateway \
  -f gateway/examples/minimal.yaml --template-version main
```

See `examples/empty.yaml` for the empty-bodies fixture and `examples/full.yaml`
for a dotted name, a custom path and a preserved CloudEvent identity.
