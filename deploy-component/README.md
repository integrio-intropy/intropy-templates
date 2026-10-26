# deploy-component

The per-block half of manifest generation: one workload and one thin overlay
per environment.

Rendered once per topology component into
`domains/<domain>/<system>/<component>/`.

## Workload follows the block kind

An extractor and a transactional integration run to completion: each wakes on a
schedule, sweeps its source port, drains what it produced and exits, so it
renders a `CronJob`. Every other block reacts to messages or requests and must
stay resident, so it renders a `Deployment`. `spec.files` picks one and never
writes the other.

This is not an invention: the hand-written manifests in two customer
repositories already run their extractors as CronJobs, and the Aspire host
derives the same split from the same kinds. The topology's block kind is what
lets the CLI derive it.

The CronJob's schedule defaults to `* * * * *` — a once-a-minute dev cadence
— and is its one and only home. Activation cadence is deployment
configuration, so the topology carries no schedule to copy from; change it
here per environment.

## The image tracks the dev tag

`spec.values.imageTag` is the literal `dev`: the tag CI pushes on every build,
so a rendered environment runs the freshest component image. `intropy deploy
pin` writes the digest when a deployment must be frozen.

The local render replaces the reference with a different convention — see "The
local overlay" below.

## Each file port's adapter kind follows its binding

A component chooses the file adapter behind each port from `Ports:<port>:Kind`
and refuses to start without it outside Development. Every overlay, local
included, patches one `Ports__<port>__Kind` variable onto the workload for each
port whose scopes list the component's app-id: `file` renders `Local`, `sftp`
renders `Sftp`, and an `http` port is not a file port, so it gets none. It lives
in the overlays, not the base, because a port's binding may differ per
environment. No framework file adapter speaks the `blob` binding
(`bindings.aws.s3`) yet, so a blob port fails the render instead of the
component's first run.

## Loaders and transactional integrations get a redelivery policy

A loader leaves what it cannot load for redelivery: a failed send, a timeout,
a message no route handles. A transactional integration does the same with the
files it hops through its internal queue. Without a policy the sidecar does not
retry such a message at all; it hands it straight back to the broker, which
dead-letters it (see deploy-host). So these blocks render one more file,
`base/resiliency.yaml`: a Dapr `Resiliency` scoped to the block's app-id that
retries inbound deliveries, with exponential backoff, before the broker
dead-letters the message. A loader's target is the pub/sub its topics subscribe
through; a transactional integration's is the pub/sub its internal queue rides,
taken from the Subscription the topology mints for it (the system's
pub/sub, `<system>-pubsub`, on the topic `internal-<app-id>`). The block
kind is folded the way the CLI folds it, so `transactional-integration`,
`transactionalIntegration` and `TransactionalIntegration` all match.

