# system-host

The Aspire host project of an Intropy integration system: a .NET Aspire
AppHost whose one job is to hold the system's source of truth — a typed C#
declaration of which components exist and which topics connect them — and to
run, validate, or generate the system from that one declaration.

This template is normally rendered by **`intropy sys create`**, not directly:
the command scans the workspace for the sibling components' scaffold records
(`.intropy/scaffold.json`), validates them into a system model, and assembles
the **value payload** this template renders every declaration file from.
Rendering it directly requires the same payload (see `examples/minimal.yaml`);
with empty lists it produces a valid *empty* system.

This template owns every byte of generated content — the CLI owns only the
workspace knowledge that assembles the payload. A template release, its
pinned Intropy.Topology version, and the generated code shape are one atomic
unit.

## What renders

| Files | Rendered from |
|---|---|
| `Topics.cs` | `topics` — one `TopicRef<T>` field per topic |
| `Connectors.cs` | `connectors` — one `ConnectorRef` per connector (the name is the whole identity; the deployed binding type is environment-owned deployment configuration) |
| `<Project>Development.cs` | `connectors` — one `development.Files(...).RootPath("./test/<name>")` resolution per connector, plus OpenAPI-backed mocks for both platform services (the skeleton's `Services.cs` + `mocks/` exist regardless of payload) |
| `<Project>System.cs` | `components` — one `builder.Add<Kind>(...)` chain per component |
| `<Project>.SystemHost.csproj` | `sharedContracts.include` — the `ProjectReference` to the workspace's shared contracts project |
| `Program.cs`, `Taskfile.yml`, `Properties/launchSettings.json`, `Services.cs`, `mocks/`, `sample-data/`, `AGENTS.md`, `README.md`, `.gitignore` | static shell |

Contract types are not generated: the host references the workspace's shared
contracts project (the `shared-library` scaffold, typically `Contracts/`),
whose path arrives in the payload as `sharedContracts.include`.

A connector's name is its whole identity — the deployed binding's type and
connection values are environment-owned deployment configuration the topology
deliberately does not repeat. The
local-run picture lives in `<Project>Development.cs`
(`IDevelopmentDefinition`), so a fully assembled system runs end-to-end with
zero external configuration. Every connector the topology uses must have a
`Files(...).RootPath(...)` resolution — `check` fails otherwise. The drop
folders themselves are **not** created by this render or by
Intropy.Topology at runtime: `intropy sys create` creates `test/<connector>`
after rendering, and `task seed` `mkdir -p`s its target on demand. When
rendering outside `sys create`, create them by hand.

## The payload contract

`spec.parameters` declares the payload as required: an older CLI that
renders this release with only `name` fails validation loudly instead of
producing an empty system.

The payload is **pre-joined**: the CLI resolves every reference so the
skeleton stays a flat `range` over the lists — no lookups in templates.

| key | shape | notes |
|---|---|---|
| `name` | string | DNS-1123 system name; becomes `SystemName`. |
| `topics` | list of `{pubsub, name, contract, field}` | `contract` is the shared-contracts record; `field` its PascalCase `Topics` identifier. Sorted by (pubsub, name). |
| `connectors` | list of `{name, field}` | `field` is the PascalCase `Connectors` identifier. Sorted by name. |
| `components` | list of `{appId, kind, topicField, connectorField}` | `kind` is `extractor` or `loader`; `topicField`/`connectorField` are the pre-joined `Topics`/`Connectors` identifiers the component touches (`connectorField` empty when the component has no connector). |
| `sharedContracts` | `{name, include}` | `name` is the contracts project/namespace (the `using` in `Topics.cs`); `include` is the slash-separated `ProjectReference` path from the host's output directory to the contracts csproj. |

Derived `projectName`/`systemClass` (`order-flow` → `OrderFlow` /
`OrderFlowSystem`) must keep matching the CLI's `pascalCase` derivation —
the CLI reads them back from the scaffold record for its result summary.

An empty system compiles, and `dotnet run -- check` reports **ITP002** ("a
system must declare at least one component") until `Define()` declares
components — for an empty render that error is correct behavior.

## Rendering locally

```bash
intropy int create system-host -o /tmp/system-host-out \
  -f system-host/examples/minimal.yaml --version main --no-input
```

`examples/empty.yaml` covers the empty-system render. Building a render
requires the `Intropy.Topology.Aspire` / `Intropy.Topology.Generation`
packages to be resolvable from a NuGet feed; until they are published, pack
the intropy-topology repo to a local folder feed:

```bash
cd ~/dev/intropy/tooling/intropy-topology
dotnet pack -p:Version=0.3.0 -o ~/dev/intropy/local-nuget
```

Do not lower the pin below 0.4.0. From that version the topology carries only
minted facts: connector identity is the name alone (no declared transport),
and activation cadence lives in deployment configuration. Earlier versions
require a declared `Transport` on every `ConnectorRef` and cannot compile
this skeleton's declarations.
