# intropy-templates

A library of **Intropy templates** — scaffolds rendered into
working projects by the `intropy` CLI (separate repo:
`integrio-intropy/intropy-cli`). This repo contains content, not code: the CLI
is the only renderer.

One engine: Go `text/template` + [sprig](https://masterminds.github.io/sprig/).
One manifest format. One skeleton tree per template.

## Templates

- **`hello-world/`** — minimal example template that exercises the manifest
  and skeleton conventions.
- **`transactional/`** — a transactional integration component template.

Rendered by `intropy manifests create` rather than `intropy int create`, into
a GitOps repository rather than a working project:

- **`deploy-host/`** — a system's shared Dapr components, owned by one ArgoCD
  Application.
- **`deploy-component/`** — one block's workload (`CronJob` for an extractor,
  `Deployment` otherwise) and its per-environment overlays.

Both also ship a `local` overlay, built by
`intropy manifests render --env local`: dev-valued manifests for the local
development cluster,
including deploy-component's closed fixture catalog (`spec.local.fixtures`) —
one Dapr binding skeleton per fixture type, pointing at the servers the k3s
setup scripts always install. Each template's README documents the overlay and
the fixture contract. Run `intropy manifests inspect` from a system workspace
to see the topology and unresolved connector choices before rendering.

Those two are the only templates that use `spec.files` to decide which files
exist at all. See [`CLAUDE.md`](./CLAUDE.md) for the rule — and for why no
older template may adopt it yet.

## Template layout

```
<template>/
  template.yaml          required: the intropy.dev/v1 manifest
  skeleton/              required: rendered into the user's --output
    <files…>             `.tmpl` files are templated; everything else is copied
  README.md              optional: author-facing notes
  examples/              optional: test fixtures for local renders
```

Only `template.yaml` and `skeleton/` are seen by the renderer. Anything else at
the template root is for the author, not the scaffolded project.

## Rendering locally

```bash
intropy int create hello-world -o /tmp/hello-out \
    -f hello-world/examples/minimal.yaml --version main
```

Pass `--version main` while there is no release yet. Once releases exist, the
default `--version` resolves to the latest tag.

## Conventions

See [`CLAUDE.md`](./CLAUDE.md) for the full manifest schema and skeleton
conventions. In short:

- File contents are templated only when the filename ends in `.tmpl` (the
  suffix is stripped on output). Path segments are always templated.
- Reference parameters and derived values with `{{ .paramName }}`.
- The renderer runs with `missingkey=error`, so a typo like `{{ .Name }}`
  fails the render instead of producing an empty string.
