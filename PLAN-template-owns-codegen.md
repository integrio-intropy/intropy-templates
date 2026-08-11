# Plan: move system-assembly codegen from the CLI into the template library

Audience: the agent implementing this change. Read the referenced files
before writing code; they are the ground truth where this plan and the code
disagree.

## Goal

`intropy sys create` today renders the `system-host` template shell and then
**the CLI itself** writes all C# declaration files, edits the csproj, and
creates connector test folders. The CLI thereby encodes the Intropy.Topology
API surface, csproj XML, and three branches of "template release era"
detection. Every Intropy.Topology API change currently forces a CLI release.

After this change:

- The **template library** owns every byte of C# and .NET project content:
  `Topics.cs`, `Connectors.cs`, `<ProjectName>Development.cs`,
  `<SystemClass>.cs`, the csproj `<ProjectReference>` to the contracts
  project, and the default extractor schedule.
- The **CLI** owns only workspace knowledge: scanning scaffold records,
  topology validation (duplicates, conflicts, exactly-one-shared-library),
  path computation, and assembling a structured value payload passed to one
  `template.Create` call.
- Template version = Intropy.Topology version = generated code shape, one
  atomic unit. All era/degrade logic is deleted.

## Compatibility stance: none

Everything here is **preview**. Keep nothing for backwards compatibility:

- Delete the era/degrade branches outright (they already only existed to
  paper over the split-brain). No deprecation window, no fallback path.
- Old CLI + new template and new CLI + old template are both unsupported
  combinations that must **fail fast with a remedy in the message**. There
  is no supported mixed-version matrix to design for.
- The flag surface, result JSON shape, scaffold.json schema, and payload
  contract may all change freely if the implementation lands somewhere
  better than this plan's sketches. This plan describes intent and
  constraints, not frozen shapes.
- Existing workspaces simply re-run `sys create` with a matched CLI and
  template release; there is no migration path to build.

## Explicitly out of scope

- **The .slnx solution file.** Deferred. It is the only artifact that would
  need rendering outside the host output dir (workspace root), which would
  force a new render-engine concept (`ExtraRenders`). It lands as a
  follow-up once this change has shipped. Do not add `ExtraRenders` or any
  workspace-root rendering in this work.
