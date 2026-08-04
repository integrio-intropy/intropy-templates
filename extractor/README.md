# extractor

Scaffolds a run-to-completion .NET extractor integration: one run sweeps a
local inbound folder binding once, runs each swept file through the Intropy
extractor pipeline (`Intropy.Framework.Blocks.Extractor`), publishes the
result as a CloudEvent to a pub/sub topic, deletes the source file, and
exits. Scheduling lives outside the block — the system host locally, a
Kubernetes CronJob in production. The extractor is the publishing half of a
system contract — scaffold the consuming loader with the same `topic` value.

The rendered project is a one-shot console job (same shape as `transactional`)
with a Taskfile (`task build`, `task test`, `task coverage` — the
component-level loop), two test projects, a Dockerfile on the chiseled
runtime, and an `AGENTS.md` describing the component to coding agents. The
framework does not yet ship an extractor host, so the skeleton carries its own
`ExtractorRunner` (sidecar lifecycle + exit codes) delegating to a
sidecar-free `Sweep` (list inbound, pipeline per file, delete on success) —
split so the integration suite can construct the sweep directly.

Tests split into `<name>.Test.Unit` (pipeline step contracts, pure xUnit) and
`<name>.Test.Integration` (sweep, publish-wiring, composition, runner) built
on the `Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at the keyed
source-adapter seam, `FakeTopic` plugged in via `WithSender`, the two
platform-service clients overridden in DI (last registration wins), and
`PublishedMessageCapture` for the single NSubstitute `DaprClient` seam. The
pipeline is built exactly as production composition does — both
`Composition.BuildPipeline(provider)` (production/`AddProcessPipeline`) and
the test's `Composition.BuildPipeline(provider, b => b.WithSender(topic))`
go through the same builder chain. No sidecar, no Testcontainers.

Components do not run standalone: the extractor runs via its system host,
which schedules the job and provides every Dapr component (source binding,
pub/sub, platform services — including the Intropy Idempotency Service and
Business Incident Service the pipeline wires in).

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the published
contract (`Order`, `OrderLine`) — unless that sibling already exists
(scaffolded by an earlier component), in which case it is left untouched. The
extractor's csproj references it as `../../Contracts/Contracts.csproj`; only the
inbound file shape (`In`) stays local to the component. The name is plain
`Contracts` because the project is scoped by the system directory it lives in.

The sample logic and the scaffolded Contracts project use `Order` as the
contract record. Passing a different `contract` value renames the record the
topic is typed with, so rename the record in Contracts (and the sample
pipeline code, unless `empty=true`) to match.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderExtractor`).            |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`        | yes      | Pub/sub topic the extractor publishes to (kebab-case); the consuming loader subscribes to the same.   |
| `contract`     | yes      | PascalCase shared-contracts record the topic carries; the sample uses `Order` (see above).            |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; extractor lambdas throw).    |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
