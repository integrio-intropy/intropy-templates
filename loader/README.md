# loader

Scaffolds a long-running ASP.NET loader integration: the service declares a
Dapr subscription on a pub/sub topic, the sidecar POSTs each delivered
message to the subscription endpoint, and the handler rebuilds the envelope
into a CloudEvent, runs it through the Intropy loader pipeline
(`Intropy.Framework.Blocks.Loader`), and writes the result as
`{orderId}.json` through a local destination folder binding. In production
the loader runs as a Deployment (unlike the run-to-completion `extractor`).
The loader subscribes to the system's message: scaffold the publishing
extractor with the same `topic` value (or wire both against the same registry
message, letting the flags record the resolved channel).

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
| `topic`        | no       | Override — the endpoint channel the message arrives over, when it must differ from the message name. Defaults to the message; registry resolution records the resolved producing channel in the record's subscribe block. |
| `contract`     | no       | Override — pins the payload type name instead of deriving it (PascalCase of the message). The generated record in the shared-contracts sibling is named after it. Never derived from a topic. |
| `contract`     | yes      | PascalCase payload type the message carries — the generated record in the shared-contracts sibling; the sample uses `Order` (see above). Registry resolution carries no payload type; the type stays a hand-set decision and is never derived from a topic. |
| `message`      | yes      | The one declaration everything derives from: the endpoint channel defaults to it, the payload type is its PascalCase projection, and it doubles as the CloudEvents `type` of what arrives. Seeded by `--subscribe` from the resolved registry message, or set by hand; the publishing extractor declares the identical name. |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; no idempotency lambdas).     |

## Render

```bash
intropy int create loader -o /tmp/loader-out \
  -f loader/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
