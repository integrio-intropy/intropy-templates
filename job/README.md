# job

Scaffolds a run-to-completion .NET job: a console runner hosted by the
framework's `JobRunner` (`Intropy.Framework.Hosting`) with one `IJob` to
implement. It is the catch-all for scheduled work that is not an extractor,
loader or transactional integration — a reconciler that retries what is due,
a nightly job that seals and delivers a batch, a cleanup.

The runner owns everything around the work: it waits for the Dapr sidecar,
runs the job inside one span named after the component, shuts the sidecar
down, and maps the result to the exit code (`0` success / nothing to do /
cancelled, `1` job failure, `2` infrastructure failure). The scaffolded `Job`
logs and reports an empty run, so the project builds, tests green and runs
before any work is written; `AGENTS.md` points a coding agent at `Job.cs` and
the services it takes, and away from the hosting.

The rendered project has a Taskfile (`task build`, `task test`, `task
coverage`), two test projects, a Dockerfile on the chiseled runtime, and an
`AGENTS.md`. Every service is registered in `src/Composition/Composition.cs`,
shared by the entrypoint and the tests. Tests split into `<name>.Test.Unit`
(the job without a host) and `<name>.Test.Integration` (composition, and one
run under the real `JobRunner` with the Dapr sidecar and the platform clients
faked).

The job wires no messages and declares no ports. It registers the Idempotency
and Business Incident Service clients, which resolve lazily: a job that never
takes them never calls the services.

## Block kind: `job`

The template declares `intropy.io/block-kind: job` and `data-flow: internal`,
and wires no message parameters: `--publishes` and `--subscribes` are usage
errors against it. `intropy sys create` assembles the job into the system
host as `builder.AddJob("<app-id>")`, which runs it once at startup and
re-runs it on the development cadence, and the deploy templates render it as
a CronJob. The topology's `Job` kind is exempt from the no-edges warning: a
job's work is its own.

The job kind needs Intropy.Topology and the intropy CLI releases that know
it; an older CLI skips a `job` record in `sys create` with a warning.

## Parameters

| Name           | Required | Description                                                                                    |
| -------------- | -------- | ---------------------------------------------------------------------------------------------- |
| `name`         | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Int1055.NightlyCleanup`).      |
| `organization` | yes      | PascalCase organization name; telemetry ServiceNamespace and incident source URN.              |
| `idempotencyAppId` | no   | Dapr app-id of the Idempotency Service (default `idempotency-service.services`). Rendered into `src/appsettings.json`. |
| `businessIncidentsAppId` | no | Dapr app-id of the Business Incident Service (default `business-incident-service.services`). Same wiring as `idempotencyAppId`. |

## Render

```bash
intropy int create job -o /tmp/job-out \
  -f job/examples/minimal.yaml --version main --no-input
```
