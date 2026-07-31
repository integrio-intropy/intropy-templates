# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

## What this repo is

A library of **Intropy templates**. Each template is a scaffold
rendered into a working project by the `intropy` CLI (separate repo:
`integrio-intropy/intropy-cli`). The CLI is the only renderer; this repo
contains content, not code.

The model is intentionally narrow: one engine (Go `text/template` + sprig),
one manifest format, one source of truth per template.

## Repository layout

```
<template>/
  template.yaml          # required: the intropy.dev/v1 manifest
  skeleton/              # required: rendered into the user's --output
    <files…>             # `.tmpl` files are templated; everything else is copied
  README.md              # optional: author-facing — what this template produces
  examples/              # optional: test fixtures for local renders
    minimal.yaml
    full.yaml
```

Rules:

- The CLI selects a template via positional argument:
  `intropy int create hello-world -o ./out`.
- Only `<template>/template.yaml` is parsed; only `<template>/skeleton/` is
  walked by the renderer.
- Anything else at the template root (README, examples, CHANGELOG, ADRs) is
  invisible to the renderer — it's for the template author, not the
  scaffolded project.
- There is **no shared content between templates**. No repo-level
  `skeletons/` dir, no cross-template includes. If two templates need the
  same file, duplicate it. (Composition happens at the *output* level:
  a template may declare whole sibling templates under `spec.dependencies`
  — see below — but never share skeleton files with them.)

## Manifest schema (`<template>/template.yaml`)

```yaml
apiVersion: intropy.dev/v1
kind: Template
metadata:
  name: <kebab-case>           # convention: match the directory name
  title: <Title Case>
  description: <one sentence>
  tags: [example, go, http]
  labels:                      # two intropy.dev/* keys are load-bearing (see below)
    intropy.io/template-level: example
    intropy.dev/block-kind: extractor   # extractor | loader | aggregator | transactional-integration
    intropy.dev/data-flow: "in"         # in | out | both | internal
spec:
  parameters:                  # raw JSON Schema; type must be "object"
    type: object
    required: [name]
    properties:
      name:
        type: string
        title: Service name
        description: Lowercase kebab-case identifier.
        pattern: "^[a-z][a-z0-9-]*$"
      port:
        type: integer
        default: 8080
  values:                      # optional; derived values rendered via Go template + sprig
    module: 'github.com/example/{{ .name }}'
  dependencies:                # optional; sibling templates scaffolded when missing
    - template: shared-contracts  # directory name in this repo
      output: '{{ .contracts }}'  # Go template; single path segment (conventionally "Contracts")
      values:                  # Go templates over this template's resolved values
        name: '{{ .contracts }}'
```

Notes:

- **`spec.parameters` is plain JSON Schema.** Types: `string`, `boolean`,
  `integer`, `number`. Supported attributes: `title`, `description`, `pattern`,
  `enum`, `default`. The same schema validates CLI inputs and drives the
  Backstage form when a Template entity references this template.
- **Parameter declaration order matters** for human display. Keep the YAML
  order intentional.
- **`spec.values` derives string values from parameters.** Each entry is a Go
  template (with sprig) rendered against the resolved parameters. Use for
  composite values needed in multiple skeleton files (e.g. a module path).
  Output is always a string. Don't chain entries — map iteration order
  is non-deterministic.
- **Two labels are load-bearing.** The CLI reads `intropy.dev/block-kind`
  (`extractor` | `loader` | `aggregator` | `transactional-integration`) and
  `intropy.dev/data-flow` (`in` | `out` | `both` | `internal`) at render time
  and records them in the scaffolded project's `.intropy/scaffold.json`, which
  `intropy sys create` later assembles into the system declaration. Templates
  that are a system block must declare both. Everything else under `labels`
  is free-form.
