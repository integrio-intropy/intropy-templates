# deploy-host

The system-level half of `intropy deploy init`: the Dapr components a system's
blocks resolve by name, the secret store behind them, and — on a platform
without an external one — the Secret holding the placeholders.

Rendered once per system into `domains/<domain>/<system>/host/`.

## Why these objects live here

A Dapr `Component` is namespace-scoped, and every integration in a customer's
namespace shares it. So exactly one ArgoCD Application may own each one. If each
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
| `secretStore` | only the matching `base/dapr/secretstore-<store>.yaml` |
| `secretStore == kubernetes` | `base/secrets/` at all |
| topology has connectors | `base/bindings/` at all |

`base/kustomization.yaml` references `dapr/pubsub-{{ .pubsub }}.yaml`, so the
list needs no conditional — the file name follows the parameter.

Adding a broker is a new `pubsub-<name>.yaml.tmpl`, a new `enum` value and a new
rule. The CLI is not involved: it passes `platform.pubsub` through from
`deploy.yaml` without knowing what the values mean.

## Everything is identical across environments

The Dapr components live in `base/` and do not vary by environment. What varies
is the *credential*, and a Component reaches that through `secretKeyRef` — so the
same YAML resolves to different values in each environment's namespace. The
overlays are a `namespace:` and a `resources:` line.

One customer repository does vary the broker per overlay (in-memory in dev,
RabbitMQ in prod). That is a deviation, not the convention, and this template
does not reproduce it.

## Reserved values

Beyond the declared parameters, the CLI injects:

- `.env` — the environment being rendered. Only meaningful under `overlays/`; the
  CLI refuses a skeleton where it changes anything else.
- `.topology` — the derived system model: `pubsubs` (with `appIds` for `scopes:`),
  `connectors` (name, directions, appIds — the topology mints names only;
  binding types are owned here, so every connector renders as a REPLACE-ME
  scaffold), `topics`, `components`.
- `.gitops` — `domain`, `system`, `component`, `host`, `registry`,
  `argocdAppNamespace`, `environments`, `platform`.

## Check a change locally

```sh
intropy deploy init --domain sales --topology topology.json --plan
```

Then render what it produced:

```sh
kustomize build domains/sales/<system>/host/overlays/dev
```
