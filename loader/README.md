# loader

Scaffolds a long-running .NET loader integration: a runner holds a Dapr
streaming subscription on a pub/sub topic, rebuilds each delivered message
into a CloudEvent, runs it through the Intropy loader pipeline
(`Intropy.Framework.Blocks.Loader`), and writes the result as
`{orderId}.json` through a local destination folder binding. The loader is
the consuming half of a system contract — scaffold the publishing extractor
with the same `topic` value.

The rendered project is a console app (like `extractor`, running until
stopped) with a Taskfile (`task run` starts the local observability +
platform-services stack and runs the app under Dapr until Ctrl+C;
`task publish` sends a sample order through the loader's own sidecar), xUnit
tests for the pipeline steps, a Dockerfile on the chiseled runtime, and an
`AGENTS.md` describing the component to coding agents. The framework does not
yet ship a loader host, so the skeleton carries its own `LoaderRunner`
(sidecar lifecycle + streaming subscription), modeled on `ExtractorRunner`.

The pipeline wires idempotency and business incidents (the loader block
requires both), so the local compose stack includes the Intropy Idempotency
Service and Business Incident Service alongside grafana/otel-lgtm. Loader
idempotency keys off CloudEvent Subject/Time; the sample `Deserializer`
stamps both from the order's business identity (a `dapr publish` envelope
carries no subject and a wall-clock time).

There is no sample-data seeding: a subscriber is verified by publishing
through its own sidecar (`dapr publish --publish-app-id ...`), because the
in-memory pub/sub lives inside the integration's sidecar.

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
