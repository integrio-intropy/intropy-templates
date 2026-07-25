# loader

Scaffolds a long-running ASP.NET loader integration: the service declares a
Dapr subscription on a pub/sub topic, the sidecar POSTs each delivered
message to the subscription endpoint, and the handler rebuilds the envelope
into a CloudEvent, runs it through the Intropy loader pipeline
(`Intropy.Framework.Blocks.Loader`), and writes the result as
`{orderId}.json` through a local destination folder binding. In production
the loader runs as a Deployment (unlike the run-to-completion `extractor`).
The loader is the consuming half of a system contract — scaffold the
publishing extractor with the same `topic` value.

The rendered project is an ASP.NET service with a Taskfile (`task run` starts
the local observability + platform-services stack and runs the app under
Dapr — app port 5001, HTTP delivery — until Ctrl+C; `task publish` sends a
sample order through the loader's own sidecar), a `/healthz` endpoint, xUnit
tests for the pipeline steps, a Dockerfile on the chiseled ASP.NET runtime,
and an `AGENTS.md` describing the component to coding agents. The
subscription lives in `src/Endpoints/LoaderEndpoints.cs` (`Dapr.AspNetCore`'s
`.WithTopic` + `MapSubscribeHandler`).

The pipeline wires idempotency and business incidents (the loader block
requires both), so the local compose stack includes the Intropy Idempotency
Service and Business Incident Service alongside grafana/otel-lgtm. Loader
idempotency keys off CloudEvent Subject/Time; the sample `Deserializer`
stamps both from the order's business identity (a `dapr publish` envelope
carries no subject and a wall-clock time).

There is no sample-data seeding: a subscriber is verified by publishing
through its own sidecar (`dapr publish --publish-app-id ...`), because the
in-memory pub/sub lives inside the integration's sidecar.

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the consumed
contract (`Order`, `OrderLine`) — unless that sibling already exists
(scaffolded by an earlier component, typically the extractor), in which case
it is left untouched. The loader's csproj references it as
`../../Contracts/Contracts.csproj`; only the destination load record (`Out`) stays
local to the component. The name is plain `Contracts` because the project is
scoped by the system directory it lives in.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderLoader`).                |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`        | yes      | Pub/sub topic the loader subscribes to (kebab-case); the publishing extractor uses the same.          |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; no idempotency lambdas).     |

## Render

```bash
intropy int create loader -o /tmp/loader-out \
  -f loader/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
