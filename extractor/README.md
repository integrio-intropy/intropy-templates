# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup. The extractor is the publishing half of a
system contract — scaffold the consuming loader with the same `topic` value.

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
| `topic`        | yes      | Pub/sub topic the extractor publishes to (kebab-case); the consuming loader subscribes to the same.   |
| `contract`     | yes      | PascalCase shared-contracts record the topic carries; the sample uses `Order`. Threaded to the `shared-contracts` dependency, which names its canonical record after it. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
