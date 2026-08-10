# deploy-component

The per-block half of manifest generation: one workload and one thin overlay
per environment.

Rendered once per topology component into
`domains/<domain>/<system>/<component>/`.

## Workload follows the block kind

An extractor wakes on a schedule, pulls from its source and exits, so it renders
a `CronJob`. Every other block reacts to messages or requests and must stay
resident, so it renders a `Deployment`. `spec.files` picks one and never writes
the other.

This is not an invention: the hand-written manifests in two customer
repositories already run their extractors as CronJobs. The topology's block kind
is what lets the CLI derive it.

The CronJob's schedule is a `REPLACE-ME-CRON-SCHEDULE` placeholder — and its
one and only home. Activation cadence is deployment configuration, so the
topology carries no schedule to copy from; fill it in here and edit it here.

The local cluster is the one exception: a local render is applied without a
review step, so the job must run on first install. `base/cronjob-local.yaml`
carries a low-frequency dev cadence for it, and the `spec.files` rules pick
between the two files on `.env` — the schedule may not be templated on `.env`
inside one file, because everything outside `overlays/` must render
identically per environment (the CLI's mergeRendered refuses the render
otherwise). `base/kustomization.yaml` names the picked variant.

## The image tracks the dev tag

`spec.values.imageTag` is the literal `dev`: the tag CI pushes on every build,
so a rendered environment runs the freshest component image. `intropy deploy
pin` writes the digest when a deployment must be frozen.

The local render replaces the reference with a different convention — see "The
local overlay" below.

## imageNamespace must match CI

Customers disagree on the path segment between the registry and the image name —
one uses the customer slug, another uses `integrations`. The template defaults
to `integrations`. If CI pushes elsewhere, edit the created GitOps source before
merge so the workload and `component.yaml` name the same image repository.

## The local overlay

`overlays/local/` is rendered by `intropy manifests render --env local`: a
one-off render for the local development cluster, piped straight to
`kubectl apply -f -`. It differs from the environment overlays in three ways.

**The image reference is pinned to a `local/` name.** `deployment.yaml` and
`cronjob-local.yaml` render `image: local/{{ .name }}` — the prefix the k3s
setup scripts tag every side-loaded image under, no tag — exactly when `.env`
is `local`. `intropy manifests render` writes a root kustomization whose
`images[]` entries rewrite that reference to `local/<name>:dev`, the tag the
k3s setup scripts build and load component images under. kustomize matches
`images[]` on the exact rendered string and **silently ignores a miss**, so
the shape is part of the contract; the CLI also fails the render if
any built Deployment image has no tag, as a second net.

**The fixture bindings live here.** `overlays/local/fixtures/` carries one
Dapr binding skeleton per fixture type — the closed catalog declared in
`spec.local.fixtures` (`sftp`, `smb`, `http`, `file`, `blob`). Each file renders one
`Component` per topology connector whose binding for this local render names
that fixture, and nothing when none do. Repeat
`--binding <connector>=<fixture>` for reproducible renders; an interactive
terminal asks for omitted choices with a Huh selector. Choices are validated
against the catalog and never persisted.

**Dev values, not placeholders.** Every fixture skeleton points at the
conventional fixture server the k3s setup scripts always install —
`sftp.fixtures.svc.cluster.local:22` (`intropy`/`intropy`),
`smb.fixtures.svc.cluster.local:445`, `http.fixtures.svc.cluster.local:8080`
(wiremock-style stub), `garage.fixtures.svc.cluster.local:3900`
(S3-compatible, bucket `dev-fixtures`, pinned dev key pair) for `blob`, and
`/data/<connector>` in the component's own container for `file`. That fixture
contract — namespace, service names, ports, credentials, the `local/` image
reference above — is owned and verified by the `intropy-dev-env` repo (the
k3s setup scripts install the fixture servers) and is the only coupling
between the two; the rendered manifests are otherwise generic and apply to
any cluster with Dapr and the fixtures installed. Local output must apply
cleanly on first run, so nothing under `overlays/local/` renders a
`REPLACE-ME-*` value.

The overlay renders no namespace and no image override of its own: the CLI's
root kustomization sets both, so the same overlay serves any `--namespace`
and any `--image` override without re-rendering.

### Why a connector's binding lives with the component

`intropy manifests create` renders bindings on the host because
customer-cluster credentials are environment-owned and shared at the system
level. The local
cluster needs no secret material — fixture credentials are public dev values —
and the catalog files *can* live on the host (they render
identically there). They live here anyway: a connector is scoped to exactly
the app-ids that use it, and keeping the binding beside the block that
resolves it keeps one component's fixture choices out of every other
component's render. The host's `base/bindings/bindings.yaml` is not rendered
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
`.local.bindings` maps connector name to fixture name.
