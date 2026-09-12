# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup. The extractor publishes the system's message:
scaffold the consuming loader with the same `topic` value (or wire both
against the same registry message, letting the flags record the resolved
channel).

The rendered project is a one-shot console job (same shape as `transactional`)
with a Taskfile (`task build`, `task test`, `task coverage` — the
component-level loop), two test projects, a Dockerfile on the chiseled
runtime, and an `AGENTS.md` describing the component to coding agents. The
component is hosted by the framework's `RunToCompletionRunner` (in
`Intropy.Framework.Hosting`) — sidecar lifecycle, tracing, and the 0/1/2
exit-code contract — via a thin `ExtractJob` adapter over the sidecar-free
`Sweep` (list inbound, pipeline per file, delete on success), split so the
integration suite can construct the sweep directly. The sender is a
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
`Composition.Composition.BuildPipeline(provider)` — with every edge resolved
from DI. No sidecar, no Testcontainers.

Components do not run standalone: the extractor runs via its system host,
which runs it once at startup and provides every Dapr component (source
binding, pub/sub, platform services — including the Intropy Idempotency
Service and Business Incident Service the pipeline wires in).

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the published
contract (the `contract` parameter — `Order` by convention — plus `OrderLine`)
— unless that sibling already exists (scaffolded by an earlier component), in
which case it is left untouched. The extractor's csproj references it as
`../../Contracts/Contracts.csproj`; only the inbound file shape
(`Source<Contract>`) stays local to the component. The name is plain
`Contracts` because the project is scoped by the system directory it lives in.
The `contract` parameter is threaded through to `shared-contracts`, which
renames its canonical record to match.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderExtractor`).            |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`        | no       | Override — the endpoint channel the message flows over, when it must differ from the message name (an existing broker topic). Defaults to the message; registry resolution records the resolved channel in the record's publishes block. |
| `contract`     | no       | Override — pins the payload type name instead of deriving it (PascalCase of the message: `orders` derives `Orders`). The generated record in the shared-contracts sibling is named after it; the dependency threads it to the sibling. Never derived from a topic. |
| `contract`     | yes      | PascalCase payload type the message carries — the generated record in the shared-contracts sibling; the sample uses `Order`. Threaded to the `shared-contracts` dependency, which names its canonical record after it. Never resolved by registry resolution and never derived from a topic. |
| `message`      | yes      | The one declaration everything derives from: the endpoint channel defaults to it, the payload type is its PascalCase projection, and it doubles as the CloudEvents `type`. Seeded by `--publishes` from the resolved registry message, or set by hand; the consuming loader declares the identical name. The `eventType` parameter remains a migration override. |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `eventSource`  | no       | CloudEvent `source` for published events. Unset, derives as `urn:<organization>:<app-id>`; set it to preserve an existing event identity during a migration. |
| `eventType`    | no       | CloudEvent `type` override for the flat migration path. The registry-resolved message identity wins over it; before this release a topic-derived guess (`product-export` → `maxbo.product.export`) filled the slot — that derivation is deleted: a topic name is never guessed into an event type. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture and
`examples/migration.yaml` for preserving an existing CloudEvent identity and
platform-service app-ids.
