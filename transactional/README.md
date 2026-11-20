# transactional

Scaffolds a .NET transactional integration job: a console runner
(`Intropy.Framework.Hosting.TransactionalIntegration`) reads its source once
per run — a folder binding's files, or with `source=sql` a SQL query's rows
after a watermark (see [SQL source](#sql-source)) — publishes each read onto a
Dapr pub/sub topic, processes every delivery through a VETER send pipeline
(`Intropy.Framework.Blocks.TransactionalIntegration`), and writes the
transformed result to a destination folder binding. Two framework pipelines
cooperate: the framework owns the **receive** side — it reads the source,
publishes each read to the internal hop, and then deletes the file or moves
the watermark — and the component supplies the **send** pipeline (deserialize →
idempotency → extract → validate → transform → serialize → send), which turns
each delivered message into the outbound file.

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
`Intropy.Framework.Testing` — pins at `2.1.0-beta.15`, so the integration fakes
and the framework types they build on always come from the same build.

Components do not run standalone: the job runs via its system host, which
provides every Dapr component (storage bindings, pub/sub, platform services —
including the Intropy Idempotency Service and Business Incident Service the
send pipeline wires in, in non-empty mode).

The template declares `intropy.io/block-kind: transactional-integration`, so
`intropy sys create` assembles it into the system host as a
port-to-port block with no system message. The scaffold record
(`.intropy/scaffold.json`) carries the derived wiring values the host needs:
`fromPort`/`toPort` (the two ports, `<app-id>-source` /
`<app-id>-destination`; the Dapr binding names are the same port names) and
`appId`/`projectName`. The
internal queue between the receive and send pipelines is
component-owned: the topology mints it as the topic `internal-<app-id>` on the
system's pub/sub, `<system>-pubsub`, and nothing declares it. The
host and the deployment pass the pub/sub's name in `INTROPY_PUBSUB_NAME` and
the topic in `INTROPY_TOPIC_NAME`; the framework derives neither. The topic is
unique per component, so two transactional integrations — in one system or in
several sharing a broker — never receive each other's files.

## SQL source

With `source=sql` the source port is external: the component reads it through
`Intropy.Framework.Adapters.Sql` (`AddSqlSource`, a `PolledSourcePort`), with
no Dapr binding and no folder. Each run reads the rows after the watermark as
one message, the rows as a JSON array, queues it on the internal hop, and moves
the watermark once it is on the queue. The destination stays a file binding.
The scaffold record carries `fromPortKind: external`, so `intropy sys create`
declares the port with `PortRef.DefineExternal` and the topology mints the
cursor store the watermark is kept in (`<app-id>-cursors`), which the host
passes as `Ports__<port>__CursorStore`. `src/appsettings.Development.json`
reads a SQLite file and names that store, so the composition and integration
tests run without a host or a database server.

The send pipeline wires `WithSourceIdempotency` and `WithBusinessIncidents`,
both keyed by the range each read covers: a range read again after a failed
run is cancelled as a duplicate, and an incident is named by its range. Both
platform-service clients are registered whatever `empty` says. The source
always renders empty step bodies (`empty` applies to the file source only);
its sample is the query, `SELECT … FROM ChangeLog WHERE ChangeId > @watermark`.

## Container image

The Dockerfile builds with the system folder as its context
(`docker build -f <app-id>/Dockerfile .`), so it copies any
`Directory.Build.props` the system keeps next to its components.
`Dockerfile.dockerignore` limits the context to that file and the components'
`src/` folders. The component folder defaults to the app id; pass
`--build-arg COMPONENT=<folder>` when it differs.

## Parameters

| Name           | Required | Description                                                                                    |
| -------------- | -------- | ---------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.OrderSync`).           |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.               |
| `source`       | no       | What the integration reads: `file` (default) sweeps a folder through a Dapr binding; `sql` queries a database for the rows after a watermark (`src/Sql/Query.sql`, external port, always empty step bodies). See [SQL source](#sql-source). |
| `idempotencyAppId` | no  | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`, read via `IConfiguration` in Composition. Wired when `empty` is false or `source` is `sql`. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Rendered and wired like `idempotencyAppId`: when `empty` is false or `source` is `sql`. |
| `empty`        | no       | File source only: strip sample step bodies and the idempotency/business-incident wiring for a migration agent to fill in. A SQL source always renders empty bodies with both services wired. |

## Render

```bash
intropy int create transactional -o /tmp/transactional-out \
  -f transactional/examples/full.yaml --template-version main --no-input
```

See `examples/empty.yaml` for the empty-bodies fixture and `examples/sql.yaml`
for the SQL source.
