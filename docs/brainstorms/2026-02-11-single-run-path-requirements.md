# Single run path: remove component-level run

**Date:** 2026-02-11
**Status:** Agreed in brainstorm; ready for planning

## Problem

Every component template (extractor, loader, transactional) ships its own
local-run infrastructure — a docker-compose stack (grafana/otel-lgtm,
Idempotency Service, Business Incident Service), hand-written
`local/dapr-components/*.yaml`, and Taskfile `run` / `publish` /
`services:up` tasks — while the system-host template runs the same system
under Aspire with the topology as the typed source of truth.

Two consequences, in priority order:

1. **Developer confusion (primary pain).** Two ways to run the same thing.
   New developers can't tell which is canonical; every README, AGENTS.md, and
   piece of guidance must explain both paths and when each applies.
2. **Template maintenance burden.** ~15 infra files per component skeleton,
   duplicated across three component templates, that must be kept in sync
   with each other, with the framework, and with the system host.

There is also a latent drift hazard: the component's hand-written
`local/dapr-components/*.yaml` is a third representation of wiring that the
topology (system host) already owns. It is the representation most likely to
rot, since nothing generates or validates it.

## Decision

**The system host is the only run vehicle — even for a system of one
component.** Component-level `task run` is removed. Component skeletons ship
zero local-run infrastructure. The component-level feedback loop is tests,
not a running sidecar.

## Scope

### In scope

Per component skeleton (extractor, loader, transactional):

- **Delete:**
  - `docker-compose.yml.tmpl`
  - `local/platform-services/` (compose + platform-service YAML)
  - `local/dapr-components/`
  - `local/sample-data/` (or relocate into `test/` if the smoke test or
    fixtures reuse it — planning decides)
  - Taskfile tasks: `run`, `publish`, `services:up`, `services:down`
  - `.gitignore` / `.dockerignore` entries that existed only for the stack
- **Keep:** `Taskfile.yml` with `build` and `test` only; `Dockerfile`;
  `src/`; `test/`; `AGENTS.md`; `README.md`.
- **Add:** one in-process smoke test per component (see Test strategy).
- **Rewrite:** component `AGENTS.md` and `README.md` — the run section now
  states that the component runs via its system host and `task test` is the
  component-level feedback loop. No `dapr run` command survives anywhere in
  component docs.
- **Repo docs:** the CLAUDE.md "one canonical run path" authoring rule is
  updated: components no longer have a run path; the system host is the run
  path for the system, and component AGENTS.md must say so instead of
  carrying a `dapr run` command.

### Out of scope

- `int create` auto-scaffolding a sibling system host per component (a
  possible later CLI improvement; the dependency mechanism already supports
  composing templates).
- Turning components into class libraries hosted in-process by the system
  host (changes the deployment model; a separate product decision).
- Changing the system-host template itself beyond docs touch-ups.
- The manual trigger story (today: `task publish` with sample data) gets a
  home — planning must decide where it lives (Aspire dashboard affordance,
  a system-host-level publish task, or documented `dapr publish` against the
  running system). It must not silently disappear.

## Test strategy

Component-level testing, after infra removal, is a two-layer contract:

1. **Adapter-fake tests (status quo, kept).** Pipeline steps depend on
   framework adapter interfaces (`IFileAdapter`, etc.); tests substitute at
   that seam with NSubstitute. No containers, no Docker requirement for
   `task test`.
2. **One boot smoke test (new).** A `WebApplicationFactory`-style test that
   boots the component's DI graph in-process and asserts its Dapr surface —
   e.g. `/dapr/subscribe` responds for loader/transactional. Catches wiring
   rot (framework builder renames, DI registration breaks, component-name
   mismatches) in milliseconds, before the system host is involved.

**Deliberately rejected:** Testcontainers-based integration tests in
component templates. They would resurrect the deleted stack as test infra
(daprd + platform-service images + component YAML), keep the maintenance
burden alive under `test/`, and make `task test` require Docker. Fidelity
testing against a real sidecar is a **framework responsibility**: the
framework repo owns Testcontainers tests proving its builders and DI wire
correctly against real daprd — components test only what they own.

**Planning must verify:** the framework's builders allow the DI graph to
boot with no sidecar present (Dapr client construction must be lazy). If
not, the framework changes first.

## Success criteria

- A scaffolded component directory contains no compose file, no
  dapr-components YAML, and no run/publish Taskfile tasks.
- `task test` passes on a fresh scaffold with no Docker daemon running.
- The smoke test fails when the component's DI/subscription wiring is
  broken (verified by deliberately breaking it during implementation).
- A new developer reading a component's AGENTS.md finds exactly one answer
  to "how do I run this": via the system host.
- Repo CLAUDE.md authoring rules match the new skeleton reality.

## Risks / open questions for planning

- **Inner loop for sidecar-dependent behavior.** Accepted trade-off: real
  pub/sub delivery against the component you're editing requires a system
  host. Mitigated by the smoke test and by framework-level fidelity tests.
- **Manual trigger relocation** (see Out of scope) — needs an explicit home.
- **Sample data** may still be wanted by tests or by the system host's
  trigger story; decide its home during planning.
- **Migration story for existing scaffolded projects:** none provided —
  templates are content, not a migration tool. Existing projects keep their
  local stacks; only new scaffolds change. State this in release notes.
