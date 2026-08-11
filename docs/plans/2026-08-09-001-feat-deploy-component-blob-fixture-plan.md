---
title: feat: Add blob fixture to deploy-component local overlay
type: feat
status: active
date: 2026-08-09
---

# feat: Add blob fixture to deploy-component local overlay

## Overview

Add a fifth fixture type, `blob`, to the `deploy-component` template's local
overlay: one catalog entry, one new Dapr binding skeleton
(`bindings.aws.s3` against the Garage server the k3s setup scripts install),
one kustomization resource entry, and a README fixture-contract update.
Purely additive → SemVer minor on the next template release. No CLI release:
the CLI treats `spec.local.fixtures` as data and validates `--binding`
choices against whatever the catalog declares.

---

## Problem Frame

Connectors whose local fidelity is an S3-compatible blob store have no
fixture in the local overlay. The k3s setup scripts (owned by the
`intropy-dev-env` repo) install a Garage server for exactly this, but the
template catalog (`sftp`, `smb`, `http`, `file`) has no entry that renders a
binding pointing at it. Adding a fixture is, by the template's own contract,
"a skeleton file, an entry here, and the fixture server in the k3s setup
scripts — a template PR, not a CLI release." The fixture server side already
exists; this plan is the template PR.

---

## Requirements Trace

- R1. `spec.local.fixtures` in `deploy-component/template.yaml` lists `blob`
  after `file`.
- R2. `skeleton/overlays/local/fixtures/blob.yaml.tmpl` renders one
  `bindings.aws.s3` Component per connector bound to `blob`, following the
  exact shape of the existing fixture skeletons (per-connector loop,
  `binding` field with `$recorded` fallback, owner-is-first-scope rule).
- R3. The rendered Component points at
  `http://garage.fixtures.svc.cluster.local:3900` with bucket `dev-fixtures`,
  region `garage`, `forcePathStyle: "true"`, `disableSSL: "true"`, and the
  pinned Garage accessKey/secretKey as inline dev values:
  `GKdev0fixture0000000001` / `dev-fixture-secret-not-real-0001`
  (pinned in `intropy-dev-env/up.sh`).
- R4. `skeleton/overlays/local/kustomization.yaml.tmpl` includes
  `fixtures/blob.yaml` in `resources:`.
- R5. `deploy-component/README.md` fixture contract section lists the blob
  endpoint and states the contract is owned/verified by the
  `intropy-dev-env` repo.
- R6. A local render proves a `blob`-bound connector produces the expected
  Component before merge.

---

## Scope Boundaries

- No CLI changes — the catalog is data to the CLI; a template release
  suffices.
- No changes to the k3s setup scripts / Garage installation — that side
  already exists in `intropy-dev-env`.
- No new fixture types beyond `blob`; the catalog remains closed.
- No `spec.files` gating for the new skeleton — fixture files render empty
  (no document) when no connector selects them, same as the existing four.

### Deferred to Follow-Up Work

- Cutting the actual SemVer-minor template release tag: happens after merge,
  per the repo's normal release cadence.
- CLI cosmetic follow-up: `fixtureLabel` in
  `intropy-cli/internal/deploy/connectors.go` hardcodes descriptions for the
  four existing fixtures; add `blob: "S3 object store"` so the interactive
  selector shows a label rather than the bare name. Validation and rendering
  already work without it — purely cosmetic, no coupling to this release.
- Companion plan required in `intropy-dev-env` (out of scope here, tracked
  separately): the stated end-to-end flow (`up.sh` → `render | kubectl
  apply -f -`) needs two dev-env changes this plan does not cover — SFTPGo
  conformance to the sftp fixture contract (service `sftp` on port 22, user
  `intropy`/`intropy`; today it installs `sftpgo:2022` with `sftpgo`/
  `sftpgo`) and `<service>:dev` alias tags in `images/build.sh` so a
  zero-flag render's images resolve. Without them, only the blob leg of the
  flow works.

---

## Context & Research

### Relevant Code and Patterns

