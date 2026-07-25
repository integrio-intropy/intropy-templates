# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — the system host locally, a
Kubernetes CronJob in production. The extractor is the publishing half of a
system contract — scaffold the consuming loader with the same `topic` value.

The rendered project is a one-shot console job (same shape as `transactional`)
with a Taskfile (`task run` seeds sample data, starts the local observability
+ platform-services stack, and runs the app once under Dapr), xUnit tests for
the pipeline steps, a Dockerfile on the chiseled runtime, and an `AGENTS.md`
describing the component to coding agents. The framework does not yet ship an
extractor host, so the skeleton carries its own `ExtractorRunner` (sidecar
lifecycle + one-shot sweep), modeled on `TransactionalIntegrationRunner`.

The pipeline wires idempotency and business incidents (the extractor block
requires both), so the local compose stack includes the Intropy Idempotency
Service and Business Incident Service alongside grafana/otel-lgtm.

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the published
contract (`Order`, `OrderLine`) — unless that sibling already exists
(scaffolded by an earlier component), in which case it is left untouched. The
extractor's csproj references it as `../../Contracts/Contracts.csproj`; only the
inbound file shape (`In`) stays local to the component. The name is plain
`Contracts` because the project is scoped by the system directory it lives in.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderExtractor`).            |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`        | yes      | Pub/sub topic the extractor publishes to (kebab-case); the consuming loader subscribes to the same.   |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
