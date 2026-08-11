---
title: "feat: Real, compile-clean, initially-failing tests for empty-mode extractor scaffolds"
type: feat
status: active
date: 2026-08-05
---

# feat: Real, compile-clean, initially-failing tests for empty-mode extractor scaffolds

## Overview

Today, scaffolding the `extractor` template with `empty: true` replaces every
test body with a comment or a skipped `[Fact(Skip = ...)]` placeholder — the
migration agent starts with zero executable tests. The sweep's consumption
contract and the publish wiring, however, do not depend on the step
implementations: they are pinned by `src/Sweep.cs`, the framework pipeline,
and `src/Composition/Composition.cs`, all of which are fully wired in empty
mode. This change ships the behavioral tests in every mode, written against
test-owned inputs so they compile against the empty-shell records and fail
(throwing `NotImplementedException` from the unimplemented steps) until the
steps are implemented — at which point the already-implemented subset turns
green without the tests being touched.

---

## Problem Frame

`empty: true` exists to give a migration agent a compiling skeleton with
throwing step bodies to fill in. But the empty mode currently also strips the
tests down to placeholders, even though most of the suite pins behavior that
is independent of what the steps do:

- The sweep's consumption contract (publish → delete, technical failure →
  keep + count, duplicate → cancel + delete, incident → consume without
  commit) lives in `Sweep.cs` and the framework — real in empty mode.
- The DI graph and the `DaprTopicPublisher` wiring (pubsub name, topic,
  structured-mode encoding) live in `Composition.cs` — real in empty mode.
- Only the step unit tests pin sample-business-logic behavior
  (the four validator branches, `order_id` context stamping) that has no
  meaning until the agent defines the source shape.

The user requirement: keep the failing-from-the-start signal (fair — the
steps throw) but ship compiling, executable tests so the agent gets a
red→green progression instead of a blank page.

---

## Requirements Trace

- R1. Empty-mode renders of the extractor ship real, executable tests — no
  skipped placeholders and no comment-only test classes.
- R2. The empty-mode render compiles (`task build` including test projects).
- R3. Empty-mode tests fail at runtime until the steps are implemented (the
  failure signal is the point), but must not fail for avoidable reasons like
  misconfigured fakes.
- R4. Non-empty (`empty: false`) renders remain byte-identical to today.
- R5. The tests an empty-mode render ships must turn green as the agent
  implements the steps, without being rewritten (the already-implemented
  subset turns green on its own).

---

## Scope Boundaries

- The `loader` and `transactional` templates keep their current empty-mode
  test treatment — same pattern may be applied later, deliberately separate.
- `shared-contracts` skeleton stays as-is: empty mode ships the record shell
  and drops `OrderLine.cs`. The extractor's test inputs must not require
  contract fields in empty mode (see Key Technical Decisions).
- No changes to the step bodies, `Composition.cs`, `Sweep.cs`, or the
  framework — this is a test-content-only change (plus doc/test-harness
  updates in the same template).

---

## Context & Research

### Relevant Code and Patterns

- `extractor/skeleton/test/{{ .name }}.Test.Integration/SweepTests.cs.tmpl` —
  the suite to generalize; non-empty branch is the reference implementation.
- `extractor/skeleton/test/{{ .name }}.Test.Integration/FakeEdges.cs.tmpl` —
  central fake-wiring; the empty branch needs the same
  `idempotency.NextStatus` default the non-empty `CreateSut` relies on.
- `extractor/skeleton/test/{{ .name }}.Test.Integration/PublishIntegrationTests.cs.tmpl`
  and `CompositionTests.cs.tmpl` — the latter is already unconditional and is
  the proof that real tests compile in empty mode.
- `extractor/skeleton/src/Model/Source{{ .contract }}.cs.tmpl` and
  `shared-contracts/skeleton/{{ .contract }}.cs.tmpl` — empty mode renders
  both as parameterless `public record X();`, which constrains what test
  inputs may reference.
- `extractor/skeleton/src/Constants.cs.tmpl` — `ContextKeyOrderId` exists in
  both modes, so step unit tests may reference it.
- `extractor/examples/empty.yaml` + `minimal.yaml` — the two render fixtures;
  CLAUDE.md ("Adding a new template" step 6) prescribes rendering both and
  inspecting output. `docs/plans/` did not previously exist.

### Institutional Learnings

- None — no `docs/solutions/` content applies.

---

## Key Technical Decisions