- `deploy-component/skeleton/overlays/local/fixtures/sftp.yaml.tmpl` — the
  closest model: an addressed server fixture with inline dev credentials
  (`intropy`/`intropy`) and the "dev values, not placeholders" rationale in
  the header comment. `blob.yaml.tmpl` mirrors this file most closely.
- `deploy-component/skeleton/overlays/local/fixtures/http.yaml.tmpl` and
  `file.yaml.tmpl` — confirm the shared loop/fallback/owner scaffolding and
  the three-paragraph header comment convention (what the fixture is /
  binding travels on the connector with `$recorded` fallback / owner-not-user
  scope rule).
- `deploy-component/skeleton/overlays/local/kustomization.yaml.tmpl` —
  explicit `resources:` list; every fixture file must be enumerated there.
- `deploy-component/template.yaml` — `local.fixtures` catalog with a comment
  block that stays accurate after the addition (no edit needed).
- `deploy-component/README.md` — "The fixture bindings live here." and "Dev
  values, not placeholders." paragraphs under the local-overlay section.
- `deploy-component/examples/` — `extractor.yaml` and `minimal.yaml` exist;
  neither exercises local bindings today, and they cannot: examples are
  `intropy int create` value files, while fixture bindings are injected by
  *manifest* rendering (`RenderManifests`), not scaffolding.
- `intropy-cli/internal/deploy/local_integration_test.go` —
  `TestLocalTemplatesRenderForLocalCluster` renders through the real library
  (gated on `INTROPY_TEMPLATES_DIR`), kustomize-builds the local overlay and
  validates the stream with `kubectl apply --dry-run=client`. This is the
  existing harness for exactly the proof R6 asks for.
- `intropy-dev-env/infra/dapr-s3-binding.yaml` — the dev env's own static
  Garage binding. Its metadata keys (`endpoint`, `bucket`, `region`,
  `forcePathStyle`, `disableSSL`, `accessKey`, `secretKey`) are already
  proven against the real Garage install; the fixture skeleton renders the
  same keys and values.

### Institutional Learnings

- None found under `docs/solutions/` for this surface.

---

## Key Technical Decisions

- **`bindings.aws.s3` as the Dapr binding type**: Garage is S3-compatible;
  the AWS S3 binding with `forcePathStyle` and `disableSSL` is the standard
  way to point Dapr at a non-AWS S3 endpoint.
- **Inline pinned credentials, matching the sftp fixture precedent**: the
  local cluster has nothing to keep secret; the README's "dev values, not
  placeholders" contract requires the rendered output to apply cleanly on
  first run, so no `REPLACE-ME-*` and no secret indirection. The pair is
  static and public — `GKdev0fixture0000000001` /
  `dev-fixture-secret-not-real-0001`, pinned in `intropy-dev-env/up.sh` — so
  it is committed directly, not sourced at implementation time.
- **String values `"true"` for booleans**: Dapr Component metadata values are
  strings; follows Dapr binding convention.
- **One atomic PR for all four edits plus the example**: the catalog entry,
  skeleton, and kustomization entry are incoherent separately; the README
  and example ride along.

---

## Open Questions

### Resolved During Planning

- Does the kustomization resource list need an entry? Yes — explicit list,
  confirmed missing from the original instructions; added as R4.
- Does the CLI need a release to accept `blob` in `--binding`? No — the
  template's own comment states validation is against the catalog as data.
- Does `spec.files` need a rule for the new skeleton? No — fixture files are
  unconditional; they render zero documents when unselected.
- Is `intropy-dev-env` the repo owning the k3s setup scripts? Yes —
  confirmed by user; the README ownership sentence is a rewording, not a new
  claim.

### Deferred to Implementation

- Exact wording of the README ownership sentence.

### Resolved After Review

- The pinned Garage accessKey/secretKey: no longer deferred —
  `GKdev0fixture0000000001` / `dev-fixture-secret-not-real-0001`, verified
  against `intropy-dev-env/up.sh` and `infra/dapr-s3-binding.yaml`.
- How U4 proves the render: not `intropy int create` (scaffolding never
  injects bindings). Bindings flow through `RenderManifests` — the
  verification uses the CLI integration test harness or a hand-driven
  `manifests render --env local --binding x=blob` (see U4).
