# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup. The extractor publishes the system's message:
scaffold the consuming loader with the same message value (`publishes` here,
`subscribes` there).

The rendered project is a one-shot console job (same shape as `transactional`)
with a Taskfile (`task build`, `task test`, `task coverage` — the
component-level loop), two test projects, a Dockerfile on the chiseled
runtime, and an `AGENTS.md` describing the component to coding agents. The
component is hosted by the framework's `JobRunner` (in
`Intropy.Framework.Hosting`) — sidecar lifecycle, tracing, and the 0/1/2
exit-code contract — running the framework's extractor job
(`AddExtractor` with sources and routes: list each source port, run each
file through its port's pipeline, delete on success), which the integration
suite builds from the real DI graph. The sender is a
DI-registered `SendStep<Context>` (a `DaprTopicPublisher` in production),
swapped in tests like any other external.

Tests split into `<name>.Test.Unit` (pipeline step contracts, pure xUnit) and
`<name>.Test.Integration` (sweep, publish-wiring, composition) built on the
`Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at the keyed
source-adapter seam, `FakeTopic` swapped in via `RemoveAll<SendStep<Context>>()`
+ `AddSingleton<SendStep<Context>>(topic)` exactly like the other externals,
the two platform-service clients swapped the same way, and
`PublishedMessageCapture` for the single NSubstitute `DaprClient` seam (the
test re-registers the production `DaprTopicPublisher` against the substituted
client). The pipeline is always built exactly as production composition does —
for the source port, in a DI scope, configured by `Composition.ConfigurePipeline` — with
every edge resolved from DI. No sidecar, no Testcontainers.

Components do not run standalone: the extractor runs via its system host,
which runs it once at startup and provides every Dapr component (source
binding, pub/sub, platform services — including the Intropy Idempotency
Service and Business Incident Service the pipeline wires in).

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the published
payload type (plus `OrderLine`)
— unless that sibling already exists (scaffolded by an earlier component), in
which case it is left untouched. The extractor's csproj references it as
`../../Contracts/Contracts.csproj`; only the inbound file shape
(`Source<Contract>`) stays local to the component. The name is plain
`Contracts` because the project is scoped by the system directory it lives in.
The payload type is `typeName` when set, else the PascalCase of the message
(`invoice-created` becomes `InvoiceCreated`). It is threaded through to
`shared-contracts`, which names its canonical record after it, and recorded as
`payloadType` in `.intropy/scaffold.json`. Set `typeName` when a schema names
the type and the message name would make a long one:
`--publishes com.example.sales.order.created.v1 -s typeName=OrderCreatedV1`
gives `OrderCreatedV1` instead of `ComExampleSalesOrderCreatedV1`.

## Container image

The Dockerfile builds with the system folder as its context
(`docker build -f <app-id>/Dockerfile .`), so it copies the `Contracts`
sibling and any `Directory.Build.props` the system keeps next to its
components. `Dockerfile.dockerignore` limits the context to those files and
the components' `src/` folders. The component folder defaults to the app id;
pass `--build-arg COMPONENT=<folder>` when it differs.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderExtractor`).            |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `publishes`   | yes      | The message this extractor publishes. The topic, CloudEvents `type`, and (unless `typeName` is set) payload type derive from this value. |
| `typeName`     | no       | PascalCase payload type, the record the message carries in `Contracts`. Defaults to the PascalCase of `publishes`. |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `eventSource`  | no       | CloudEvent `source` for published events. Unset, derives as `urn:<organization>:<app-id>`; set it to preserve an existing event identity during a migration. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture and
`examples/migration.yaml` for preserving an existing CloudEvent identity and
platform-service app-ids.
