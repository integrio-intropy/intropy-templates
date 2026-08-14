# deploy-host

The system-level half of manifest generation: the Dapr components a system's
blocks resolve by name.

Rendered once per system into `domains/<domain>/<system>/host/`.

## Why these objects live here

A Dapr `Component` is namespace-scoped, and every integration in the
cluster's integration namespace shares it. So exactly one ArgoCD Application may own each one. If each
block owned a copy, N Applications would claim the same `Component/pubsub` and
whichever synced last would win — a live example of that exists in one customer
repository today.

This directory is that single owner. `scopes:` is what limits *use*: it lists
exactly the app-ids the topology says publish to or subscribe from the broker.
Single ownership plus scopes is the correct pair; scopes alone is not.

Its `component.yaml` declares `kind: shared`, which is how `intropy deploy` and
`intropy deploy promote` know to refuse it — there is no image to pin.

## Which files get rendered

`spec.files` decides the *file set*, not just the contents:

| condition | effect |
|---|---|
| `pubsub` | only the matching `base/dapr/pubsub-<broker>.yaml` |
| a port selects a binding kind | that kind's `base/bindings/<adapter>.yaml` |

`base/kustomization.yaml` references `dapr/pubsub-{{ .pubsub }}.yaml`, so the
list needs no conditional — the file name follows the parameter.

Adding a broker is a new `pubsub-<name>.yaml.tmpl`, a new `enum` value and a new
rule. The CLI is not involved: it passes `platform.pubsub` through from
`deploy.yaml` without knowing what the values mean.

## Shared pub/sub is identical across environments

The pub/sub components live in `base/` and do not vary by environment. What varies
is the *credential*, and a Component reaches that through `secretKeyRef` — so the
same YAML resolves to different values per environment's Secret. The overlays are a
`namespace:` and a `resources:` line; the namespace is always `integration`, the
single integration namespace every system and environment on the cluster shares, so
it is a literal in the overlay rather than a parameter.

One customer repository does vary the broker per overlay (in-memory in dev,
RabbitMQ in prod). That is a deviation, not the convention, and this template
does not reproduce it.

The local cluster is the exception, and it is no variation at all:
`intropy manifests render --env local` always renders with
`pubsub: rabbitmq`, the broker the k3s setup scripts install, so only
`pubsub-rabbitmq.yaml` reaches a
local render. Its address (`rabbitmq.fixtures.svc.cluster.local:5672`) and
credentials (`guest:guest`) are the fixture contract — dev values kept inline
because there is nothing to keep secret locally. Customer environments instead
reference the platform-owned Secret named by the template.

## Binding kinds

`intropy manifests create --binding <port>=<kind>` renders the shared
GitOps binding Component for that port from the adapter file for the
selected kind. The selection is independent of `manifests render --env local`,
which resolves local fixture bindings without reading or writing the GitOps
repository. The GitOps catalog (`spec.gitops.bindingKinds`) and the local
fixture catalog (deploy-component's `spec.local.fixtures`) are separate
contracts: the GitOps side renders reviewed, Secret-backed Components; the
local side renders dev-valued fixtures.

`base/bindings/` carries one file per adapter, each emitting one
`Component` per port that selected it:

| kind | Dapr type | required metadata |
|---|---|---|
| `sftp` | `bindings.sftp` | `address`, `rootPath`, `username`, `password` or `privateKey` (exactly one profile), `hostPublicKey` |
| `http` | `bindings.http` | `url` |
| `file` | `bindings.localstorage` | `rootPath` |
| `blob` | `bindings.aws.s3` | `bucket`, `region`, `accessKey`, `secretKey` |

The catalog names do not all match the Dapr type: there is no
`bindings.file` (the file adapter is `bindings.localstorage`) and no
`bindings.blob` (the blob adapter is `bindings.aws.s3`). A Kubernetes
deployment using the file adapter must mount suitable storage at `rootPath`
on every pod that resolves the binding. The blob adapter declares no
`endpoint`: it is optional, and setting one is wrong for ordinary AWS S3 —
an S3-compatible store is the reviewer's per-environment addition.

### The Secret contract

With no auth block, `secretKeyRef` resolves against a plain Kubernetes
Secret in this namespace. That Secret is platform-owned (synced from Key
Vault, Vault, or applied by hand) — the template only declares the name and
the port-specific keys the platform must provide:

| key | used by |
|---|---|
| `<port>-sftp-username` | `sftp` |
| `<port>-sftp-password` | `sftp` (password profile) |
| `<port>-sftp-private-key` | `sftp` (private-key profile) |
| `<port>-sftp-host-public-key` | `sftp` |
| `<port>-s3-access-key` | `blob` |
| `<port>-s3-secret-key` | `blob` |

Endpoints (`address`, `url`, `bucket`, `region`, `rootPath`) remain
`REPLACE-ME` values for the reviewer; credentials never render as values.

SFTP host validation is mandatory in GitOps output: the adapter pins the
server's SSH host public key via `hostPublicKey`, and never renders
`insecureIgnoreHostKey`. The private-key profile may add
`privateKeyPassphrase`; the reviewer deletes whichever authentication
profile is not in use. The local SFTP *fixture* is the deliberate opposite
— a disposable dev server whose host key is not worth pinning — and that
bypass may never travel into GitOps output.

### Which adapter files exist

`spec.files` renders an adapter file only when at least one port
selected its kind — an unselected adapter would render zero documents, and
an empty multi-doc file in a kustomize `resources` list breaks the build.
`base/kustomization.yaml` collects the same kind set from the ports,
deduplicated, so its references always match the rendered files. Adding a
binding kind is a new adapter file, a `spec.files` rule, a `bindingKinds`
entry, a kustomization reference, and a row in the table above — a template
PR, not a CLI release.

## The local overlay

`overlays/local/` is rendered only by `intropy manifests render --env local`. It
is an ordinary thin overlay: `resources: - ../../base` and no namespace line —
the CLI's root kustomization sets the namespace, so the overlay serves any
`--namespace` without re-rendering.

What it deliberately does not carry is port bindings. The fixture
catalog (the local counterpart of `base/bindings/`) lives on deploy-component,
next to the block that resolves each binding; deploy-component's README
explains why, and declares the catalog in `spec.local.fixtures`. The fixture
broker carries its dev credentials inline and does not depend on the customer
environments' Secret contract.

`manifests create` excludes this overlay and every local fixture from the
GitOps repository.

## Reserved values

Beyond the declared parameters, the CLI injects:

- `.env` — the environment being rendered. Only meaningful under `overlays/`; the
  CLI refuses a skeleton where it changes anything else.
- `.topology` — the derived system model: `pubsubs` (with `appIds` for `scopes:`),
  `ports` (name, directions, appIds and a selected binding kind), `topics`,
  `components`. GitOps binding metadata remains `REPLACE-ME` for reviewers to
  complete.
- `.gitops` — `domain`, `system`, `component`, `host`, `registry`,
  `argocdAppNamespace`, `environments`, `platform`.

## Check a change locally

From a system workspace, validate a GitOps render without creating files or a
review branch:

```sh
intropy manifests create --env dev --domain sales \
  --template-version feature/my-change --dry-run
```

Validate the local build separately; stdout is the complete Kubernetes YAML
stream:

```sh
intropy manifests render --env local \
  --template-version feature/my-change > /tmp/manifests.yaml
```