- Committing an example under `deploy-component/examples/`: dropped. Those
  examples feed `int create`, which cannot express per-connector local
  bindings, so no committed example would exercise the skeleton.

---

## Implementation Units

- [ ] U1. **Catalog entry in template.yaml**

**Goal:** `blob` is a valid fixture choice for local renders.

**Requirements:** R1

**Dependencies:** None

**Files:**
- Modify: `deploy-component/template.yaml`

**Approach:**
- Append `blob` to `local.fixtures` → `[sftp, smb, http, file, blob]`. The
  comment block above stays as-is (it already describes exactly this change).

**Patterns to follow:**
- Existing list at `spec.local.fixtures`.

**Test scenarios:**
- Test expectation: none — one-token config change; proven end-to-end by U4's
  render.

**Verification:**
- `local.fixtures` renders as the five-entry list; no other manifest change.

---

- [ ] U2. **blob.yaml.tmpl fixture skeleton**

**Goal:** A connector bound to `blob` renders a `bindings.aws.s3` Component
pointing at the Garage fixture server.

**Requirements:** R2, R3

**Dependencies:** None (file is inert until U3 lists it and U1 admits the
value, but content is independent)

**Files:**
- Create: `deploy-component/skeleton/overlays/local/fixtures/blob.yaml.tmpl`

**Approach:**
- Copy `sftp.yaml.tmpl`'s structure verbatim; change the first header
  paragraph to describe the blob fixture (S3-compatible store via Garage,
  installed by the k3s setup scripts), and substitute `blob` for `sftp` in
  the two match positions of the `if` condition.
- Component body: `type: bindings.aws.s3`, `version: v1`, metadata entries
  `endpoint` = `http://garage.fixtures.svc.cluster.local:3900`,
  `bucket` = `dev-fixtures`, `region` = `garage`, `forcePathStyle` = `"true"`,
  `disableSSL` = `"true"`, `accessKey` = `GKdev0fixture0000000001`,
  `secretKey` = `dev-fixture-secret-not-real-0001`. Key names and values
  match `intropy-dev-env/infra/dapr-s3-binding.yaml` byte-for-byte where they
  overlap.
- Keep the `$recorded` fallback and the owner-is-first-scope condition
  byte-identical in shape to the other four files.

**Patterns to follow:**
- `deploy-component/skeleton/overlays/local/fixtures/sftp.yaml.tmpl`
  (addressed-server fixture with inline credentials).

**Test scenarios:**
- Happy path (via U4 render): connector with `binding: blob` → exactly one
  Component named after the connector, `type: bindings.aws.s3`, the six
  metadata values as above, scopes listing all connector app-ids.
- Edge case (via U4 render): component whose app-id is *not* first in the
  connector's scopes → renders no Component (owner rule).
- Edge case (via U4 render): no connector bound to `blob` → file renders
  zero documents (same as other fixtures).

**Verification:**
- File parses as a Go template under the same renderer settings as its
  siblings; render proof happens in U4.

---

- [ ] U3. **kustomization resource entry**

**Goal:** The rendered `fixtures/blob.yaml` is actually applied by the local
overlay.

**Requirements:** R4

**Dependencies:** U2 (the listed file must exist)

**Files:**
- Modify: `deploy-component/skeleton/overlays/local/kustomization.yaml.tmpl`

**Approach:**
- Add `- fixtures/blob.yaml` after `- fixtures/file.yaml` in `resources:`.

**Test scenarios:**
- Test expectation: none — one-line list addition; proven by U4's render +
  kustomize build.

**Verification:**
- Rendered local overlay's kustomization lists all five fixture files.

---

- [ ] U4. **Local render verification**

**Goal:** Prove the change end-to-end through the manifest-render path
before PR.

**Requirements:** R6

**Dependencies:** U1, U2, U3

**Files:**
- None committed. Verification only — no example is added under
  `deploy-component/examples/` (those feed `int create`, which cannot
  express per-connector bindings).

