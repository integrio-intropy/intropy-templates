# deploy-component

The per-block half of `intropy deploy init`: one workload and one thin overlay
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

## The image is never pinned here

`spec.values.imageTag` is the literal `unpinned`. `intropy deploy` writes the
digest, and it is the only thing that may. A tag here would let what runs change
without a deployment — which is what got an earlier version of this command
deleted.

`unpinned` rather than `latest` on purpose: it fails to pull loudly, and it reads
correctly in an `ImagePullBackOff`.

The local render replaces the sentinel with a different convention — see "The
local overlay" below.

## imageNamespace has no safe default

Customers disagree on the path segment between the registry and the image name —
one uses the customer slug, another uses `integrations`. The default is
`integrations`; override it with `--set imageNamespace=<slug>` or a values file.
It must match what CI actually pushes.

## The local overlay

`overlays/local/` is rendered by `intropy int local`, not `deploy init`: a
one-off render for the local development cluster, piped straight to
`kubectl apply -f -`. It differs from the environment overlays in three ways.

**The image reference is pinned to a bare name.** `deployment.yaml` and
`cronjob-local.yaml` render `image: {{ .name }}` — no registry prefix, no
tag — exactly when `.env` is `local`. `int local` writes a root kustomization
whose `images[]` entries rewrite that reference to `<name>:dev`, the tag the
k3s setup scripts build and load component images under. kustomize matches
`images[]` on the exact rendered string and **silently ignores a miss**, so
the bare-name shape is part of the contract; the CLI also fails the render if
any built Deployment image has no tag, as a second net.

**The fixture bindings live here.** `overlays/local/fixtures/` carries one
Dapr binding skeleton per fixture type — the closed catalog declared in
`spec.local.fixtures` (`sftp`, `smb`, `http`, `file`). Each file renders one
`Component` per topology connector whose recorded binding in the workspace's
`.intropy/local.yaml` names that fixture, and nothing when none do; the CLI
fails the render if every catalog file comes out empty, because a bindingless
local overlay is a template release bug, not a system state. `int local`
prompts for and validates the bindings, so the values are always catalog
members.

**Dev values, not placeholders.** Every fixture skeleton points at the
conventional fixture server the k3s setup scripts always install —
`sftp.fixtures.svc.cluster.local:22` (`intropy`/`intropy`),
`smb.fixtures.svc.cluster.local:445`, `http.fixtures.svc.cluster.local:8080`
(wiremock-style stub), and `/data/<connector>` in the component's own
container for `file`. That fixture contract — namespace, service names,
ports, credentials, the bare image reference above — is shared with the k3s
setup scripts and is the only coupling between the two; the rendered
manifests are otherwise generic and apply to any cluster with Dapr and the
fixtures installed. Local output must apply cleanly on first run, so nothing
under `overlays/local/` renders a `REPLACE-ME-*` value.

The overlay renders no namespace and no image override of its own: the CLI's
root kustomization sets both, so the same overlay serves any `--namespace`
and any `--image` override without re-rendering.

### Why a connector's binding lives with the component

`deploy init` renders bindings on the host because a customer cluster
resolves their credentials through the system's secret store, which the host
owns. The local cluster needs no secret store — fixture credentials are
public dev values — and the catalog files *can* live on the host (they render
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
renders only — `.local`, the decided bindings: `.local.bindings` maps
connector name to fixture name.
