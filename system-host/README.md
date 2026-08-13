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

A transactional integration's internal pub/sub hop — the receive side
publishes each source file for the send side to process — is component-owned:
the topic lives in the component's own constants and is deliberately invisible
to the topology, so `Topics.cs` and the payload's `topics` list never mention
it. The host declares only the two connectors.

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
| `components` | list of `{appId, kind, …}` | `kind` is `extractor`, `loader`, or `transactional-integration`. The wiring fields follow the component's shape: a topic block carries `topicField` plus `connectorField` when it has a connector (empty when it has none); a transactional integration — connector-to-connector, no topic — carries `fromField`/`toField`. All are pre-joined `Topics`/`Connectors` identifiers. |
| `sharedContracts` | `{name, include}` — always emitted, possibly empty | `name` is the contracts project/namespace (the `using` in `Topics.cs`); `include` is the slash-separated `ProjectReference` path from the host's output directory to the contracts csproj. A topics-free system (e.g. only transactional integrations) has no shared library: the CLI emits an empty object and the render omits the `using`, the `ProjectReference`, and the contracts paragraphs. The key is always present (never omitted) because the renderer runs `missingkey=error` — an absent key fails even an `{{ if }}` guard, an empty object evaluates false. |

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

Do not lower the pin below 0.4.1. From that version the topology carries only
minted facts (connector identity is the name alone, no declared transport;
activation cadence lives in deployment configuration), the transactional
integration builder exists (`From`/`To` connectors, enforced by a
completeness rule), and the Aspire host treats run-to-completion kinds'
finished sidecars as their intended terminal state. Earlier versions cannot
compile or run this skeleton's declarations.
