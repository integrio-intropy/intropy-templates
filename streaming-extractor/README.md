# streaming-extractor

Scaffolds a run-to-completion .NET extractor for **independent files too
large to ride the message broker**. One run sweeps the incoming prefix,
streams each file record-by-record in constant memory through a registered
streamed extractor (`Intropy.Framework.Streaming`), writes the transformed
output to storage through the keyed `IFileStreamAdapter`, publishes **one
claim-check CloudEvent per file** (a reference to the output — location, file
name, source file name, entity type, record/reject counts — never the
payload) to a pub/sub topic, archives the consumed input, and exits.

Use this template when the producer delivers large files that must be
processed in constant memory and the payload cannot travel on the broker. For
small independent files where the payload rides inside the event, use the
`extractor` (sweep) template instead. For sets of files that must be complete
before processing (one summary event per delivery), there is no scaffold —
batch extraction is producer protocol; compose it by hand on the framework's
`Batch` block.

Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup.

The rendered project is a one-shot console job with a Taskfile (`task build`,
`task test`, `task coverage`), an integration test project built on the
`Intropy.Framework.Testing` fakes (no sidecar, no Testcontainers), a
Dockerfile on the chiseled runtime, and an `AGENTS.md` describing the
component to coding agents. The component is hosted by the framework's
`RunToCompletionRunner` via a thin `StreamJob` adapter over the sidecar-free
`Sweep` (list → per-file streamed extraction → publish reference → archive),
split so the integration suite can drive the sweep directly. The sender is a
DI-registered `SendStep<TCtx>` (a `DaprTopicPublisher` in production),
swapped in tests like any other external.

Sweep idempotency is the archive's contract: a processed file moves to the
processed prefix, which makes it invisible to the next sweep. The reference
event's subject/id is the source file name, so downstream consumers can
deduplicate a re-delivery after a publish-then-archive crash.

The source adapter is the streaming `IFileStreamAdapter` (open read/write
streams) behind a keyed registration — large files go through native storage
SDKs (Azure Blob SDK, etc.), never a Dapr binding, because bindings buffer
the payload through the sidecar and defeat constant-memory streaming. The
claim-check reference makes the file-adapter kind and the prefix layout part
of the *system contract*: the consuming loader resolves the reference by
streaming the named file from the same storage, on equal footing with the
topic and the contract record.

Components do not run standalone: the extractor runs via its system host,
which runs it once at startup and provides every Dapr component (source
binding, pub/sub, platform services).

The template declares a `spec.dependencies` entry on `shared-contracts` with
`mode: reference`: the render scaffolds a sibling `Contracts` class library
holding the claim-check reference contract (the `contract` parameter) —
unless that sibling already exists, in which case it is left untouched.

## Parameters

| Name                    | Required | Description                                                                                          |
| ----------------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`                  | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Extractor.Catalog`).                  |
| `organization`          | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`                 | yes      | Pub/sub topic the extractor publishes to (kebab-case).                                                |
| `contract`              | yes      | PascalCase shared-contracts record the topic carries — the claim-check reference (output location, file, entity, record/reject counts). |
| `idempotencyAppId`      | no       | Dapr app-id of the Idempotency Service (default matches the platform-services deployment).            |
| `businessIncidentsAppId`| no       | Dapr app-id of the Business Incident Service (default matches the platform-services deployment).      |
| `eventSource`           | no       | CloudEvent `source` override; preserves an existing envelope identity during a migration.             |
| `eventType`             | no       | CloudEvent `type` override; same purpose.                                                             |
| `empty`                 | no       | Strip sample step bodies for a migration agent to fill in (sweep wiring stays; entity mapping, deserializer, process, and reference bodies throw). |

## Render

```bash
intropy int create streaming-extractor -o /tmp/streaming-out \
  -f streaming-extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture and
`examples/migration.yaml` for a migration preserving an existing CloudEvent
identity.