**Approach:**
- Bindings are injected by `RenderManifests`, so the render must go through
  manifest rendering, not `intropy int create`. Two equivalent harnesses:
  1. The CLI integration test against this checkout:
     `INTROPY_TEMPLATES_DIR=<this repo> go test -tags integration -count=1
     -run TestLocalTemplatesRenderForLocalCluster ./internal/deploy/`
     (extend it or run a variant with a `blob`-bound connector in the
     topology), or
  2. Hand-driven: `intropy manifests render --env local --binding
     <connector>=blob` against a workspace with a topology file declaring
     one connector, then `kustomize build` the staged local overlay.
- Inspect the rendered `overlays/local/fixtures/blob.yaml` and the built
  stream for the expected Component.

**Test scenarios:**
- Happy path: render with a `blob`-bound connector → Component present with
  `type: bindings.aws.s3`, the metadata values of R3, scopes listing all
  connector app-ids; `kustomize build` on the local overlay succeeds and
  includes it.
- Edge case: component whose app-id is *not* first in the connector's
  scopes → that component's overlay renders no Component for the connector
  (owner rule).
- Edge case: render with no `blob` binding → `blob.yaml` renders empty and
  `kustomize build` still succeeds.

**Verification:**
- Rendered output matches R3 exactly; the local overlay builds cleanly in
  every scenario.

---

- [ ] U5. **README fixture contract update**

**Goal:** The documented contract reflects five fixtures and the new owner.

**Requirements:** R5

**Dependencies:** U2, U3 (documents what exists)

**Files:**
- Modify: `deploy-component/README.md`

**Approach:**
- In "The fixture bindings live here.": update the catalog parenthetical to
  include `blob`.
- In "Dev values, not placeholders.": add the blob entry to the inline
  endpoint list — `garage.fixtures.svc.cluster.local:3900` (S3-compatible,
  bucket `dev-fixtures`) — alongside the existing four.
- Reword the contract sentence that currently reads "is shared with the k3s
  setup scripts" to state the contract is owned/verified by the
  `intropy-dev-env` repo (same claim, named owner).

**Test scenarios:**
- Test expectation: none — documentation.

**Verification:**
- Every enumeration of the fixture catalog in the README lists five entries
  and names the Garage endpoint; no stale four-item list remains (grep
  `sftp` across the repo to confirm).

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Wrong Dapr metadata key names for `bindings.aws.s3` (e.g. `endpoint` vs `url`) | Already de-risked: the keys are copied from `intropy-dev-env/infra/dapr-s3-binding.yaml`, proven against the real Garage install; spot-check against the Dapr AWS S3 binding reference docs anyway |
| Pinned Garage credentials drift from what `intropy-dev-env` installs | The pair is pinned in `intropy-dev-env/up.sh` and committed here verbatim; README names that repo as contract owner |
| Old CLI reading the new catalog | Not a risk — the catalog is data; old CLIs validate against whatever the fetched template declares |
| Interactive selector shows the bare `blob` name on current CLI releases | Cosmetic only; tracked as the `fixtureLabel` follow-up under Deferred to Follow-Up Work |

---

## Documentation / Operational Notes

- After merge: cut a SemVer-minor template release (GitHub release tag) so
  the CLI fetches the new catalog by default.
- The change is additive; already-scaffolded projects are unaffected (their
  rendered overlays were materialized at scaffold time).

---

## Sources & References

- Agreed scope: conversation of 2026-08-09 (change instructions "Phase 2 —
  intropy-templates (add blob, requires a release)", confirmed with
  kustomization step, README endpoint text, intropy-dev-env ownership, and
  validation option (a)).
- Plan review of 2026-08-09: U4 verification redirected from `int create`
  examples to the manifest-render path; pinned credentials committed
  verbatim; companion intropy-dev-env work (SFTP conformance, `:dev` image
  tags) flagged as required for the full end-to-end flow.
- Related code: `deploy-component/template.yaml`,
  `deploy-component/skeleton/overlays/local/fixtures/`,
  `deploy-component/skeleton/overlays/local/kustomization.yaml.tmpl`,
  `deploy-component/README.md`
- External docs: Dapr `bindings.aws.s3` component reference;
  `intropy-dev-env` repo (k3s setup scripts, pinned Garage credentials).
