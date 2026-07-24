# extractor

Scaffolds a long-running .NET extractor integration: a Dapr cron binding ticks
every 5 seconds, the app sweeps a local inbound folder binding, runs each file
through the Intropy extractor pipeline (`Intropy.Framework.Blocks.Extractor`),
publishes the result as a CloudEvent to a pub/sub topic, and deletes the source
file. The extractor is the publishing half of a system contract — scaffold the
consuming loader with the same `topic` value.

The rendered project is an ASP.NET Core web app (the cron ticks arrive as
`POST /poll`) with a Taskfile (`task run` seeds sample data, starts the local
observability + platform-services stack, and runs the app under Dapr until you
stop it), xUnit tests for the pipeline steps, a Dockerfile on the chiseled
ASP.NET runtime, and an `AGENTS.md` describing the component to coding agents.

The pipeline wires idempotency and business incidents (the extractor block
requires both), so the local compose stack includes the Intropy Idempotency
Service and Business Incident Service alongside grafana/otel-lgtm.

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