The same `Resiliency` also retries the block's own calls to its ports: one
`outbound` target per port the topology gives the component, named after the
port (a port's Dapr binding takes its name), with its own policy
(`portCallMaxRetries`, default 5; `portCallMaxInterval`, default `10s`).
A share, SFTP server or HTTP API that is briefly unreachable then heals before
the call fails back into the pipeline. For a transactional integration this is
the only retry a destination outage gets: its send step turns a failed write
into a business incident and acknowledges the message, so the inbound
redelivery policy never sees it. An outage that outlasts the retries still ends
as that incident.

Two cautions:

- The block waits while the sidecar retries, so port retries spend the same
  processing window as redelivery retries. Keep the two policies together
  within the broker's timeouts.
- An outbound retry repeats the call. Writing a file again under the same name
  is harmless, but a `post` to an HTTP API that reached the API and then timed
  out may be delivered twice. Keep `portCallMaxRetries` low for an HTTP-bound
  port, or make the API idempotent.

## The workload is told its pub/sub name

The framework derives no Dapr component name. Each block runs on exactly one
pub/sub, and the topology names it on the component (`.component.pubsub`):
the system's bus for an extractor or a loader, the internal-queue pub/sub for
a transactional integration. The workload gets it in `INTROPY_PUBSUB_NAME`,
which wins over a name set in the component's code. A component the topology
gives no pub/sub gets no variable.

The workload is likewise told the one topic it runs on (`.component.topic`), in
`INTROPY_TOPIC_NAME`: a transactional integration's internal queue topic, which
the topology names `internal-<component>`, or the single topic an extractor's or
a loader's messages share. The framework derives no topic either. A component the
topology gives no single topic gets no variable.

`redeliveryMaxRetries` (default 10) and `redeliveryMaxInterval` (default `15s`)
tune it. The sidecar holds the message while it retries, so the whole window,
about `(retries + 1) x` the loader's `MaxMessageProcessingTime` plus the
backoff, must fit inside the broker's own lock or processing timeout.

## imageNamespace must match CI

Customers disagree on the path segment between the registry and the image name —
one uses the customer slug, another uses `integrations`. The CLI fills
`imageNamespace` from `deploy.yaml`, so the workload and `component.yaml` name the
repository CI pushes to; without it the template defaults to `integrations`.

## Pull secret, namespace and native sidecar

- `imagePullSecret`, from `deploy.yaml`, renders `imagePullSecrets` on the
  CronJob or Deployment, for a private registry project the namespace's service
  account cannot pull from. Empty renders none. The local overlay never gets it.
- The environment overlay's `namespace` is that environment's `namespace` in
  `deploy.yaml`, or `integration` when it names none.
- `nativeSidecar` (default true) runs a CronJob's daprd as a Kubernetes native
  sidecar, so a run that dies before shutting its sidecar down still completes
  the Job instead of blocking every later run under `concurrencyPolicy: Forbid`.
  It needs Kubernetes 1.29 or later; set it false for an older cluster.

## A file port mounts its claim into the sidecar

A `file` port's binding is `bindings.localstorage`, which runs in daprd, so its
directory must be mounted into the sidecar, not the app container. The
environment overlay of every component using the port mounts the port's claim,
`<port>-file`, at `/data/<port>` (`dapr.io/volume-mounts-rw` plus a pod volume).
deploy-host renders the claim next to the binding, with
`REPLACE-ME-STORAGE-CLASS` for the share behind it. The local overlay does the
same with its own fixture claims.

## The local overlay

`overlays/local/` is rendered by `intropy manifests render --env local`: a
one-off render for the local development cluster, piped straight to
`kubectl apply -f -`. It differs from the environment overlays in two ways.

**The image reference is pinned to a `local/` name.** `deployment.yaml` and
`cronjob.yaml` render `image: local/{{ .name }}` — the prefix the k3s
setup scripts tag every side-loaded image under, no tag — exactly when `.env`
is `local`. `intropy manifests render` writes a root kustomization whose
`images[]` entries rewrite that reference to `local/<name>:dev`, the tag the
k3s setup scripts build and load component images under. kustomize matches
`images[]` on the exact rendered string and **silently ignores a miss**, so
the shape is part of the contract; the CLI also fails the render if
any built Deployment image has no tag, as a second net.

**The fixture bindings live here.** `overlays/local/fixtures/` carries one
Dapr binding skeleton per fixture type — the closed catalog declared in
`spec.local.fixtures` (`sftp`, `http`, `file`, `blob`). Only fixture files selected by a
local port binding render; each selected file renders one `Component`
per matching topology port. Repeat
`--binding <port>=<fixture>` for reproducible renders; an interactive
terminal asks for omitted choices with a Huh selector. Choices are validated
against the catalog and never persisted.

**Dev values, not placeholders.** Every fixture skeleton points at the
conventional fixture server the k3s setup scripts always install —
`sftp.fixtures.svc.cluster.local:22` (`intropy`/`intropy`, root path
`/home/intropy`), `http.fixtures.svc.cluster.local:8080`
(wiremock-style stub), `garage.fixtures.svc.cluster.local:3900`
(S3-compatible, bucket `dev-fixtures`, pinned dev key pair) for `blob`, and
`/data/<port>` for `file`. A `file` port has no server: its directory is a
`PersistentVolumeClaim`, `<port>-file`, rendered by the port's owning
component and mounted into the sidecar of every component using the port
(`dapr.io/volume-mounts-rw`), because the `localstorage` binding runs in
daprd and daprd's filesystem is read-only. A claim, not an `emptyDir`, so a
file dropped between two CronJob runs is still there for the next one. That fixture
contract — namespace, service names, ports, credentials, the `local/` image
reference above — is owned and verified by the `intropy-dev-env` repo (the
k3s setup scripts install the fixture servers) and is the only coupling
between the two; the rendered manifests are otherwise generic and apply to
any cluster with Dapr and the fixtures installed. Local output must apply
cleanly on first run, so nothing under `overlays/local/` renders a
`REPLACE-ME-*` value. The SFTP fixture's `insecureIgnoreHostKey` is the
same trade: the fixture server is a disposable dev pod whose host key
changes on every reinstall, so pinning it serves nothing. That bypass is
part of the disposable fixture contract only — the GitOps SFTP adapter on
deploy-host pins `hostPublicKey` and never renders the flag.

The local fixture catalog and deploy-host's GitOps binding catalog
(`spec.gitops.bindingKinds`) are separate contracts, kept deliberately:
fixture files carry dev values next to the component, GitOps adapters carry
reviewed, Secret-backed Components on the host. A binding kind must exist
in the catalog for its side of the render to offer it; today both catalogs
are `sftp`, `http`, `file`, `blob`.

The overlay renders no namespace and no image override of its own: the CLI's
root kustomization sets both, so the same overlay serves any `--namespace`
and any `--image` override without re-rendering.

### Why a port's binding lives with the component

`intropy manifests create` renders bindings on the host because
customer-cluster credentials are environment-owned and shared at the system
level. The local
cluster needs no secret material — fixture credentials are public dev values —
and the catalog files *can* live on the host (they render
identically there). They live here anyway: a port is scoped to exactly
the app-ids that use it, and keeping the binding beside the block that
resolves it keeps one component's fixture choices out of every other
component's render. The host's `base/bindings/` adapters are not rendered
locally — the CLI renders only the local overlay, and only this overlay
references fixture files.

### ApplicationSet safety

If this rendered tree ever lands in a GitOps repository, `overlays/local/`
must not produce an ArgoCD Application. It cannot: the ApplicationSet that
fans overlays out to clusters generates from its *cluster* generator, which
matches only clusters registered in ArgoCD — the local development cluster
never is — so a `local` overlay resolves to no cluster and no Application.
There is no path segment in the generator to match on.

## Reserved values

`.env`, `.topology`, `.gitops`, `.component` (this block's derived record),
`.scaffold` (its `.intropy/scaffold.json` values, or `{}`) and — local
renders only — `.local`, the ephemeral bindings for this render:
`.local.bindings` maps port name to fixture name.