- Changing `int create`, component templates' rendered output, or the
  scaffold.json schema. (One optional, additive exception: see "Preserve
  slnx optionality" below.)
- Changing the flag surface, result JSON schema, or `template.Create`'s
  public contract beyond what this plan states.

## Architecture facts the implementation must respect

From `internal/system/create.go` (current flow):

1. `system.Create` scans `StartDir` for `.intropy/scaffold.json` records
   (`template.ListScaffolds`), builds a `Model` via `Assemble`
   (`internal/system/assemble.go`), then calls `template.Create` with
   `Template: "system-host"`, `SetValues: {"name": kebab}`, `NoInput: true`.
2. It reads `projectName` and `systemClass` back out of the scaffold record
   the render just wrote, so CLI/template name derivation cannot drift
   (comment in create.go: "reading them back from the record it just wrote
   makes CLI/template derivation drift impossible"). Keep this mechanism —
   in the new design the read-back serves the result summary only; no CLI
   code needs to locate the csproj anymore.
3. It then overwrites the placeholder files via `codegen.go`, inserts a
   `<ProjectReference>` into the rendered csproj via `csproj.go`, and
   `MkdirAll`s `test/<connector>` folders.

From `internal/system/codegen.go` (what moves to the template, verbatim
port candidates):

- `topicsFileTmpl` — `Topics.cs`: usings + one
  `public static readonly TopicRef<T> Field = TopicRef<T>.Define(pubsub, name);`
  per topic, with doc comments.
- `connectorsFileTmpl` — `Connectors.cs`: one
  `ConnectorRef.Define(name, Transport.Default())` per connector.
- `connectorsFileLegacyTmpl` — **delete, do not port.** Exists only for
  pre-development-definition template eras.
- `developmentFileTmpl` — `<ProjectName>Development.cs`:
  `IDevelopmentDefinition` with two platform-service mocks
  (`Services.Idempotency`, `Services.BusinessIncidents`, from static
  `mocks/*.openapi.yaml` skeleton files) and one
  `development.Files(Connectors.X).RootPath("./test/<name>")` per connector.
- `systemClassFileTmpl` — `<SystemClass>.cs`: `ISystemDefinition` with a
  fluent chain per component. Extractor:
  `AddExtractor(appId).From(Connectors.X).Publishes(Topics.Y).Uses(Services.Idempotency).Uses(Services.BusinessIncidents).WithSchedule("* * * * *")`
  (`.From` only when the component has a connector). Loader:
  `AddLoader(appId).Subscribes(Topics.Y).To(Connectors.X).Uses(...)`
  (`.To` only with a connector).
- `defaultExtractorSchedule = "* * * * *"` — becomes a template parameter
  with that default, not a Go constant.
- **Flag-spelling cleanup, same PR:** the existing warnings in codegen.go
  say `--version`; the actual flag is `--template-version`
  (cmd/intropy/sys_create.go). These warnings are deleted with the era
  logic, but any new error text written for this change must use
  `--template-version`.

From `internal/system/csproj.go`:

- `insertProjectReference` textually inserts, before `</Project>`:

  ```xml
  <ItemGroup>
      <!-- Shared contracts project, referenced by `intropy sys create`. -->
      <ProjectReference Include="<rel>/Contracts.csproj" IsAspireProjectResource="false" />
  </ItemGroup>
  ```

  `IsAspireProjectResource="false"` keeps the Aspire AppHost from treating
  the class library as an orchestrated resource. In the new design the
  csproj `.tmpl` simply contains this ItemGroup, rendered from
  `.sharedContracts.include`. The textual-insertion function is deleted.
  (Comment in the template must not reference `intropy sys create` — the
  template now owns the reference. Reword or drop.)

From `internal/template/` (the engine, already sufficient):

- `Render` (render.go) is Go `text/template` + sprig, `missingkey=error`;
  `{{ range .topics }}` over arrays-of-objects works today.
- `Resolve` (values.go) layers, in order: defaults → base → values files →
  set values → prompts → **schema validation** → **derived values**
  (`spec.values`). Consequence for template authors: derived values render
  *after* validation, so a `spec.values` expression can only reference
  schema-validated keys. Validation is santhosh-tekuri/jsonschema against
  `spec.parameters`. `map[string]any` set values pass through untouched;
  `coerceKnownFieldValue` only coerces strings, so nested arrays/objects in
  set values are never mangled. **Verification task (V1 below): confirm an
  undeclared set value or an array-valued parameter passes schema
  validation as expected, and decide the schema shape accordingly.**
  Template-authoring rule that falls out of this: every object in the
  payload schema needs `additionalProperties: true` (or fully declared
  properties) — there is no preserve-unknown-fields shorthand.
- Empty-value semantics: `isEmpty` (values.go) treats an empty slice as
  **non-empty**, so a `required` parameter with value `[]` passes. Where
  the template must reject an empty list (topics, components), the schema
  needs `minItems: 1`; `required` alone is not enough.
- `Template` manifest (manifest.go): `spec.parameters` is JSON Schema;
  `spec.values` are derived values rendered against the merged map
  (used today for `projectName`/`systemClass` derivation).
- Dependency rendering (dependencies.go) exists and is **unchanged**.

## Design

### Value payload (the new CLI ↔ template contract)

`system.Create` builds this map and passes it as `SetValues` to
`template.Create` (template name stays `system-host` unless the template
repo prefers a new directory — see Open question O1):

```yaml
name: order-flow                 # kebab-case system name (unchanged)
topics:                          # sorted by (pubsub, name); empty allowed? No — Assemble requires ≥1 component, topics follow
  - {pubsub: pubsub, name: orders, contract: Order, field: Orders}
connectors:                      # sorted by name; may be empty
  - {name: sftp-drop, field: SftpDrop}
components:
  - appId: order-extractor
    kind: extractor              # "extractor" | "loader"
    topicField: Orders           # pre-joined: the Topics.<field> this component touches
    connectorField: SftpDrop     # pre-joined; empty string when the component has no connector
sharedContracts:
  name: Contracts                # project/namespace name (from the shared-library scaffold's values.name)
  include: ../contracts/Contracts.csproj   # slash-separated, relative from host dir (existing contractsInclude logic)
extractorSchedule: "* * * * *"   # optional set value; template parameter default
```

Notes:

- The topic/connector field **joins** (today done inside
  `writeSystemClassFile`) move into payload assembly: for each component,
  resolve `Topic.Field` by its `TopicKey` and `Connector.Field` by name.
  This is topology wiring, legitimately CLI-side; it keeps the template a
  flat `range`. This *decision* is load-bearing — the YAML above is a
  sketch; field names and nesting are the implementer's to adjust as long
  as the template stays join-free.
- `projectName`/`systemClass` stay template-derived via `spec.values`
  (unchanged mechanism). The CLI still reads them back from the scaffold
  record — now only for its result summary; no files are written from
  them and nothing needs to locate the rendered csproj.
- PascalCase field derivation (`pascalIdent` in assemble.go) stays
  CLI-side. Mechanical transliteration; keeps templates readable.

### Template-library changes (repo: integrio-intropy/intropy-templates)

In the system-host template (or its successor, per O1):

1. `skeleton/Topics.cs.tmpl`, `Connectors.cs.tmpl`,
   `{{ .projectName }}Development.cs.tmpl`, `{{ .systemClass }}.cs.tmpl` —
   port the four templates from codegen.go (current-era variants only),
   re-keyed to the payload above.
2. `skeleton/{{ .projectName }}.SystemHost.csproj.tmpl` — add the
   ProjectReference ItemGroup rendered from `.sharedContracts.include`.
3. `template.yaml`:
   - Declare new parameters (`topics`, `connectors`, `components`,
     `sharedContracts`) with JSON Schema types (arrays of objects / object).
     These are supplied programmatically, never via `--set` or prompts:
     give `connectors` a default of `[]`; for `topics`/`components`/
     `sharedContracts` see O2 — preferred is declaring them so that
     validation fails loudly when a too-old CLI calls this template.
   - `spec.values` gains nothing; `projectName`/`systemClass` derivations
     unchanged.
   - Add `extractorSchedule` parameter, `type: string`,
     `default: "* * * * *"`, used by the system-class template.
   - Add `spec.minCLI` (new optional manifest field — see CLI change 4),
     set to the CLI version that ships this plan.
4. The `test/<connector>` empty folders: **first investigate** whether
   `development.Files(...).RootPath(...)` in Intropy.Topology creates the
   directory at runtime (or can be made to). If yes, the CLI's `MkdirAll`
   is deleted with no replacement — document that in the template's
   README/description. If no **or the investigation is inconclusive**
   (default to keeping the loop when in doubt — do not stall on this),
   keep a minimal CLI-side creation loop (`internal/system`, not the
   template engine) and file a follow-up issue against the framework.
   Do **not** add empty-dir support to the render engine for this.

### CLI changes (this repo)

1. **`internal/system/payload.go` (new)** — builds the `map[string]any`
   above from `*Model` + `CreateOptions`. Includes the field joins and
   `contractsInclude` (moved here from csproj.go).
2. **`internal/system/create.go`** — after `Assemble`:
   build payload → single `template.Create` call (payload as `SetValues`,
   `NoInput: true`) → read back `projectName`/`systemClass` from the
   scaffold record → result summary. Delete: the four `write*File`
   calls, `insertProjectReference` call, connector-folder `MkdirAll`
   (per template change 4), and the legacy/era plumbing
   (`withDevelopment`/`withConnectors`).
   **Result-JSON decision (settled):** `CreateResult.Values` echoes
   `record.Values`, which now contains the full topics/connectors/
   components payload. That expansion is accepted — preview stage,
   additive, and the payload is the honest record of what was rendered.
   `Summary` keeps its existing fields; consumers get redundancy, not
   breakage.
3. **Delete `internal/system/codegen.go` and `internal/system/csproj.go`.**
4. **Template version gate (replaces era logic).** Sub-decisions, all
   settled:
   - Manifest: add optional `spec.minCLI` (string) to `Spec` + `rawSpec`
     in `internal/template/manifest.go`. Parsing alone is decoration —
     enforcement lives in the next bullet.
   - Enforcement point: pre-render, inside the `template.Create` flow,
     **after manifest load and before resolve, render, dependency
     processing, and the scaffold-record write**. Implement as an
     optional hook on `template.CreateOptions`
     (e.g. `OnManifest func(*Template) error`); `system.Create` installs
     the check, other callers are unaffected.
   - Version source and comparison: the CLI version is the build-time
     `version` var in `cmd/intropy` (ldflags; `dev` when built without a
     tag). Pass it into `system.CreateOptions` from the command layer —
     `internal/system` must not import main-package state. Compare with
     **Masterminds/semver/v3 (already in go.mod)**.
   - **Dev-build carve-out (required):** a non-semver version (`dev`,
     empty) skips the gate entirely. Without this, every local build
     fails against every template. Tests must cover the carve-out.
   - Failure message: name the remedy — the `--template-version` flag for
     pinning a newer release, or upgrading the CLI. Match the repo's
     error style (AGENTS.md: front-load what failed, remedy on a second
     line). Do not copy the old warnings' `--version` spelling.
5. **Old CLI → new template** fails via schema validation ("missing
   required parameter(s): ...") provided O2's required-parameters
   recommendation is taken. CLI-side work for that direction is nil.

   Note on the safety net being deleted: today the CLI's `os.Stat`
   placeholder checks catch template/CLI drift at render time. After this
   change, a payload/template mismatch surfaces only at `dotnet build`.
   The replacement safety net is render tests in the template repo (see
   Tests), not CLI-side file checks.
6. **`cmd/intropy/sys_create.go`** — no flag changes. Update `Long:` to
   describe the new behavior (the template now renders the full
   declaration; CLI assembles values). Obey the repo writing style
   (AGENTS.md): Long ≤ ~150 words, no history narration.

### Preserve slnx optionality (cheap, do it)

- Component templates (extractor/loader) should start recording their
  project name in scaffold values (e.g. a `projectName` derived value),
  matching the host's existing convention. One-line `spec.values` addition
  per component template. This unblocks a future slnx payload without
  re-scaffolding. Additive; old records simply lack it.
- `Assemble` does **not** collect projects yet.

### Tests

- `internal/system/create_test.go` — the fixture tarball
  (`systemHostFiles()`) grows the four declaration-file `.tmpl`s and the
  csproj ItemGroup; assertions shift from "CLI wrote correct C# text" to
  "rendered output contains the assembled declaration" (end-to-end through
  `template.Create`) plus payload-shape unit tests for the new
  `payload.go`. Delete tests covering legacy/legacy-connectors eras
  (e.g. the cases around create_test.go:386/417 that delete the
  Development placeholder).
- `internal/system/assemble_test.go` — unchanged except any moved helpers.
- `internal/template/manifest_test.go` — `minCLI` parse/optional coverage.
- Add one engine-level test confirming arrays-of-objects set values render
  and validate (V1), if none exists.
- Template repo: add render tests if that repo has a harness; if it has
  none, validate with `helm-template`-equivalent dry runs (render fixtures
  locally via the CLI's own machinery in a scratch dir) before publishing.

### Docs

- `README.md` § around lines 330–368 (`sys create` section): update the
  description of what the command generates; remove any mention of the CLI
  assembling C#; note the minimum template release. Keep the note style
  already present.
- `internal/system` package doc comment (create.go top) — rewrite: the
  package assembles the value payload; the template renders everything.

## Verification tasks (do these first, in order)

- **V1.** Write a scratch Go test: `Resolve` + `Render` with an
  arrays-of-objects value passed via `SetValues`, parameter declared in
  `spec.parameters` as `type: array`. Confirm validation + `range` render
  behave. Also confirm behavior of a set value **absent** from
  `spec.parameters` (passes, or fails with additionalProperties) — the
  payload keys must all be declared, or the schema must permit extras.
- **V2.** Check whether Intropy.Topology's
  `development.Files(...).RootPath(...)` (or the host runtime) creates
  missing directories. Decides the `test/<connector>` folder fate.
- **V3.** Confirm the current template release's `system-host` manifest
  shape against this repo's `LoadTemplate` (fields used here:
  labels role/block-kind, spec.values derivations).

## Open questions (resolve with the requester before or during implementation)

- **O1.** Same `system-host` template directory (new major release), or a
  new template (e.g. `system-assembly`)? Recommendation: same directory,
  new major — the host shell is the same artifact; a second template
  splits the catalog for no user benefit. If same-directory, old CLIs
  hitting the new release fail via schema validation, which is the
  intended gate.
- **O2.** Schema strictness for `topics`/`components`/`sharedContracts`:
  required (old CLI fails with "missing required parameter(s)") vs
  optional-with-empty-default (old CLI silently renders an empty system).
  Recommendation: required — loud failure beats an empty system.
- **O3.** ~~`spec.minCLI` enforcement point~~ — **settled**: pre-render
  hook in `template.CreateOptions`, per CLI change 4.

## Sequencing

1. V1–V3.
2. Template repo: new system-host release (declaration files, csproj
   ItemGroup, parameters, `extractorSchedule`, component-template
   `projectName` recording). Publish as a new tag.
3. CLI repo, one PR: payload builder, create.go rewrite, codegen.go +
   csproj.go deletion, version gate, tests, README/package-doc updates.
   PR must pin its test fixtures to the new template tag.
4. Dogfood: scaffold a workspace with the new component templates, run
   `sys create`, and diff the result against output from the previous CLI
   + previous template — identical modulo intended changes (csproj comment
   text, schedule parameterization).

## Risks

- **Coupled release.** New CLI + old template must fail clearly (gate
  above, with the dev-build carve-out); old CLI + new template fails via
  schema validation. Both failure messages must name the remedy
  (`--template-version` / upgrade intropy). Preview stage means no mixed
  matrix is supported — this replaces today's three soft-degrade branches
  with one hard gate, deliberately.
- **Schema validation of rich values** (V1) is the main engine
  uncertainty; everything else reuses shipped machinery.
- **Drift detection moves to build time.** The CLI's placeholder
  `os.Stat` checks (which caught template/CLI shape drift at render time)
  are deleted with the split-brain they compensated for. Mitigation is
  render tests in the template repo, plus the dogfood diff in Sequencing.
- **Deleted era support:** workspaces pinned to old template releases via
  `--template-version` will need to upgrade the release. Acceptable; the
  error message says so.
