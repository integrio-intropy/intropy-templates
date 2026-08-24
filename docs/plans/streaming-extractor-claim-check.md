# Plan: reshape streaming-extractor from batch protocol to streamed claim-check sweep

> Repo: `~/dev/intropy/tooling/intropy-templates`, branch `streaming-extractor` (already checked
> out). Keep all work on this branch; do not push. Commit in logical units with
> conventional-commit messages matching repo history (`feat(...)`, `refactor(...)`, `docs(...)`).
> Supersedes `docs/plans/streaming-extractor-harness-demote.md` — that plan optimized a batch
> template we have now decided not to keep. Delete it as part of this work; its core learning
> (a template rendered from n=1 customer protocol is ~60% wrong for the next producer) is
> carried into the Context section below.

## Context (why)

The `streaming-extractor` template scaffolds batched, multi-file deliveries: detect a complete
batch, stream each file, publish one summary event per batch. Rendered for the one real batch
customer so far, nearly every load-bearing piece was rewritten — batch identity, readiness
marker, completeness rule, file→entity mapping, summary shape are producer protocol, not
template material.

Meanwhile the case that will actually be common is different: **independent files too large to
ride the message broker**, each streamed record-by-record in constant memory. Large files
cannot use Dapr bindings at all — bindings buffer the payload through the sidecar, defeating
streaming — so both ends of the pipeline must go through native storage SDKs (Azure Blob SDK
etc.) behind the framework's keyed `IFileStreamAdapter` (`OpenReadStreamAsync` /
`OpenWriteStreamAsync`). The event cannot carry the payload; it carries a **claim-check
reference** (output location + counts) that the consumer resolves by streaming from the same
storage.

That gives three extractor shapes, each justified by a transport constraint:

| Shape | Files | Event carries | Template |
| --- | --- | --- | --- |
| Sweep | small, independent | payload in `data` | `extractor` (unchanged) |
| Streamed claim-check | large, independent | reference to streamed output | `streaming-extractor` (this plan) |
| Batch | sets with producer protocol | summary per batch | none — hand-composed (see below) |

Batch is demoted to a hand-built composition for the known customer. This is safe because the
expensive part is already promoted to the framework: the `Batch` block (`BatchPipeline`, step
base classes) is pinned at `0.3.0-local.2`. If more batch producers appear, template the shape
then — with several real detect contracts to generalize from, not one.

**Target skeleton shape:** sweep inbound prefix → match each file to a registered
`IStreamedExtractor` (entity mapping stays producer-owned) → stream records through
`Process/Streamed/` steps → write transformed `.jsonl` to the output prefix → publish one
claim-check CloudEvent per file (reference + record/reject counts) → archive or delete the
source → exit. Same run-to-completion hosting as today.

**Consumer contract:** the claim-check reference makes the file-adapter kind and prefix layout
part of the *system contract*, on equal footing with `topic` and `contract`. The consuming
loader resolves the reference by streaming from the same adapter kind — never through a Dapr
binding (same buffering constraint). This coupling is implicit today; this plan makes it
explicit in both templates' docs and in what gets recorded in `.intropy/scaffold.json`.

## Open decisions (resolve before or during implementation — listed in plan order)

1. **Shared-contracts shape.** The `batch` mode dies with the batch template. What replaces it:
   - Option A (recommended): a new `mode: reference` — the contract record is the claim-check
     envelope (output location, entity, valid/invalid counts). Keeps the `mode` mechanism.
   - Option B: drop `mode` entirely; the reference record ships as the default skeleton and
     the sweep record becomes the conditional. Simpler manifest, but `extractor` also depends
     on `shared-contracts` and expects the sweep shape — so `mode` likely stays with
     `[sweep, reference]`.
   Either way: remove `mode: batch`, the `spec.files` rule keyed on it, and the batch branch
   of `skeleton/{{ .contract }}.cs.tmpl`. `streaming-extractor`'s dependency values pass the
   new mode.
2. **Batch disposition.** Delete `src/Batch/`, `src/Model/{{ .contract }}Batch.cs.tmpl`, and
   the batch unit-test folder outright (git history keeps them). Do not keep an
   `empty=false`-style batch sample "for reference" — that is exactly the misleading-sample
   failure mode the demote plan diagnosed.
