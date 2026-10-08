# loader

Scaffolds a long-running loader integration: a generic-host worker that
consumes one Dapr pub/sub message through a streaming subscription (the app
connects to its sidecar; it serves no HTTP and needs no app port), runs every
delivered event through one Intropy loader pipeline (`AddLoader` from
`Intropy.Framework.Hosting`), and writes the result as `{orderId}.json`
through a local destination folder binding. In production the loader runs as a
Deployment (unlike the run-to-completion `extractor`). The loader subscribes
to the system's message: scaffold the publishing extractor with the same
message value (`subscribes` here, `publishes` there). To route multiple event
types to different pipelines, change the composition to use the framework's
routed `AddLoader` overload.

The rendered project is a generic-host worker with a Taskfile (`task build`,
`task test`, `task coverage` — the component-level loop), two test projects, a
Dockerfile on the chiseled ASP.NET Core runtime (the framework's hosting
package runs on the ASP.NET Core shared framework), and an `AGENTS.md`
describing the component to coding agents. The framework owns the
subscription, the CloudEvent rebuild, the ack mapping, the consumer span and
graceful shutdown; the component owns only `src/Composition/Composition.cs` —
the host builder and every service registration, ending in `AddLoader`, so the
pipeline is always built exactly as production composition does and tests swap
any edge by swapping its registration. A broken subscription stream is
reopened; a pipeline that cannot be composed stops the host with exit code 1.

Tests split into `<name>.Test.Unit` (pipeline step contracts, pure xUnit —
the Sender's `IFileAdapter` seam via NSubstitute) and
`<name>.Test.Integration` (delivery, boot smoke, composition) built on the
`Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at the keyed
destination-adapter seam (its `WriteException` pins the Retry path), the two
platform-service clients swapped the same way, and `FakeStreamingSubscriber`
standing in for the sidecar's subscription — it delivers CloudEvents to the
running host exactly as the sidecar would — asserting on fake state and the
delivery ack. No sidecar, no Testcontainers.

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

The payload type derives from `subscribes` (`orders` becomes `Orders`) and is
scaffolded in the sibling Contracts project. If that project already exists,
ensure it declares the same payload type; this template leaves existing shared
contracts untouched.

## Parameters

| Name           | Required | Description                                                                                          |
| -------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderLoader`).                |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `subscribes`  | yes      | The message this loader subscribes to. The CloudEvents `type` and payload type derive from this value. |
| `routes`       | no       | The loader's subscription: messages it takes from its topic, in evaluation order, each `{message, when?}` with an optional Dapr CEL filter over the camelCase payload. Must include `subscribes`; empty means `subscribes`, unfiltered. Recorded for the system topology; the skeleton never reads it. |
| `topic`        | no       | The topic the loader consumes. Defaults to `subscribes`; set it to the shared topic when the routed messages' producers publish on one (`topic` on the extractor). |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; no idempotency lambdas).     |

## Render

```bash
intropy int create loader -o /tmp/loader-out \
  -f loader/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture.
