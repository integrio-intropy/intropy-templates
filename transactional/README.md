# transactional

Scaffolds a .NET transactional integration job: a console runner
(`Intropy.Framework.Hosting.TransactionalIntegration`) polls a local source
folder binding, publishes each file onto a Dapr pub/sub topic, processes every
delivery through a VETER send pipeline (`Intropy.Framework.Blocks.TransactionalIntegration`),
and writes the transformed result to a destination folder binding. Two
framework pipelines cooperate: the framework owns the **receive** side — it
sweeps the source folder, publishes each file to the internal hop, and deletes
it once it is on the queue — and the component supplies the **send**
pipeline (deserialize → idempotency → extract → validate → transform →
serialize → send) turns each delivered message into the outbound file.

The rendered project has a Taskfile (`task build`, `task test`, `task coverage`
— the component-level loop), two test projects, a Dockerfile on the chiseled
runtime, and an `AGENTS.md` describing the component to coding agents. Every
service is registered in `Configuration/Composition.cs`, shared by the
entrypoint and the composition smoke test.

Tests split into `<name>.Test.Unit` (pipeline-step contracts, pure xUnit;
NSubstitute only at the Sender's `IFileAdapter` seam) and
`<name>.Test.Integration` (receive side, send pipeline, composition) built on
the `Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at both keyed
adapter seams, `FakeEnqueueStep` registered as the receive side's broker edge,
the two platform-service clients swapped the same way (non-empty mode), and a
single NSubstitute stub for the topic subscription when the receive-side tests
run the component's job. Everything is resolved from the real DI graph — built
exactly as production composition does — with only the edges faked. No
sidecar, no Testcontainers.

Note on versions: every `Intropy.Framework.*` package — including
`Intropy.Framework.Testing` — pins at `1.0.0-file-sweep-local.5`, so the integration fakes
and the framework types they build on always come from the same build.

Components do not run standalone: the job runs via its system host, which
provides every Dapr component (storage bindings, pub/sub, platform services —
including the Intropy Idempotency Service and Business Incident Service the
send pipeline wires in, in non-empty mode).

The template declares `intropy.io/block-kind: transactional-integration`, so
`intropy sys create` assembles it into the system host as a
port-to-port block with no system topic. The scaffold record
(`.intropy/scaffold.json`) carries the derived wiring values the host needs:
`fromPort`/`toPort` (the two ports, `<app-id>-source` /
`<app-id>-destination`; the Dapr binding names are the same port names) and
`appId`/`projectName`. The
internal pub/sub hop between the receive and send pipelines is
component-owned: its topic (`<app-id>-in`) is unique per component and
deliberately not recorded — invisible to the topology, so two transactional
integrations under one host never cross wires.

## Parameters

| Name           | Required | Description                                                                                    |
| -------------- | -------- | ---------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderSync`).           |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.               |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. Only wired when `empty` is false. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `empty`        | no       | Strip sample step bodies and the idempotency/business-incident wiring for a migration agent to fill in. |

## Render

```bash
intropy int create transactional -o /tmp/transactional-out \
  -f transactional/examples/full.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
