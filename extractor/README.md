# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup. The extractor is the publishing half of a
system contract — wire the consuming loader to the same message (or, without
a registry, the same `topic` value).

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
| `message`      | no       | Id of the message the extractor publishes. `--publishes <ref>` seeds it from the registry; it is also settable by hand. It is the CloudEvent `type`, and with no `topic` it is the topic name too. |
| `topic`        | no       | Pub/sub topic the extractor publishes to (kebab-case); the consuming loader subscribes to the same. Leave unset when a message is wired. |
| `contract`     | yes      | PascalCase shared-contracts record the topic carries; the sample uses `Order`. Threaded to the `shared-contracts` dependency, which names its canonical record after it. |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `eventSource`  | no       | CloudEvent `source` for published events. Unset, derives as `urn:<organization>:<app-id>`; set it to preserve an existing event identity during a migration. |
| `eventType`    | no       | CloudEvent `type` for published events. Unset, derives as `<organization-lower>.<first-topic-segment>.<last-topic-segment>` (e.g. topic `product-export` → `maxbo.product.export`); set it to preserve an existing event identity during a migration. A wired `message` outranks it — see Channel and event identity. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Channel and event identity

Neither `topic` nor `message` is required on its own, but a render with
neither fails: there is no channel to publish on. What resolves them, in
order:

| Rendered value | Precedence |
| -------------- | ---------- |
| `PubSubName`   | the `publishes` block's `pubsub` (a registry-resolved publication), else `pubsub` — the system-independent component name every internal publication uses |
| `TopicName`    | a `publishes` block decides alone: its `topic`, or — for an internal publication carrying no channel — its `message`, the convention `intropy sys create` assembles the channel with (system pubsub, topic named after the message). With no block: the `topic` parameter, else the `message` parameter under the same convention. The block never merges with the `topic` parameter, matching how the CLI reads a record |
| `EventType`    | the `message`, else the `eventType` parameter, else `<organization-lower>.<first-topic-segment>.<last-topic-segment>` derived from the topic |
| `EventSource`  | the `eventSource` parameter, else `urn:<organization>:<app-id>` — the registry resolves identity's type, never its source URI |

The `publishes` block is written by the CLI: `--publishes <message-ref>`
resolves the message against the registry and snapshots the channel into
`.intropy/scaffold.json`. The template only reads it. A block carrying a
message and no channel is an internal publication — the shape
`examples/internal-message.yaml` shows — and the topic then follows the
convention above, so the render and `sys create` agree without either
consulting the other.

A registry-resolved publication also records the schema version pinned at
resolve time. It is documented in the rendered `AGENTS.md`/`README.md` and
nothing more: contract types stay hand-written in `Contracts`.

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture,
`examples/migration.yaml` for preserving an existing CloudEvent identity and
platform-service app-ids, `examples/message.yaml` for what `--publishes`
resolves against a registry, and `examples/internal-message.yaml` for a
message declared without one.
