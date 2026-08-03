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
| this skeleton | `Program.cs`, `<Project>.SystemHost.csproj`, `Taskfile.yml`, `Properties/launchSettings.json`, `Services.cs`, `mocks/`, `AGENTS.md`, `README.md`, `.gitignore` |
| this skeleton, as compilable placeholders that codegen overwrites | `<Project>System.cs`, `Topics.cs`, `Connectors.cs`, `<Project>Development.cs` |

Contract types are not generated: the host references the workspace's shared
contracts project (the `shared-library` scaffold, typically `Contracts/`), and
`sys create` inserts that `ProjectReference` into the rendered csproj itself —
templates never hardcode cross-project references. `Apis.cs` codegen is
deferred; today's codegen owns the placeholder files. Connectors declare only
their deployed transport shape (`Transport.Sftp()` — value-free; connection
values are environment-owned deployment configuration). The local-run picture
lives in `<Project>Development.cs` (`IDevelopmentDefinition`): OpenAPI-backed
mocks for the platform services (`Services.cs` + `mocks/`, served by Microcks)
and a file resolution per connector (a drop folder under `test/`), so a fully
assembled system runs end-to-end with zero external configuration. Every
connector the topology uses must have such a resolution — `check` fails
otherwise.

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
`pascalCase` derivation: codegen overwrites `<projectName>System.cs` and
`<projectName>Development.cs`, and a disagreement would add a second
`ISystemDefinition` / `IDevelopmentDefinition` beside the placeholder instead
of replacing it (discovery then fails at runtime).

## Rendering locally

```bash
intropy int create system-host -o /tmp/system-host-out \
  -f system-host/examples/minimal.yaml --version main --no-input
```

Building the render requires the `Intropy.Topology.Aspire` /
`Intropy.Topology.Generation` packages (0.3.0) to be resolvable from a NuGet
feed; until they are published, pack the intropy-topology repo to a local
folder feed:

```bash
cd ~/dev/intropy/tooling/intropy-topology
dotnet pack -p:Version=0.3.0 -o ~/dev/intropy/local-nuget
```

Do not lower the pin below 0.3.0. That is the first version with the
deployed-transport connector API and development definitions
(`Transport.Sftp`, `IDevelopmentDefinition`); earlier versions cannot compile
this skeleton's placeholders.