3. **Reference record fields.** Minimum: output file name/uri, entity type, valid count,
   invalid count. Decide whether it also carries the source file name (traceability) and
   whether location is a plain string (matches today's `OutputFiles` map) or a small record
   (adapter-key + path) to make the consumer-side coupling explicit in the type system.
4. **Source disposition on success.** The sweep shape deletes on success (`extractor`
   precedent); the batch shape archives. For claim-check, archive-to-processed-prefix is the
   safer default (the source is the only full-fidelity copy until the consumer confirms); make
   it a fixed behavior with the prefix in `IngestOptions`, not a parameter.
5. **Zero-valid-records and streamed failure.** Per file: omit the event entirely (today's
   sub-pipeline behavior for zero valid) or publish a reference with `valid: 0`? Failure of
   one file must not fail the sweep (differs from batch, where partial batches were never
   published). Recommend: publish only when valid > 0; a streamed business/technical failure
   logs + raises the incident path and continues to the next file; the run's exit code still
   follows the 0/1/2 contract.
6. **Loader impact (scope boundary).** The `loader` template writes payloads through a folder
   binding and reads the event `data`. A claim-check consumer is a *different loader shape*
   (resolve reference → stream input → load). Decide: this plan only documents the contract
   on the extractor side (loader rework as a follow-up plan), or includes a minimal loader
   variant. Recommend follow-up — one plan, one template.
7. **Old customer render.** The rendered batch component (Fluxia `OrderBatchExtractor`) is
   customer code, not this repo. Nothing to migrate here; note in the commit message that the
   batch scaffold is retired and the framework `Batch` block remains supported.

## Changes

### 1. Delete the batch scaffold

- Remove from `streaming-extractor/skeleton/`:
  - `src/Batch/**` (`BatchJob`, `DetectStep`, `SubPipelineStep`, `SerializeSummaryStep`,
    `ArchiveStep`, `{{ .contract }}BatchContext`)
  - `src/Model/{{ .contract }}Batch.cs.tmpl`
  - `test/{{ .name }}.Test.Unit/Batch/**`
  - `test/{{ .name }}.Test.Integration/BatchRunTests.cs.tmpl`
- Keep `Source{{ .contract }}Record.cs.tmpl` and `{{ .contract }}Record.cs.tmpl` (the streamed
  record shapes are producer-owned sample content, same as before).

### 2. Add the streamed sweep

- New `src/Sweep.cs.tmpl`, modeled on `extractor/skeleton/src/Sweep.cs.tmpl` but per file:
  resolve the `IStreamedExtractor` by entity type (unmatched file → incident + continue),
  build the `StreamSource` (open input from the keyed source adapter, open output to
  `<output-prefix>/<file>.jsonl`), execute, and hand the `StreamedContext` to the publish
  step. Output naming rule (`.jsonl`, source base name preserved) comes from today's
  `SubPipelineStep` — it is not producer protocol.
- New `src/Publish/ReferencePublishStep.cs.tmpl` (name TBD): builds the claim-check CloudEvent
  (Id/Subject = source file identity; Data = the reference record) and sends via the existing
  DI-registered `SendStep<TCtx>` / `DaprTopicPublisher` seam. Envelope Source/Type stay with
  the publisher exactly as today.
- `Program.cs.tmpl` / `BatchJob.cs.tmpl`: replace the `BatchJob` adapter with an `ExtractJob`-
  style adapter over the sweep (mirror `extractor`'s hosting split so the integration suite
  can drive the sweep directly).
- `Composition/Composition.cs.tmpl`: drop batch-pipeline registration; register the sweep,
  the streamed extractors, the publish step. Keep the keyed source-adapter registration —
  it is the point of the template.
- `Configuration/IngestOptions.cs.tmpl`: replace batch-detection options with incoming prefix,
  ready/marker rule (if the sweep needs one — prefer none; completeness is a batch concept),
  output prefix, processed prefix.

### 3. Empty/sample split (unchanged mechanism)

- `empty: true` (default, keep): streamed step bodies throw `NotImplementedException` with
  the decision checklists — this mechanism from the demote plan is still right, now for the
  streamed steps only (deserializer envelope path, validation rules, transform mapping,
  entity-type mapping).
- `empty: false`: one entity, minimal sample whose only job is keeping the integration suite
  executable. Apply the demote plan's test-shrink target (~7 unit tests, file-level summaries
  say the suite is a wiring smoke net, not a specification).

