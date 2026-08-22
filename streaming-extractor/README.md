# streaming-extractor

Scaffolds a run-to-completion .NET extractor for **batched, multi-file
deliveries**: one run detects a complete batch on the source (incoming prefix
+ ready marker), streams each file record-by-record in constant memory
through a registered streamed extractor (`Intropy.Framework.Streaming` + the
framework's `Batch` block), publishes **one summary CloudEvent per batch**
(batch id + per-file record/reject counts) to a pub/sub topic, archives the
consumed inputs, and exits.

Use this template when the producer delivers *sets* of files that must be
complete before processing (export folders, manifest-marked deliveries), and
the consumer wants one event per delivery rather than one per file. For
independent files with one event each, use the `extractor` (sweep) template
instead.

Scheduling lives outside the block — activation cadence is deployment
configuration (a Kubernetes CronJob in production); locally the system host
runs the block once at startup.

The rendered project is a one-shot console job with a Taskfile (`task build`,
`task test`, `task coverage`), an integration test project built on the
`Intropy.Framework.Testing` fakes (no sidecar, no Testcontainers), a
Dockerfile on the chiseled runtime, and an `AGENTS.md` describing the
component to coding agents. The component is hosted by the framework's
`RunToCompletionRunner` via a thin `BatchJob` adapter over the sidecar-free
`BatchPipeline` (detect → per-entity streamed extractors → serialize summary
→ publish → archive), split so the integration suite can drive the pipeline
directly. The sender is a DI-registered `SendStep<TCtx>` (a
`DaprTopicPublisher` in production), swapped in tests like any other
external.

The batch chain deliberately has no framework idempotency steps: batch
idempotency is the detect step's contract — `ArchiveStep` moves consumed
inputs to the processed prefix after a successful publish, which makes a
completed batch invisible to detection. The summary event's subject is the
batch id, so downstream consumers can deduplicate a re-delivery after a
mid-chain crash.

Components do not run standalone: the extractor runs via its system host,
which runs it once at startup and provides every Dapr component (source
binding, pub/sub, platform services).

The template declares a `spec.dependencies` entry on `shared-contracts` with
`mode: batch`: the render scaffolds a sibling `Contracts` class library
holding the batch-summary contract (the `contract` parameter, with a nested
per-file record) — unless that sibling already exists, in which case it is
left untouched.

## Parameters

| Name                    | Required | Description                                                                                          |
| ----------------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `name`                  | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Extractor.Perfion`).                  |
| `organization`          | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.                     |
| `topic`                 | yes      | Pub/sub topic the extractor publishes to (kebab-case).                                                |
| `contract`              | yes      | PascalCase shared-contracts record the topic carries — the batch summary (with a nested per-file record). |
| `idempotencyAppId`      | no       | Dapr app-id of the Idempotency Service (default matches the platform-services deployment).            |
| `businessIncidentsAppId`| no       | Dapr app-id of the Business Incident Service (default matches the platform-services deployment).      |
| `eventSource`           | no       | CloudEvent `source` override; preserves an existing envelope identity during a migration.             |
| `eventType`             | no       | CloudEvent `type` override; same purpose.                                                             |
| `empty`                 | no       | Strip sample step bodies for a migration agent to fill in (chain wiring stays; detect/serialize/streamed bodies throw). |

## Render

```bash
intropy int create streaming-extractor -o /tmp/streaming-out \
  -f streaming-extractor/examples/minimal.yaml --version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture and
`examples/migration.yaml` for a migration preserving an existing CloudEvent
identity.
