# transactional

Scaffolds a .NET transactional integration job: a console runner
(`Intropy.Framework.Hosting.TransactionalIntegration`) polls a local source
folder binding, publishes each file onto a Dapr pub/sub topic, processes every
delivery through a VETER send pipeline (`Intropy.Framework.Blocks.TransactionalIntegration`),
and writes the transformed result to a destination folder binding. Two
framework pipelines cooperate: the **receive** pipeline (`Receiver` → `Enqueuer`
→ `Completer`) reads, publishes, and deletes each source file; the **send**
pipeline (deserialize → idempotency → extract → validate → transform →
serialize → send) turns each delivered message into the outbound file.

The rendered project has a Taskfile (`task build`, `task test`, `task coverage`
— the component-level loop), two test projects, a Dockerfile on the chiseled
runtime, and an `AGENTS.md` describing the component to coding agents. Every
service is registered in `Configuration/Composition.cs`, shared by the
entrypoint and the composition smoke test.

Tests split into `<name>.Test.Unit` (pipeline-step contracts, pure xUnit;
NSubstitute only at the Receiver/Sender `IFileAdapter` seams) and
`<name>.Test.Integration` (receive/send pipelines, composition, Dapr publish
wiring) built on the `Intropy.Framework.Testing` fakes: `InMemoryFileAdapter`
at both keyed adapter seams, `FakeEnqueueStep` swapped in at the receive
pipeline's broker edge, the two platform-service clients swapped the same way
(non-empty mode), and `PublishedMessageCapture` for the single NSubstitute
`DaprClient` seam in `EnqueuePublishTests`. Both pipelines are resolved from
the real DI graph — built exactly as production composition does — with only
the edges faked. The receive pipeline's enqueuer swap goes through an optional
override seam on `Composition.ConfigureServices` /
`Receive.ServiceCollectionExtensions.AddReceivePipeline`, so the fake never
re-wires the builder chain. No sidecar, no Testcontainers.

Note on versions: the framework packages pin at `0.3.0-beta.4` while
`Intropy.Framework.Testing` pins at `0.3.0-beta.5` — the first published build
shipping `FakeEnqueueStep`. The integration test project suppresses NU1605 for
that deliberate pairing (the transactional block types Testing builds on are
identical across beta.4 and beta.5).

Components do not run standalone: the job runs via its system host, which
provides every Dapr component (storage bindings, pub/sub, platform services —
including the Intropy Idempotency Service and Business Incident Service the
send pipeline wires in, in non-empty mode).

The template declares `intropy.dev/block-kind: transactional-integration`, so
`intropy sys create` assembles it into the system host as a
port-to-port block with no system topic. The scaffold record
(`.intropy/scaffold.json`) carries the derived wiring values the host needs:
`fromPort`/`toPort` (the two ports, `<app-id>-source` /
`<app-id>-destination`; the skeleton's binding names follow the host's
`binding.<port-name>` derivation) and `appId`/`projectName`. The
internal pub/sub hop between the receive and send pipelines is
component-owned: its topic (`<app-id>-in`) is unique per component and
deliberately not recorded — invisible to the topology, so two transactional
integrations under one host never cross wires.

## Parameters

| Name           | Required | Description                                                                                    |
| -------------- | -------- | ---------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderSync`).           |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.               |
| `empty`        | no       | Strip sample step bodies and the idempotency/business-incident wiring for a migration agent to fill in. |

## Render

```bash
intropy int create transactional -o /tmp/transactional-out \
  -f transactional/examples/full.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
