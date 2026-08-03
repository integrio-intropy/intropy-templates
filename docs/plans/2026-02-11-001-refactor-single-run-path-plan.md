---
title: "refactor: Single run path — remove component-level run infrastructure"
type: refactor
status: active
date: 2026-02-11
origin: docs/brainstorms/2026-02-11-single-run-path-requirements.md
deepened: 2026-02-11
---

# refactor: Single run path — remove component-level run infrastructure

## Overview

Remove all local-run infrastructure from the three component templates
(extractor, loader, transactional) so the system host is the only way to run
a component — even a system of one. Component skeletons keep only
build/test, gain one in-process boot smoke test each, and have their docs
rewritten to point at the system host. The system host gains the manual
trigger story (publish/seed tasks + sample data) that components lose.
hello-world remains a documented exception.

---

## Problem Frame

Two run paths (component `task run` with hand-written Dapr YAML + compose
stack, vs. system host under Aspire with topology-generated wiring) cause
developer confusion about which is canonical, and triple the template
maintenance burden (byte-identical `local/platform-services/` trees in three
templates, per the repo's no-shared-content rule). The hand-written
component YAML is a third representation of wiring the topology already owns
— the representation most likely to rot. (See origin:
docs/brainstorms/2026-02-11-single-run-path-requirements.md)

---

## Requirements Trace

- R1. A scaffolded component directory contains no compose file, no
  dapr-components YAML, and no run/publish/services/seed/clean Taskfile
  tasks. (origin success criteria)
- R2. `task test` passes on a fresh scaffold with no Docker daemon running.
  (origin success criteria)
- R3. Each component test project has a boot smoke test that fails when the
  component's DI/subscription wiring is broken. (origin test strategy,
  layer 2)
- R4. Component AGENTS.md/README give exactly one answer to "how do I run
  this": via the system host. No `dapr run` command survives in component
  docs. (origin scope: rewrite)
- R5. The manual trigger story (publish a message, seed a source folder)
  has a home in the system-host template — it does not silently disappear.
  (origin scope boundary, resolved in planning: system-host tasks in this
  plan)
- R6. Repo CLAUDE.md authoring rules match the new skeleton reality,
  including the documented hello-world exception. (origin scope: repo docs;
  exception resolved in planning)
---

## Scope Boundaries