### 4. Manifest, contracts, docs

- `streaming-extractor/template.yaml`:
  - `metadata.description`: run-to-completion extractor for files too large for the broker —
    streams each file in constant memory through native storage SDKs, publishes one
    claim-check CloudEvent per file. Drop the `batch` tag.
  - `contract` parameter description: the claim-check reference record, not a summary.
  - Dependency values: `mode: 'reference'` (per open decision 1).
- `shared-contracts/template.yaml` + skeleton: remove `batch` mode, add `reference` mode
  (record fields per open decision 3), update the `spec.files` rule and the mode description.
- `streaming-extractor/README.md`: rewrite around the claim-check shape; state the three-shape
  table (sweep / streamed claim-check / hand-composed batch); document the consumer-side
  contract (loader must stream from the same adapter kind — Dapr bindings are not an option
  for large files, buffering defeats streaming).
- Skeleton `AGENTS.md.tmpl` / `README.md.tmpl`: describe the sweep + claim-check flow, the
  fail-forward workflow first (render default, `task test:integration` is the to-do list),
  and the facts a migration agent needs (keyed adapter seam, reference record fields, output
  naming rule).
- Repo `CLAUDE.md`: no characterization change expected (templates are described generically),
  but verify.
- Delete `docs/plans/streaming-extractor-harness-demote.md` in the same commit series,
  referencing this plan as its replacement.
- Examples: keep three fixtures (`minimal`, `empty`, `migration`) — they are shape-agnostic
  values files; `migration.yaml`'s comment loses "batch".

### 5. Tests

- Unit: one small suite per new step (sweep file-matching, publish reference shape) plus the
  trimmed streamed-pipeline tests — target ~7 total, per the demote plan's smoke-net framing.
- Integration: `SweepRunTests` (replaces `BatchRunTests`) driving the sweep against
  `Intropy.Framework.Testing` fakes: `InMemoryFileAdapter` at the keyed source seam,
  `FakeTopic` at the send seam. Assert the reference event's location matches the file the
  fake adapter holds. `CompositionTests` unchanged in spirit.
- `empty=true` keeps the fail-forward integration failures as the to-do list.

## Verification (must all pass before finishing)

1. Re-render `empty=false` (minimal.yaml) with the gorender harness from the demote plan
   (`/tmp/gorender/gorender`; verify it still exists, recreate from that plan's transcript
   section if gone): `dotnet build` clean, `dotnet test` green.
2. Re-render `empty=true`: build clean, unit suite absent/empty, integration failures are
   exactly the intended fail-forward list, none from compile or wiring errors.
3. Render `migration.yaml` (dot-named component + event-identity overrides) — same green bar.
4. Render a `shared-contracts` consumer for each remaining mode (`sweep` via `extractor`'s
   fixture, `reference` via this template's) — both compile.
5. Repo-wide grep: no `Maxbo|Lovenskiold|Perfion|maxbo|lovenskiold|perfion|Varer|AXVareNr`;
   no `mode: 'batch'` / `mode=batch` left outside git history; no `Batch` references in the
   `streaming-extractor` tree.
6. Diff review: no batch-protocol content survives in the skeleton; the reference record
   carries no producer-specific fields; every `NotImplementedException` body carries a
   decision checklist.

## Commits (suggested)

1. `refactor(streaming-extractor): remove batch scaffold, add streamed claim-check sweep`
   (skeleton deletions + new sweep/publish/hosting + composition).
2. `feat(shared-contracts): replace batch mode with claim-check reference mode`
   (+ `streaming-extractor` dependency values).
3. `docs(streaming-extractor): claim-check shape, three extractor shapes, consumer contract`
   (READMEs, AGENTS.md, examples comments; delete superseded demote plan).
4. `test(streaming-extractor): sweep + reference smoke net`.
Run the full verification after each commit; do not push.

## Out of scope

- The framework (`Intropy.Framework.*`) — the `Batch` block stays promoted and supported for
  hand-composed batch components; the `Streaming`/`Streamed` blocks are unchanged.
- The `extractor` (sweep) and `loader` templates' current shapes (loader claim-check variant
  is a follow-up plan, per open decision 6).
- Deployment templates (`deploy-host`, `deploy-component`).
- The known customer's rendered batch component — customer code, not this repo.