- **Keep the `if .empty` / `else` conditional structure in every touched
  test file.** The empty branches become real tests; only the unit-test
  empty branches shrink. Do not unify branches where the pinned behavior
  genuinely differs (validator-branch unit tests cannot exist before the
  agent defines the source shape).
- **Empty-mode `SweepTests` use a distinct, test-owned valid input** —
  `{"sourceRecordId":"REC-1","occurredAt":"...","lines":[...]}` — rather than
  reusing the order-shaped JSON. Rationale: in empty mode `Source{{ .contract }}`
  and `{{ .contract }}` are parameterless record shells, so every CloudEvent
  payload is `{}` regardless of input. A test-named "valid" input that is
  order-shaped would be silently wrong about what the empty shell
  deserializes. The distinct field names document that the input is
  test-owned and replaceable when the agent defines the real shape.
- **Drop exactly two empty-mode sweep tests** (duplicate-cancel and
  delete-failure-redelivery). Both require `idempotency.NextStatus =
  new StatusResponse(Action.Ignore, ...)` to take effect, which requires the
  throwing id-extractor lambda to be replaced. With the lambda throwing, both
  would fail for the wrong reason (unimplemented wiring, not the pinned
  contract) — violating R3. The other four sweep tests fail for the right
  reason: the default `Action.Proceed` path reaches the throwing deserializer.
  A comment in the file names the two deferred tests and when to add them.
- **Failing tests carry no `Skip` and no special markers.** Plain `[Fact]`s
  that fail. The intended workflow is implement → watch tests flip green, and
  a skipped test flips silently to passing without ever failing in between.
- **The empty-mode publish test fails from the start — deliberately.**
  `CloudEventSerializeStep.ExecuteAsync` invokes the component-supplied
  `subject`/`time` lambdas unconditionally (verified against
  `integrio-intropy/intropy-framework/src/Intropy.Framework.Blocks/Extractor/Steps/CloudEventSerializeStep.cs`),
  and in empty mode those lambdas throw `NotImplementedException`. So the
  publish test drives the full pipeline to the serialize step and fails there
  — pinning that the pipeline *reaches* serialization with the component's
  pubsub/topic/source/type wiring in place. The only tests green out of the
  box are `CompositionTests`; the red wall is the intended starting state.
- **Docs state the starting state plainly**: `task test` fails out of the
  box in empty mode; that is the intended red→green signal. Both
  `README.md.tmpl` (Testing strategy section) and `AGENTS.md.tmpl`
  (Development notes) say it in one sentence each — facts, not narrative.

---

## Implementation Units

- [ ] U1. **Real empty-mode `SweepTests` (4 tests) + `FakeEdges` default status**

**Goal:** Ship the sweep's consumption-contract tests in empty mode, failing
only because steps are unimplemented.

**Requirements:** R1, R2, R3, R5

**Dependencies:** None

**Files:**
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Integration/SweepTests.cs.tmpl`
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Integration/FakeEdges.cs.tmpl`

**Approach:**
- Replace the empty branch's skipped placeholder with real versions of:
  `SweepAsync_WithValidFile_PublishesAndDeletesSourceFile`,
  `SweepAsync_WithBusinessFailure_RoutesIncidentAndConsumesFileWithoutCommit`
  (no-lines input; assertion stays on `incidents.Incidents.Single()`, not on
  the incident description, which is implementation-defined),
  `SweepAsync_WhenBrokerIsDown_KeepsFileAndCountsIt`,
  `SweepAsync_WithUnreadableFile_FailsLoudlyAndNeverBlocksTheBatch`.
- Reuse the non-empty branch's structure and comments verbatim where the
  pinned contract is identical; swap `ValidOrderJson` for the test-owned
  input constant (see Key Technical Decisions) and drop order-specific
  assertions (`published.Subject == "ORD-1001"` — keep the publish/delete/
  commit counts, which are shape-independent).
- Omit the duplicate-cancel and delete-failure-redelivery tests; add a short
  comment naming them and stating they land once the idempotency extractor
  lambdas in `Composition.BuildPipeline` are implemented (the fake's
  duplicate simulation only takes effect then).
- `CreateSut` is shared between branches — keep it unconditional (it already
  is structurally; ensure the empty branch uses it).