- No changes to component `src/` pipeline code. Two testability-only
  touches are in scope: the Composition.cs registration move
  (extractor/transactional, U1/U3) and loader's `public partial class
  Program {}` tail (U2). Neither changes runtime behavior.
- No `int create` auto-scaffolding of a sibling system host.
- No migration story for existing scaffolded projects — templates are
  content, not a migration tool; release notes state this.
- No Testcontainers anywhere in component templates.
- hello-world keeps its standalone `dapr run` + `local/dapr-components/` as
  a documented exception (decision below).

### Deferred to Follow-Up Work

- Framework-owned Testcontainers fidelity tests for block builders/DI:
  separate repo (Intropy framework); tracking note in Risks.
- Transactional binding-name alignment (`inbound-storage`/`outbound-storage`
  in component Constants.cs vs. connector-derived names in system-host
  generation): requires a framework/topology decision on naming authority;
  see Risks. Until resolved, existing system-host behavior is unchanged by
  this plan.

---

## Context & Research

### Relevant Code and Patterns

- Component skeletons: `extractor/skeleton/`, `loader/skeleton/`,
  `transactional/skeleton/` — verified inventory in research: compose +
  `local/platform-services/` byte-identical across all three; per-template
  `local/dapr-components/` differs (extractor `inbound.yaml.tmpl`, loader
  `destination.yaml.tmpl`, transactional `inbound-/outbound-storage.yaml.tmpl`).
- Taskfiles: `*/skeleton/Taskfile.yml.tmpl` — keep `build`/`test`; delete
  `run` (loader deps `[services:up]`; extractor/transactional deps
  `[seed, services:up]`), `publish` (loader only), `seed`
  (extractor/transactional), `services:up`, `services:down`, `clean`; prune
  run-only vars (APP_PORT, DAPR_HTTP_PORT, DAPR_GRPC_PORT, LOG_LEVEL,
  INBOUND_DIR/OUTBOUND_DIR).
- Test projects: `*/skeleton/test/{{ .name }}.Test.csproj.tmpl` — identical
  today (net10.0, xunit, NSubstitute); zero coupling to `local/` (grep
  clean). Loader smoke test needs `Microsoft.AspNetCore.Mvc.Testing` added.
- Csproj asymmetry (pre-existing, out of scope but must not surprise):
  transactional's src csproj is `transactional/skeleton/src/{{ .name
  }}.csproj` — a plain file whose *filename* is templated but whose
  contents are not (extractor/loader use `.csproj.tmpl`). It contains no
  template expressions, so renders work today; U3's render verification
  treats this as intentional, not a defect to fix in this plan.
- Console composition roots: `extractor/skeleton/src/Program.cs.tmpl` and
  `transactional/skeleton/src/Program.cs.tmpl` build `ServiceCollection` in
  top-level statements then `BuildServiceProvider()` — the smoke test needs
  that collection logic callable, hence the `Composition.cs` seam (U1).
  Verified: `new DaprClientBuilder().Build()` is lazy — no sidecar needed at
  registration or provider-build time.
- Loader web root: `loader/skeleton/src/Program.cs.tmpl` —
  `WebApplication`, `MapLoaderEndpoints()`, `MapSubscribeHandler()`,
  `MapHealthChecks("/healthz")`. Needs trailing
  `public partial class Program {}` for WebApplicationFactory.
- System host: `system-host/skeleton/Taskfile.yml.tmpl` (build/run/check/
  graph/generate — no trigger tasks today), `system-host/skeleton/AGENTS.md.tmpl`
  Development notes (staged overlay bullet references component
  `local/dapr-components/` pass-through — must be revised), connector
  folders live under the host project (`development.Files(...).RootPath(...)`).
- CLAUDE.md authoring rules (all in the AGENTS.md-convention block, lines
  ~216–237): "One canonical run path", "Facts only" rootPaths/ports,
  section structure "Build and run", facts-must-match.
- Author-facing READMEs: `extractor/README.md` (lines 12–20),
  `loader/README.md` (lines 13–23) describe `task run`/stack.

### Institutional Learnings

- None — no `docs/solutions/` in this repo.

---

## Key Technical Decisions

- **Manual trigger lands in the system-host template as `publish` + `seed`
  Taskfile tasks**, with the sample order moving to
  `system-host/skeleton/sample-data/sample-order.json.tmpl`. `seed` copies
  it into the host's connector drop folder (path parameterized, default
  `./test/<connector>` per the development definition convention);
  `publish` uses `dapr publish` against the running system. This keeps
  ad-hoc E2E verification possible out of the box. (Resolves origin's
  must-not-disappear constraint.)
- **Smoke test shape is per app model, not uniform.** Loader (a real
  WebApplication) gets a WebApplicationFactory test asserting the host
  builds and `/dapr/subscribe` responds. Extractor and transactional
  (console apps, no HTTP surface) get a composition test that builds the
  ServiceProvider and resolves the runner + pipeline steps. Uniform
  *intent* ("the composition the system host will run actually builds"),
  honest per-shape mechanics. (Refines the origin's test strategy, which
  assumed `/dapr/subscribe` applied to transactional — verified in repo
  research: transactional is a console job with no HTTP surface.)
- **Console composition roots move into a static `Composition` class
  (introduced in U1, mirrored in U3).** Top-level statements are invisible
  to a test assembly; the
  ServiceCollection-building portion of extractor/transactional Program.cs
  moves to `src/Configuration/Composition.cs` (e.g.
  `Composition.ConfigureServices(IServiceCollection)`) that both Program.cs
  and the smoke test call. Smallest possible seam; no framework dependency.
- **hello-world keeps its standalone run path as a documented exception.**
  It is the first-touch teaching example and uses no framework blocks;
  CLAUDE.md states the rule ("system blocks never ship a run path") and the
  exception explicitly.
- **Docs in the same commit as deletions, per template.** The repo rule
  "facts in AGENTS.md must match the skeleton" makes split commits a lie in
  between. Each template's removal + doc rewrite + smoke test is one atomic
  unit.

---

## Open Questions

### Resolved During Planning

- Where does the manual trigger live? → System-host publish/seed tasks +
  moved sample data (user decision).
- Smoke test shape for console components? → DI-graph-build test; loader
  gets WebApplicationFactory (user decision).
- hello-world in scope? → Kept as documented exception (user decision).
- Do test projects break when `local/` is deleted? → No; verified zero
  references to `local/`, sample-data, or storage paths in all three test
  trees.
- Does DI build require a sidecar? → No; `DaprClientBuilder().Build()` is
  lazy. Runtime calls need the sidecar, provider build does not. The
  origin's "planning must verify" item is satisfied by inspection; the
  smoke tests themselves prove it at execution time.

### Deferred to Implementation

- Exact Taskfile mechanics of system-host `seed`/`publish` (vars for
  connector folder path and topic; whether publish takes a file or inline
  JSON) — depends on Task syntax details best settled while editing.
  Includes the sidecar-addressing question for publish (app-id + HTTP port
  under Aspire) — see U4's addressing caveat.
- Exact public surface of `Composition` (method name/signature) — settle
  while writing the first template, then mirror across the other two.
- Whether `sample-order.json.tmpl` content changes when moving (it is
  connector-agnostic JSON; likely a plain copy).

---

## Implementation Units

- [ ] U1. **Extractor: remove run infra, add smoke test, rewrite docs**

**Goal:** extractor skeleton ships zero run infrastructure; its test
project proves the composition builds; its docs point at the system host.

**Requirements:** R1, R2, R3, R4

**Dependencies:** None (the Composition seam pattern originates in this
unit; U3 mirrors it)

**Files:**
- Delete: `extractor/skeleton/docker-compose.yml.tmpl`,
  `extractor/skeleton/local/` (dapr-components, platform-services,
  sample-data), and the `local/storage/` entry at the end of
  `extractor/skeleton/.gitignore`; remove `local/` and
  `docker-compose.yml` from `extractor/skeleton/.dockerignore`
- Modify: `extractor/skeleton/Taskfile.yml.tmpl` (keep `build`/`test`;
  delete `seed`/`run`/`services:up`/`services:down`/`clean`; prune run-only
  vars; rewrite header comment)
- Create: `extractor/skeleton/src/Configuration/Composition.cs.tmpl`
- Modify: `extractor/skeleton/src/Program.cs.tmpl` (delegate service
  registration to Composition; keep provider build + runner invocation)
- Create: `extractor/skeleton/test/CompositionTests.cs.tmpl`
- Modify: `extractor/skeleton/AGENTS.md.tmpl`,
  `extractor/skeleton/README.md.tmpl` (Build and run → Build and test; run
  via system host; drop rootPath/local file facts; binding facts restated
  as "provided by the system host")
- Modify: `extractor/README.md` (author-facing; remove stack/run
  description)

**Approach:**
- Composition seam: everything from `var services = new ServiceCollection()`
  through `services.AddSingleton<ExtractorRunner>()` moves into
  `Composition.ConfigureServices`. Program.cs becomes: build collection via
  Composition, build provider, telemetry scope, resolve runner, run.
- Smoke test calls `Composition.ConfigureServices(new ServiceCollection())`,
  builds the provider, and resolves `ExtractorRunner` (which transitively
  proves pipeline steps, keyed `IFileAdapter`, idempotency/business-incident
  clients, and DaprClient all register).
- AGENTS.md "Build and run" becomes "Build and test": `task build`,
  `task test`; one sentence stating the component runs via its system host
  (scheduled by the host; "Run now" in the Aspire dashboard).
- The `environment` local (read from `DOTNET_ENVIRONMENT`) moves into
  Composition with the registrations it feeds — it is the only top-level
  local the registration block depends on.
- Note: extractor's Program.cs.tmpl has no `{{ if .empty }}` conditionals
  (those live in transactional's); nothing conditional moves here. The
  smoke test must still compile and pass on the empty variant render.

**Patterns to follow:**
- Existing test style: `extractor/skeleton/test/Process/TransformerTests.cs.tmpl`
  (xUnit facts, no fixture ceremony)
- CLAUDE.md AGENTS.md authoring rules (facts only; section structure)

**Test scenarios:**
- Happy path: fresh `ServiceCollection` → ConfigureServices →
  BuildServiceProvider → `GetRequiredService<ExtractorRunner>()` returns an
  instance (proves full DI graph wires without a sidecar)
- Happy path: keyed `IFileAdapter` resolves under
  `Constants.SourceFileAdapterKey`
- Integration: render the template (`intropy int create extractor
  -f extractor/examples/minimal.yaml --version main`), then `task build` and
  `task test` pass with no Docker daemon running
- Integration: deliberately break one registration (e.g. comment out
  AddProcessPipeline in the rendered output) → smoke test fails
- Edge case: render with the empty variant and confirm the smoke test still
  compiles and passes

**Verification:**
- `grep -r "dapr run\|docker-compose\|local/dapr-components" extractor/skeleton/`
  returns nothing
- Rendered scaffold has no compose file, no `local/`, and Taskfile exposes
  only build/test
- Rendered `task test` green without Docker

---

- [ ] U2. **Loader: remove run infra, add WebApplicationFactory smoke test, rewrite docs**

**Goal:** same as U1 for the loader, with the web-shaped smoke test.

**Requirements:** R1, R2, R3, R4

**Dependencies:** None (parallel with U1/U3; lands as its own commit)

**Files:**
- Delete: `loader/skeleton/docker-compose.yml.tmpl`,
  `loader/skeleton/local/`; prune `.gitignore` / `.dockerignore` entries
- Modify: `loader/skeleton/Taskfile.yml.tmpl` (keep build/test; delete
  run/publish/services:up/services:down/clean; prune vars)
- Modify: `loader/skeleton/src/Program.cs.tmpl` (add trailing
  `public partial class Program {}`)
- Modify: `loader/skeleton/test/{{ .name }}.Test.csproj.tmpl` (add
  `Microsoft.AspNetCore.Mvc.Testing` PackageReference — pin version to the
  net10.0-matching line at implementation time; the Web SDK reaches the
  test project transitively via the ProjectReference to the
  `Microsoft.NET.Sdk.Web` src project, so no extra FrameworkReference is
  needed)
- Create: `loader/skeleton/test/BootSmokeTests.cs.tmpl`
- Modify: `loader/skeleton/AGENTS.md.tmpl`,
  `loader/skeleton/README.md.tmpl`, `loader/README.md`

**Approach:**
- Smoke test: `WebApplicationFactory<Program>` → `CreateClient()` →
  `GET /dapr/subscribe` → 200 with the subscription payload containing the
  pubsub name and topic from Constants. Building the factory and receiving
  any HTTP response already prove the DI graph builds and the app serves
  HTTP without a sidecar.
- Loader needs no Composition seam — WebApplicationFactory boots Program.cs
  directly. The `partial class Program` tail is the only src change.
- AGENTS.md loses the app-port/pubsub-component/publish facts; run section
  states the component runs via its system host and messages are published
  from there.

**Patterns to follow:**
- Standard WebApplicationFactory + top-level-statements pattern
  (`public partial class Program {}` tail)
- `loader/skeleton/test/Process/SenderTests.cs.tmpl` for test style

**Test scenarios:**
- Happy path: factory builds (DI graph wires without sidecar, app starts
  serving HTTP); `GET /dapr/subscribe` → 200, payload names
  Constants.PubSubName + topic
- Error path: break a registration in rendered output (e.g. remove
  AddProcessPipeline) → factory build / endpoint test fails
- Integration: render template, `task build` + `task test` pass with no
  Docker daemon
- Edge case: render empty variant; smoke test compiles and passes

**Verification:**
- No run/publish/services tasks in rendered Taskfile; no compose/`local/`
- Rendered `task test` green without Docker; grep finds no `dapr run` in
  loader skeleton docs

---

- [ ] U3. **Transactional: remove run infra, add composition smoke test, rewrite docs**

**Goal:** same as U1 for the transactional template (console job with two
keyed file adapters).

**Requirements:** R1, R2, R3, R4

**Dependencies:** U1 (Composition seam pattern established there — mirror
it, don't reinvent)

**Files:**
- Delete: `transactional/skeleton/docker-compose.yml.tmpl`,
  `transactional/skeleton/local/`; prune `.gitignore` / `.dockerignore`
- Modify: `transactional/skeleton/Taskfile.yml.tmpl`
- Create: `transactional/skeleton/src/Configuration/Composition.cs.tmpl`
- Modify: `transactional/skeleton/src/Program.cs.tmpl` (delegate to
  Composition)
- Create: `transactional/skeleton/test/CompositionTests.cs.tmpl`
- Modify: `transactional/skeleton/AGENTS.md.tmpl`,
  `transactional/skeleton/README.md.tmpl`

**Approach:**
- Mirror U1's Composition seam; resolve `TransactionalIntegrationRunner` in
  the smoke test, plus both keyed `IFileAdapter`s
  (`SourceFileAdapterKey`/`DestinationFileAdapterKey`) — the two-binding
  registration is transactional's distinctive wiring and the thing most
  likely to rot.
- Unlike extractor, transactional's Program.cs.tmpl has `{{- if not .empty
  }}`-guarded usings and idempotency/business-incident registrations —
  those guards move into Composition.cs intact so both render variants
  compile; the smoke test must pass on the empty variant.
- AGENTS.md binding facts (`inbound-storage`/`outbound-storage` names) stay
  — they are Constants.cs facts — but the rootPath/`local/storage` phrasing
  goes; note the names must match the system host's generated bindings (see
  Risks).

**Patterns to follow:**
- U1's Composition.cs and CompositionTests.cs as implemented

**Test scenarios:**
- Happy path: resolve `TransactionalIntegrationRunner` from the built
  provider
- Happy path: both keyed `IFileAdapter`s resolve under their Constants keys
- Error path: remove `AddSendPipeline()` in rendered output → runner
  resolution fails
- Integration: render, `task build` + `task test` without Docker
- Edge case: empty variant compiles and passes

**Verification:**
- Rendered scaffold has no compose/`local/`; Taskfile exposes only
  build/test; `task test` green without Docker

---

- [ ] U4. **System host: publish/seed trigger tasks + sample data + docs**

**Goal:** the manual trigger story deleted from components gets its home in
the system host.

**Requirements:** R5

**Dependencies:** None (lands before U6, whose cross-reference sweep is the
unit that adjudicates doc agreement)

**Files:**
- Create: `system-host/skeleton/sample-data/sample-order.json.tmpl` (moved
  from extractor/transactional `local/sample-data/`)
- Modify: `system-host/skeleton/Taskfile.yml.tmpl` (add `publish` and
  `seed` tasks)
- Modify: `system-host/skeleton/AGENTS.md.tmpl` (trigger tasks in Build and
  run; revise staged-overlay Development note — components no longer ship
  `local/dapr-components/`, so generated components are the sole source)
- Modify: `system-host/skeleton/README.md.tmpl` (document triggering a
  loader via publish, an extractor via seed or dashboard "Run now")

**Approach:**
- `publish`: `dapr publish` against a running component's sidecar, payload
  from `sample-data/sample-order.json`, pubsub/topic parameterized as
  Taskfile vars (defaults matching the Topics.cs example: pubsub
  `{{ .pubsub }}`, topic `orders`). Comment states the host must be
  running.
- **Addressing caveat:** `dapr publish` needs `--publish-app-id` (a
  component app id) and the sidecar's HTTP port. Under Aspire the host
  assigns sidecar ports — whether they are pinned/deterministic is
  Intropy.Topology.Aspire behavior not verifiable from this repo. The task
  ships a `DAPR_HTTP_PORT` var (default 3500) with a comment telling the
  developer to match it to the dashboard if the host assigns differently.
  Verify against a live host at implementation time; if ports are dynamic,
  document the lookup instead of pretending the default is authoritative.
- `seed`: copies `sample-data/sample-order.json` into a connector drop
  folder (var, default `./test/<connector>` per the development-definition
  convention); comment ties it to `Files(...).RootPath(...)` in the
  development definition.
- Staged-overlay note rewrite: components ship no component YAML; the host
  generates everything into `obj/dapr-components/<component>/` from the
  declaration.

**Test scenarios:**
- Integration: render system-host; `task --list` shows publish and seed
- Integration: `task seed` copies the sample file into the configured
  folder; `task publish` fails with a clear connection error when no host
  is running (proves the command shape without needing a live system)

**Verification:**
- Rendered system host can publish a message and seed a folder without any
  component-side infrastructure

---

- [ ] U5. **Repo authoring rules: CLAUDE.md updates**

**Goal:** repo-level authoring rules match the new skeleton reality.

**Requirements:** R6

**Dependencies:** U1 (so the rule text describes reality as implemented)

**Files:**
- Modify: `CLAUDE.md`

**Approach:**
- "One canonical run path" section → rewritten: system blocks
  (extractor/loader/transactional/system blocks generally) never ship a run
  path — `task build`/`task test` are the component-level loop and
  AGENTS.md must say the component runs via its system host; the system
  host owns the run path (`task run` under Aspire).
- Document the hello-world exception explicitly: it is the intro example,
  uses no framework blocks, and intentionally keeps a standalone
  `dapr run` in AGENTS.md/README (no Taskfile).
- "Facts only" + facts-must-match sections: drop rootPath/port facts that
  came from deleted files; binding/topic name facts remain (Constants.cs).
- Section structure: component skeletons use "Build and test"; only
  system-host uses "Build and run".

**Test scenarios:**
- Test expectation: none — documentation-only unit; correctness is verified
  by reading the rendered component AGENTS.md files against the new rules

**Verification:**
- A reader following CLAUDE.md would author exactly the AGENTS.md files
  produced by U1–U4

---

- [ ] U6. **Final sweep, doc cross-check, release note**

**Goal:** adjudicate every surviving run-path reference in the repo,
confirm component and system-host docs cross-reference cleanly, and record
the migration stance. Per-template render/build/test verification already
gates U1–U4's own commits; this unit does not re-run it.

**Requirements:** R1–R6 (final adjudication)

**Dependencies:** U1, U2, U3, U4, U5

**Files:**
- Modify: template READMEs only if a stale run reference survives the
  sweep
- Modify: PR description / release notes (no repo file; authored at PR time)

**Approach:**
- Repo-wide grep sweep: `docker compose`, `dapr run`, `task run`,
  `dapr-components`, `sample-data` — every remaining hit must be either
  hello-world (documented exception), system-host (owner of the run path),
  or CLAUDE.md prose describing the new rules
- Cross-reference check: rendered component AGENTS.md's "run via the system
  host" pointer lands on instructions that exist in the rendered
  system-host README/AGENTS.md (no dangling references in either direction)
- Release note: templates are content; existing scaffolded projects keep
  their local stacks — only new scaffolds change

**Test scenarios:**
- Integration: grep sweep adjudicated — zero unexplained hits
- Integration: rendered loader + rendered system host — doc
  cross-references resolve in both directions

**Verification:**
- The sweep is clean and the release note is written; per-template success
  criteria (R1–R4) were already gated by U1–U4's own verification

---

## System-Wide Impact

- **Unchanged invariants:** component runtime behavior under the system
  host is untouched — no pipeline, endpoint, or builder code changes
  (Composition.cs is a move, not a behavior change). Dockerfile, csproj
  dependencies (aside from the loader test package), and deploy templates
  are unaffected.
- **Cross-template contract:** component Constants.cs binding/topic names
  are now the *only* component-side declaration of those names; the system
  host's generated YAML must match them. This was always implicitly true;
  deletion of the hand-written YAML makes it load-bearing.
- **Authoring-surface parity:** CLAUDE.md, template AGENTS.md.tmpl files,
  and author READMEs must change together — this repo's own rule ("when you
  change one, change the others in the same commit") applies to this plan's
  commits.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Transactional's hardcoded `inbound-storage`/`outbound-storage` names may not match what system-host generation produces from connectors — today masked by the component's pass-through YAML | Flagged to framework/topology owners as a naming-authority decision (Deferred to Follow-Up Work); U3 keeps the names visible in AGENTS.md with a note; existing host behavior unchanged by this plan |
| System host's staged overlay currently passes component `local/dapr-components/` through; deleting them changes host behavior for old components | Not this repo's runtime — but U4 updates the AGENTS.md note so new scaffolds describe generated-only overlay; mixing old components (with YAML) into new hosts still works via pass-through |
| Smoke tests may surface framework registrations that secretly need a sidecar at provider-build time | Research verified DaprClient is lazy; if implementation hits an eager registration, that is a framework bug to report, not a template workaround |
| Developers with existing scaffolded projects read new docs and delete their stacks prematurely | Release note (U6) states new-scaffolds-only; component README keeps one line acknowledging the change |
| Framework fidelity tests (origin test strategy: real-daprd tests owned by the framework repo) are outside this repo | Tracking note only — open a framework-repo issue; this plan's smoke tests cover the composition-build gap in the meantime |

---

## Documentation / Operational Notes

- All doc rewrites follow the CLAUDE.md writing style: state the invariant,
  no history ("used to ship a compose stack" — never; git keeps the
  archaeology).
- PR should note the release-notes line for existing projects (no
  migration; new scaffolds only).

---

## Sources & References

- **Origin document:** docs/brainstorms/2026-02-11-single-run-path-requirements.md
- Related code: `extractor/skeleton/`, `loader/skeleton/`,
  `transactional/skeleton/`, `system-host/skeleton/`, `CLAUDE.md`
- Framework dependency: Intropy.Topology generation (naming authority for
  bindings) — external repo