- **`spec.dependencies` composes whole templates at the output level.** Each
  entry names a sibling template in this repo, an `output` (a Go template
  that must render to a single path segment — the dependency is created as a
  direct sibling of the component's output directory), and a `values` map
  (Go templates rendered against the declaring template's resolved values).
  The CLI renders dependencies from the same fetched tag right after the
  component render. Idempotency: a target directory whose
  `.intropy/scaffold.json` names the same template is **skipped silently**
  (drift in values only warns); a missing or empty directory is rendered; a
  foreign scaffold record or an unmanaged non-empty directory is an error.
  `--force` never propagates to dependencies. The component's scaffold
  record lists every declared dependency under `dependsOn`.
  Authoring rules:
  - A dependency render never prompts — the `values` map plus the dependency
    template's own defaults must cover all its required parameters.
  - Shared-support templates (like `shared-contracts`) declare
    `intropy.dev/template-role: shared-library` and **no**
    `intropy.dev/block-kind` / `data-flow` labels; the role is recorded in
    scaffold.json so system assembly can tell them apart from blocks.
  - A skeleton may reference its declared dependencies (e.g. a
    `ProjectReference` to `../../<dep>/…`) — the mechanism guarantees the
    sibling exists. The system-host skeleton never references the workspace's
    shared contracts project; `intropy sys create` discovers it by its
    scaffold record and inserts that reference itself.
- **`spec.files` decides which files exist at all**, where `spec.parameters`
  only decides what is in them. Each entry is a `path` glob relative to
  `skeleton/` plus a `when` — a Go template (sprig available) rendered against
  the resolved values. Any result other than `""`, `"false"` or `"0"` includes
  the match.

  ```yaml
  files:
    - path: base/dapr/pubsub-servicebus.yaml.tmpl
      when: '{{ eq .pubsub "servicebus" }}'
    - path: base/dapr/pubsub-rabbitmq.yaml.tmpl
      when: '{{ eq .pubsub "rabbitmq" }}'
    - path: base/secrets/**
      when: '{{ eq .secretStore "kubernetes" }}'
  ```

  Rules:
  - **The first matching rule decides**, so a specific rule can override a
    broader one placed after it. A path no rule matches is **included** — which
    is why every template written before this field existed renders unchanged.
  - **Paths match the source**, `.tmpl` suffix included. A rule that decides on
    values cannot depend on a path those values produce, so a
    `{{ .name }}/` segment is only reachable via a glob.
  - **A trailing `/**` matches the directory and everything under it**, and
    prunes the subtree *before* its contents are parsed. So a skeleton may carry
    a file that is not even a valid template for the values in play.
  - `*` does not cross a `/`, so `dapr/*.yaml` cannot silently prune
    `dapr/nested/x.yaml`.
  - `when` is required and parsed at load time: a syntax error fails
    immediately rather than part way through a render. `missingkey=error`
    applies, so a typo'd value name is a loud error, not a silent skip.
  - **Prefer a value-derived filename over a conditional list.** If the rendered
    file is named after the parameter that selected it
    (`pubsub-{{ .pubsub }}.yaml`), a `kustomization.yaml` referencing it needs
    no conditional at all.

  ⚠️ **Do not add `spec.files` to a pre-existing template until the CLI floor
  moves.** The CLI parses manifests without rejecting unknown fields, so an
  older CLI reading a template that uses `spec.files` silently **ignores the
  filter and renders every file** — a wrong answer with no error. New templates
  fetched only by a new command (`deploy-host`, `deploy-component`) are safe
  because no old CLI ever fetches them.

- **No `spec.steps`**, no `spec.owner`, no `nextSteps`. The model is
  intentionally narrow: a manifest declares parameters and the skeleton tree
  describes what gets written.

## Skeleton conventions (`<template>/skeleton/`)

- **File contents are templated only if the filename ends in `.tmpl`.** The
  `.tmpl` suffix is stripped on output (`README.md.tmpl` → `README.md`).
- **Filenames and directory names are templated** with the same `{{ .param }}`
  syntax as file contents (e.g. `src/{{ .name }}.csproj`). The `.tmpl` suffix
  only governs whether *file contents* are rendered; path segments are always
  templated.
- **Use `{{ .paramName }}` to reference parameters and derived values.** The
  data context is a flat map containing parameters + `spec.values` entries.
- **Sprig is available.** `upper`, `lower`, `title`, `kebabcase`,
  `snakecase`, `replace`, `default`, `trim`, `len`, `get`, `dict`. Full list:
  https://masterminds.github.io/sprig/ .
- **Missing keys are a hard error.** The renderer runs with
  `missingkey=error`. Typos like `{{ .Name }}` (wrong case) fail the render
  with a clear message — they don't silently produce empty strings.
- **Sprig functions with side effects exist** (`env`, `now`, `uuidv4`,
  `getHostByName`). Don't use them — they break the "same inputs → same
  output" reproducibility guarantee.

Example skeleton file `skeleton/README.md.tmpl`:

```markdown
# {{ .name }}

Generated from the `{{ .name }}` template.

Module: `{{ .module }}`
```

## The `AGENTS.md` convention

Every skeleton ships an `AGENTS.md.tmpl` at its root. `AGENTS.md` (per the
[agents.md](https://agents.md/) standard, auto-loaded by most coding agents)
is the scaffolded project's **manifest for agents**: the facts about *this*
component that no skill can know. It is not a tutorial — generic framework
how-to lives in the Intropy skills collection (`intropy skills collection add
--name intropy --ref harbor.intropy.io/skills/index:latest`), which
`int create` offers to install into `.agents/skills/`.

Each skeleton also ships a one-line `CLAUDE.md` containing exactly
`@AGENTS.md` — Claude Code doesn't auto-load AGENTS.md, so this import gives
it the same context. Keep it one line; never put content in it.

Rules for authoring `AGENTS.md.tmpl`:

- **Facts only, no teaching.** State what the component is, its component /
  topic / binding names (with rootPaths and ports), app id, and the key-file
  map. Do not restate framework conventions (naming-sync rules, builder
  usage, DI patterns) — those belong to the skills and duplicating them here
  drifts.
- **One canonical run path.** If the skeleton ships a `Taskfile.yml`,
  `task run` is canonical: `AGENTS.md` points at it and briefly says what it
  does; it never duplicates the underlying `dapr run` command. If there is no
  Taskfile, the raw `dapr run` command lives in `AGENTS.md` (and `README.md`
  must agree with it — same ports, same flags).
- **Project-specific deviations are facts.** Deliberate departures from
  framework defaults (e.g. "idempotency omitted in this sample; add
  `.WithIdempotency(...)` in …") belong in a short Development notes section.
- **One skills pointer.** End with a single "Framework guidance" line
  pointing at the skills collection — no per-skill routing table.
- **Section structure:** title + one-liner, Project overview, Important
  files, Build and run, optional Development notes / Testing, Framework
  guidance.

The facts in `AGENTS.md` must match the skeleton (component YAML `metadata.name`
and rootPaths, `Constants.cs` values, Taskfile vars, `.http` ports). When you
change one, change the others in the same commit.

## Writing style for descriptions

Descriptions are CLI output, not documentation. `intropy template show`
prints `metadata.description` verbatim under the title and each parameter's
`description` indented under its name; the interactive prompter appends it
to the prompt label as `Title (description): `. Write for a narrow terminal:
the intropy-cli `AGENTS.md` writing style is the house style, and the parts
that apply here are:

- **`metadata.description`:** one sentence. What the scaffolded thing is and
  does, nothing else. Environment facts (CronJob in production, Deployment,
  how `sys create` assembles around it) belong in the template's README.md.
- **Parameter `title`:** a terse noun phrase, no trailing period (`Pub/sub
  broker`, `Contract type`). It is concatenated with the description, so
  never repeat the title's words in the description's opening.
- **Parameter `description`:** one full sentence with a trailing period,
  ~25 words. A second sentence is allowed only for the consequence of the
  choice ("Only "kubernetes" commits a Secret stub; the others resolve out
  of cluster."). Rename burdens, migration caveats, and codegen internals
  belong in the template's README.md.
- **Explain why, never narrate history.** No PR or issue references, no
  "used to", "now", "previously", no "customers differ" stories. State the
  invariant ("An extractor is scheduled and exits; everything else stays
  resident.") and let git keep the archaeology.
- **`spec.values` comments follow the same rule:** state what must hold
  ("Must match the CLI's pascalCase(name)"), not the saga of what breaks.

## Adding a new template

1. Create the directory: `mkdir -p <name>/skeleton <name>/examples`.
2. Write `<name>/template.yaml` (manifest).
3. Write skeleton files under `<name>/skeleton/`. Suffix anything you want
   templated with `.tmpl`.
4. Write `<name>/README.md` describing what the template produces and what
   parameters it takes (this is for humans browsing the repo, not for the
   scaffolded project).
5. Write at least one `<name>/examples/minimal.yaml` containing values that
   satisfy the required parameters.
6. Render it locally to confirm it works:

   ```bash
   intropy int create <name> -o /tmp/<name>-out \
     -f <name>/examples/minimal.yaml --version main
   ```

   Pass `--version main` while we don't have a release yet. Once releases
   exist, the default `--version` resolves to the latest tag.

7. Inspect `/tmp/<name>-out/` and confirm the rendered output matches what
   you expected.

## Releases and versioning

The CLI fetches templates by GitHub release tag:

- `intropy int create <name> -o ./out` → uses the latest GitHub release.
- `intropy int create <name> -o ./out --version v0.2.1` → uses that tag.
- `intropy int create <name> -o ./out --version main` → uses the default
  branch (works on any ref the GitHub tarball endpoint accepts).

To ship a new template version, cut a GitHub release. There is no
intermediate index. The template version is the release tag, not a commit
SHA — all templates in the repo share the same release cadence.

## What this repo is NOT

- **Not a Backstage scaffolder template repo.** Don't add
  `scaffolder.backstage.io/v1beta3` files. Don't add `spec.steps`,
  `intropy:workspace:template`, `publish:gitlab:merge-request`, or any
  scaffolder action references. Backstage talks to templates through a
  custom action that shells out to the `intropy` CLI; it does not render
  templates itself.
- **Not a multi-engine repo.** The renderer is Go `text/template` + sprig,
  full stop. One engine, one skeleton tree per template.
- **Not a place for runtime code.** The CLI lives in
  `integrio-intropy/intropy-cli`. This repo ships content only.
