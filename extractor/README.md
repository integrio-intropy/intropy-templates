# extractor

Scaffolds a run-to-completion .NET extractor integration that holds only
business code: the component's rules (`src/<Component>.cs`) and its pipeline
steps (`src/Process/`). One run sweeps the inbound port once, runs each file
through the Intropy extractor pipeline, publishes the result as a CloudEvent,
deletes the source file, and exits. Scheduling is deployment configuration (a
Kubernetes CronJob in production); locally the system host runs the block once
at startup. The extractor publishes the system's message: scaffold the
consuming loader with the same message value (`publishes` here, `subscribes`
there).

Everything that is not business code is supplied:

- **`Intropy.Framework.ExtractorHost`** — the project's one package reference —
  generates the entry point for the component class; the framework runner
  discovers the steps in the assembly and owns the sweep, Dapr, platform-service
  clients, telemetry, and the 0/1/2 exit codes.
- **The system topology** hands the component its runtime identity through
  `INTROPY__CONFIG`: component name, organization, CloudEvent source,
  published message, source binding, and platform-service app ids. The system
  host declares them once (`builder.Organization(...)`, the service roles in
  `Services.cs`); the component never repeats them. A migration that must keep
  an existing CloudEvent source overrides it in the topology with
  `.EventSource(...)` on the extractor.

The rendered project has a Taskfile (`task build`, `task test`), one unit-test
project for the steps, a Dockerfile on the chiseled runtime, and an
`AGENTS.md` describing the component to coding agents.

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the published
payload type derived from the message name (plus `OrderLine`) — unless that
sibling already exists, in which case it is left untouched. The extractor's
csproj references it as `../../Contracts/Contracts.csproj`; only the inbound
file shape (`Source<Contract>`) stays local to the component.

The framework and topology packages are pinned to the pilot package set
(`0.0.1-pilot.*`), which is not published: the workspace needs a
`nuget.config` pointing at the pilot feed (`pilot-system/pack-feed.sh`).

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderExtractor`). The last segment names the component class. |
| `organization` | yes      | PascalCase organization name, recorded for workspace tooling; the runtime organization is declared by the system host. |
| `publishes`   | yes      | The message this extractor publishes. The topic, CloudEvents `type`, and payload type derive from this value. |
| `empty`        | no       | Strip sample step bodies and rules for a migration agent to fill in (every step and rule throws).     |

## Render

```bash
intropy int create extractor -o /tmp/extractor-out \
  -f extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
