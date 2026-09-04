# loader

Scaffolds a long-running ASP.NET loader integration: the service declares a
Dapr subscription on a pub/sub topic, the sidecar POSTs each delivered
message to the subscription endpoint, and the handler rebuilds the envelope
into a CloudEvent, runs it through the Intropy loader pipeline
(`Intropy.Framework.Blocks.Loader`), and writes the result as
`{orderId}.json` through a local destination folder binding. In production
the loader runs as a Deployment (unlike the run-to-completion `extractor`).
The loader is the consuming half of a system contract — wire it to the same
message the publishing extractor declares (or, without a registry, the same
`topic` value).

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
| `message`      | no       | Id of the message the loader consumes. `--subscribe <ref>` seeds it from the registry; it is also settable by hand. With no `topic` it is the topic name too. |
| `topic`        | no       | Pub/sub topic the loader subscribes to (kebab-case); the publishing extractor uses the same. Leave unset when a message is wired. |
| `contract`     | yes      | PascalCase shared-contracts record the topic carries; the sample uses `Order` (see above).            |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |
| `empty`        | no       | Strip sample step bodies for a migration agent to fill in (wiring stays; no idempotency lambdas).     |

## Channel

Neither `topic` nor `message` is required on its own, but a render with
neither fails: there is no channel to subscribe to. What resolves the
subscription, in order:

| Rendered value       | Precedence |
| -------------------- | ---------- |
| `PubSubName`         | the `subscribe` block's `pubsub` (an external subscription's own snapshot), else `pubsub` — the system-independent component name every internal subscription uses |
| `TopicName`          | a `subscribe` block decides alone: its `topic`, or — for an internal subscription carrying no channel — its `message`, the convention `intropy sys create` resolves the channel to (the producer's channel, topic named after the message). With no block: the `topic` parameter, else the `message` parameter under the same convention. The block never merges with the `topic` parameter, matching how the CLI reads a record |
| `SubscriptionRoute`  | `/events/<TopicName>` |

The `subscribe` block is written by the CLI: `--subscribe <message-ref>`
resolves the message against the registry and snapshots the channel into
`.intropy/scaffold.json`. The template only reads it. A block carrying a
message and no channel is an internal subscription — the shape
`examples/internal-message.yaml` shows: `sys create` resolves it against the
workspace component that publishes the same message, and the render follows
the same convention so the two agree without either consulting the other.

An external subscription also records the schema version pinned at resolve
time. It is documented in the rendered `AGENTS.md`/`README.md` and nothing
more: contract types stay hand-written in `Contracts`.

## Render

```bash
intropy int create loader -o /tmp/loader-out \
  -f loader/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture,
`examples/message.yaml` for what `--subscribe` resolves against a registry,
and `examples/internal-message.yaml` for a subscription by message name
without one.