- `FakeEdges.cs.tmpl`: set the fake idempotency client's default status to
  `Action.Proceed / Reason.NoPreviousData` in the `CreateServices` path (or
  property initializer, matching the testing library's API) so the four
  empty-mode tests reach the deserializer instead of cancelling every file at
  the idempotency step. Non-empty mode already sets this per-test via
  `CreateSut`/inline defaults — verify the added default does not change
  non-empty behavior (non-empty tests set `NextStatus` explicitly where they
  need Ignore).

**Patterns to follow:**
- The non-empty branch of the same file — comments, Arrange/Act/Assert
  rhythm, and assertion style carry over.

**Test scenarios:**
- Integration (self-verifying): render `extractor/examples/empty.yaml`,
  run `dotnet build` in the output — compiles.
- Integration: run `dotnet test` on the empty render — the four sweep tests
  fail with `NotImplementedException` surfacing from the pipeline, not with
  fake-misconfiguration errors (e.g., everything cancelling at idempotency).
- Integration: render `extractor/examples/minimal.yaml`, run `dotnet test` —
  all pre-existing tests still pass; output is byte-identical except the
  intended changes.

**Verification:**
- Empty render: `task build` green; `task test` runs the four sweep tests
  (plus composition/publish tests) and fails them for
  `NotImplementedException` reasons only.

---

- [ ] U2. **Real empty-mode `PublishIntegrationTests`**

**Goal:** Ship the publish-wiring test in empty mode; it fails from the
start at the throwing serialize lambdas, and turns green once the agent
implements the steps and the `subject`/`time` extractors.

**Requirements:** R1, R2, R3

**Dependencies:** None (independent of U1; touches a different file)

**Files:**
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Integration/PublishIntegrationTests.cs.tmpl`

**Approach:**
- Replace the empty branch's skipped placeholder with the real test. The
  body is nearly the non-empty version with two adjustments:
  - Input JSON: the test-owned `{"sourceRecordId":"REC-1",...}` constant
    (same rationale as U1).
  - Subject assertion: with empty-shell records the serialized CloudEvent
    payload is `{}`, so drop `cloudEvent.Subject == "ORD-1001"`; assert
    PubSubName, TopicName, content type, Source, and Type — all come from
    `Constants` and are real in empty mode. Add a one-line comment that the
    Subject assertion lands with the implemented contract shape.
- Keep the NSubstitute `DaprClient` capture setup identical — it is
  mode-independent.
- The test fails from the start: the serializer evaluates the throwing
  `subject` lambda (see Key Technical Decisions), which surfaces as a
  technical failure (or throw) before any publish call. A comment names this:
  the test is red until the steps and the serialize extractors are
  implemented, then proves the full publish wiring in one go.

**Patterns to follow:**
- The non-empty branch of the same file.

**Test scenarios:**
- Integration: empty render — the test executes (not skipped) and fails at
  the throwing serialize lambdas, not on wiring or fake misconfiguration.

**Verification:**
- Empty render: test executes and fails only on the unimplemented
  steps/extractors — never on wiring.

---

- [ ] U3. **Minimal executable empty-mode unit tests for the three steps**

**Goal:** Replace comment-only unit test classes with one real, initially
failing test per step.

**Requirements:** R1, R2, R3, R5

**Dependencies:** None

**Files:**
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Unit/Process/DeserializerTests.cs.tmpl`
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Unit/Process/ValidatorTests.cs.tmpl`
- Modify: `extractor/skeleton/test/{{ .name }}.Test.Unit/Process/TransformerTests.cs.tmpl`

**Approach:**
- One test per class in the empty branch:
  - `DeserializerTests`: `ExecuteAsync` with the test-owned JSON → currently
    throws; when implemented, expect a `BusinessStepResult<Source{{ .contract }}>.Success`
    and `Constants.ContextKeyOrderId` stamped in the context (the constant
    exists in both modes). Frame assertions as the contract the
    implementation must satisfy.
  - `ValidatorTests`: `ExecuteAsync` with `new Source{{ .contract }}()` →
    currently throws; when implemented, expect some
    `BusinessStepResult<Source{{ .contract }}>` (no branch assertions — rules
    are the agent's to define).
  - `TransformerTests`: `ExecuteAsync` with `new Source{{ .contract }}()` →
    currently throws; when implemented, expect a
    `TechnicalStepResult<{{ .contract }}>.Success`.
- Comment in each: the test fails until the step is implemented; the
  assertions pin the step's framework contract, and further branch tests
  land with the business rules.

**Patterns to follow:**
- The non-empty branch's per-step Arrange/Act/Assert shape, reduced to the
  framework contract only.

**Test scenarios:**
- Integration: empty render compiles; the three tests fail with
  `NotImplementedException` from the respective step.

**Verification:**
- Empty render: three unit tests execute and fail only inside the step body.

---

- [ ] U4. **Update `AGENTS.md.tmpl` and `README.md.tmpl` for the new empty-mode testing story**

**Goal:** The scaffolded project's docs state the new starting state — real
tests that fail until the steps are implemented — and name the deferred
sweep tests.

**Requirements:** R1, R3

**Dependencies:** U1, U2, U3 (docs describe final behavior)

**Files:**
- Modify: `extractor/skeleton/AGENTS.md.tmpl`
- Modify: `extractor/skeleton/README.md.tmpl`

**Approach:**
- `AGENTS.md.tmpl`, empty-branch Development notes: replace/extend the "step
  bodies are scaffolded empty" bullet with the fact that the test suite is
  real and fails out of the box by design (`task test` is red until the
  steps land); name the two sweep tests that land with the idempotency
  extractors. Facts only, per the repo's AGENTS.md authoring rules.
- `README.md.tmpl`, Testing strategy: one sentence in the empty branch (or a
  mode-independent sentence) stating the starting state; keep the
  layers/edges tables accurate for empty mode (the suite contents now barely
  differ — note the two deferred sweep tests rather than a separate table).

**Test scenarios:**
- Test expectation: none — documentation content; verified by reading both
  renders' output for accuracy against the actual suite.

**Verification:**
- Both renders' `AGENTS.md`/`README.md` describe the tests that actually
  ship in that mode; no stale "placeholder" language remains.

---

- [ ] U5. **Render-verify both fixtures and run builds/tests**

**Goal:** Execute the repo's prescribed local render verification for both
modes and record the results.

**Requirements:** R2, R3, R4

**Dependencies:** U1, U2, U3, U4

**Files:**
- Test (renders): `/tmp/extractor-empty`, `/tmp/extractor-out` (ephemeral)

**Approach:**
- Render both `extractor/examples/empty.yaml` and
  `extractor/examples/minimal.yaml` per CLAUDE.md (`intropy int create
  extractor -o /tmp/... -f ... --version main --no-input`).
- `task build` (or `dotnet build`) in both renders; `dotnet test` in both.
- Diff the minimal render against a pre-change render to prove R4.

**Test scenarios:**
- Happy path: minimal render builds and all tests pass, byte-identical
  except intended changes.
- Integration: empty render builds; test run shows executable (not skipped)
  tests failing only on `NotImplementedException` paths (steps, idempotency
  extractors, serialize extractors). Only `CompositionTests` is green out of
  the box.
- Edge case: template syntax errors surface at render time (CLI fails
  loudly), not as malformed C#.

**Verification:**
- Both renders behave as specified; no placeholder comments or skipped facts
  remain in the empty render's test tree.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| ~~Whether `CloudEventSerializeStep` evaluates the throwing lambdas~~ Resolved: it invokes them unconditionally — the empty-mode publish test fails from the start by design | No action; test comment documents the intended red start |
| The testing library's fake idempotency client already defaults to `Action.Proceed`, making the `FakeEdges` change unnecessary (or its API differs from the libs-framework variant) | Check `Intropy.Framework.Testing`'s `FakeIdempotencyServiceClient` during U1; match its actual API (`NextStatus` property as used by the current non-empty tests) |
| Empty-mode tests assert something the agent's real shape cannot satisfy (test becomes wrong, not just failing) | Assertions restricted to shape-independent contracts (counts, deletion, fake state); field-level assertions deliberately omitted in empty branches |
| Conditional usings render unused in one mode | csprojs have implicit usings and no `TreatWarningsAsErrors`; still, keep branch usings minimal and verify both renders compile warning-clean |

---

## Sources & References

- Render fixtures: `extractor/examples/empty.yaml`, `extractor/examples/minimal.yaml`
- Reference suite (non-empty branch): `extractor/skeleton/test/{{ .name }}.Test.Integration/SweepTests.cs.tmpl`
- Framework testing fakes: `Intropy.Framework.Testing` (API pinned by current skeleton usage; analogous implementation at `intropy-libs-framework/src/Intropy.Libs.Framework.Testing.Xunit/Fakes/`)
- Serialize-step lambda evaluation (verified unconditional): `integrio-intropy/intropy-framework/src/Intropy.Framework.Blocks/Extractor/Steps/CloudEventSerializeStep.cs`
- Repo conventions: `CLAUDE.md` (skeleton conventions, AGENTS.md authoring rules, local render verification)
