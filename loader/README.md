# loader

Scaffolds a long-running ASP.NET loader integration: the service declares a
Dapr subscription on a pub/sub topic, the sidecar POSTs each delivered
message to the subscription endpoint, and the handler rebuilds the envelope
into a CloudEvent, runs it through the Intropy loader pipeline
(`Intropy.Framework.Blocks.Loader`), and writes the result as
`{orderId}.json` through a local destination folder binding. In production
the loader runs as a Deployment (unlike the run-to-completion `extractor`).
The loader is the consuming half of a system contract — scaffold the
publishing extractor with the same `topic` value.

The rendered project is an ASP.NET service with a Taskfile (`task build`,
`task test`, `task coverage` — the component-level loop), a `/healthz`
endpoint, two test projects, a Dockerfile on the chiseled ASP.NET runtime,
and an `AGENTS.md` describing the component to coding agents. The
subscription lives in `src/Endpoints/LoaderEndpoints.cs` (`Dapr.AspNetCore`'s
`.WithTopic` + `MapSubscribeHandler`). All service registration — including
the load edge, a DI-registered `SendStep<Out, Context>` — lives in
`src/Composition/Composition.cs`, so the pipeline is always built exactly as
production composition does and tests swap any edge by swapping its
registration.

Tests split into `<name>.Test.Unit` (pipeline step contracts, pure xUnit —
the Sender's `IFileAdapter` seam via NSubstitute) and
`<name>.Test.Integration` (delivery, boot smoke, composition) built on the
`Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at the keyed
destination-adapter seam (its `WriteException` pins the RETRY path), the two
platform-service clients swapped the same way, and `DaprDelivery.DeliverAsync`
POSTing structured-mode CloudEvents to the subscription route exactly as a
sidecar would, asserting on fake state and the delivery ack. No sidecar, no
Testcontainers.

Components do not run standalone: the loader runs via its system host, which
provides every Dapr component (pub/sub, destination binding, platform
services — including the Intropy Idempotency Service and Business Incident
Service the pipeline wires in) and carries the sample data used to publish a
message. Loader idempotency keys off CloudEvent Subject/Time; the sample
`Deserializer` stamps both from the order's business identity (a
`dapr publish` envelope carries no subject and a wall-clock time).

The template declares a `spec.dependencies` entry on `shared-contracts`: the
render also scaffolds a sibling `Contracts` class library holding the consumed
contract (`Order`, `OrderLine`) — unless that sibling already exists
(scaffolded by an earlier component, typically the extractor), in which case
it is left untouched. The loader's csproj references it as
`../../Contracts/Contracts.csproj`; only the destination load record (`Out`) stays
local to the component. The name is plain `Contracts` because the project is
scoped by the system directory it lives in.

The sample logic and the scaffolded Contracts project use `Order` as the
contract record. Passing a different `contract` value renames the record the
topic is typed with, so rename the record in Contracts (and the sample
pipeline code, unless `empty=true`) to match.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderLoader`).                |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`        | yes      | Pub/sub topic the loader subscribes to (kebab-case); the publishing extractor uses the same.          |
| `contract`     | yes      | PascalCase shared-contracts record the topic carries; the sample uses `Order` (see above).            |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; no idempotency lambdas).     |

## Render

```bash
intropy int create loader -o /tmp/loader-out \
  -f loader/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
