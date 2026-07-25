# system-host

The Aspire host project of an Intropy integration system: a .NET Aspire
AppHost whose one job is to hold the system's source of truth — a typed C#
declaration of which components exist and which topics connect them — and to
run, validate, or generate the system from that one declaration.

This template is normally rendered by **`intropy sys create`**, not directly:
the command scans the workspace for the sibling components' scaffold records
(`.intropy/scaffold.json`), renders this template, and then its codegen
assembles the declaration on top. Rendering it directly (as the repo's example
fixtures do) produces a valid *empty* system.

## Ownership split

The renderer and the CLI's codegen own different files:

| Owner | Files |
|---|---|
| this skeleton | `Program.cs`, `<Project>.SystemHost.csproj`, `Taskfile.yml`, `Properties/launchSettings.json`, `AGENTS.md`, `README.md`, `.gitignore` |
| this skeleton, as compilable placeholders that codegen overwrites | `<Project>System.cs`, `Topics.cs`, `Connectors.cs` |

Contract types are not generated: the host references the workspace's shared
contracts project (the `shared-library` scaffold, typically `Contracts/`), and
`sys create` inserts that `ProjectReference` into the rendered csproj itself —
templates never hardcode cross-project references. `Apis.cs` codegen is
deferred; today's codegen owns the three placeholder files. Connectors are
resolved to local file transports rooted in the host's `test/` folder (one
drop folder per connector, created by `sys create`), so a freshly assembled
system runs end-to-end with zero external configuration.

An empty system compiles, and `dotnet run -- check` reports **ITP002** ("a
system must declare at least one component") until `Define()` declares
components — for a fresh render that error is correct behavior.

## Parameters

| name | required | notes |
|---|---|---|
| `name` | yes | DNS-1123 system name, e.g. `order-flow`. Becomes `SystemName`. |

`sys create` renders with only `name` and `--no-input`, so no other parameter
may ever be required. The derived `projectName`/`systemClass` values
(`order-flow` → `OrderFlow` / `OrderFlowSystem`) must keep matching the CLI's
`pascalCase` derivation: codegen overwrites `<projectName>System.cs`, and a
disagreement would add a second `ISystemDefinition` beside the placeholder
instead of replacing it (discovery then fails at runtime).

## Rendering locally

```bash
intropy int create system-host -o /tmp/system-host-out \
  -f system-host/examples/minimal.yaml --version main --no-input
```

Building the render requires the `Intropy.Topology.Aspire` /
`Intropy.Topology.Generation` packages (0.1.0) to be resolvable from a NuGet
feed; until they are published, pack the Intropy.Topology repo to a local
folder feed.
