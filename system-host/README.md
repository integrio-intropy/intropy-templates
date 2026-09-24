# system-host

The Aspire host project of an Intropy integration system: a .NET Aspire
AppHost whose one job is to hold the system's source of truth — a typed C#
declaration of which components exist and which messages connect them — and to
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
| `Messages.cs` | `messages` — one `MessageRef<T>` field per internal message, channel resolved from `topics` by name (the topic name defaults to the message name) |
| `Ports.cs` | `ports` — one `PortRef` per port (the name is the whole identity; the deployed binding type is environment-owned deployment configuration) |
| `<Project>Development.cs` | `ports` — one `development.Files(...).RootPath("./test/<name>")` resolution per port, plus OpenAPI-backed mocks for both platform services (the skeleton's `Services.cs` + `mocks/` exist regardless of payload) |
| `<Project>System.cs` | `organization` (default: the system name) — `builder.Organization(...)`, the runtime organization the framework runner reads; `components` — one `builder.Add<Kind>(...)` chain per component, wired `.Publishes(...)`/`.Subscribes(...)` through `Messages.*` |
| `<Project>.SystemHost.csproj` | `sharedContracts.include` — the `ProjectReference` to the workspace's shared contracts project |
| `Program.cs`, `Taskfile.yml`, `Properties/launchSettings.json`, `Services.cs`, `mocks/`, `sample-data/`, `AGENTS.md`, `README.md`, `.gitignore` | static shell |

Contract types are not generated: the host references the workspace's shared
contracts project (the `shared-library` scaffold, typically `Contracts/`),
whose path arrives in the payload as `sharedContracts.include`.

A component whose message carries no contract anywhere in the payload fails
the render loudly: the host cannot type a `MessageRef<T>` from nothing. That
failure is a CLI-payload deficit to fix upstream.

There is no backwards-compatibility surface: a payload that carries topics
but no `messages`, or components without scalar `publishes` / `subscribes`
keys, fails the render. Old workspaces migrate by re-scaffolding.

A port's name is its whole identity — the deployed binding's type and
connection values are environment-owned deployment configuration the topology
deliberately does not repeat. The
local-run picture lives in `<Project>Development.cs`
(`IDevelopmentDefinition`), so a fully assembled system runs end-to-end with
zero external configuration. Every port the topology uses must have a
`Files(...).RootPath(...)` resolution — `check` fails otherwise. The drop
folders themselves are **not** created by this render or by
Intropy.Topology at runtime: `intropy sys create` creates `test/<port>`
after rendering, and `task seed` `mkdir -p`s its target on demand. When
rendering outside `sys create`, create them by hand.

A transactional integration's internal pub/sub hop — the receive side
publishes each source file for the send side to process — is component-owned:
the topic lives in the component's own constants and is deliberately invisible
to the topology, so `Messages.cs` and the payload's `topics` list never mention
it. The host declares only the two ports.

## The payload contract

`spec.parameters` declares the payload as required: an older CLI that
renders this release with only `name` fails validation loudly instead of
producing an empty system.

The payload is **facts-only**: each component carries the raw message/port
names it touches, and the skeleton derives the `Messages`/`Ports` field
identifiers and the joins from components to them.

| key | shape | notes |
|---|---|---|
| `name` | string | DNS-1123 system name; becomes `SystemName`. |
| `topics` | list of `{pubsub, name, contract}` | Derived transport list retained for deploy consumers. Sorted by (pubsub, name). |
| `messages` | list of `{name, type, contract, publisher}` — optional | The message-first view, primary input for `Messages.cs`: one entry per produced message (`type` repeats the name). |
| `ports` | list of `{name}` | The skeleton derives the PascalCase `Ports` identifier. Sorted by name. |
| `components` | list of `{appId, kind, …}` | `kind` is `extractor`, `loader`, or `transactional-integration`. Extractors carry scalar `publishes`, loaders carry scalar `subscribes`, and transactional integrations carry `fromPort`/`toPort`. All are raw names; the skeleton joins them to the `Messages`/`Ports` fields. |
| `sharedContracts` | `{name, include}` — optional | `name` is the contracts project/namespace (the `using` in `Messages.cs`); `include` is the slash-separated `ProjectReference` path from the host's output directory to the contracts csproj. A message-free system (e.g. only transactional integrations) has no shared library: the CLI omits the key, and `hasKey` guards in the skeleton skip the `using`, the `ProjectReference`, and the contracts paragraphs. |

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
packages to be resolvable from the configured NuGet feeds.

The Intropy.Topology / .Aspire / .Generation pins sit at the pilot package set
(`0.0.1-pilot.*`): message-first (`MessageRef<T>`) plus the topology-owned
runtime identity the framework runner reads — `builder.Organization(...)`,
the platform-service roles in `Services.cs`, and the per-component
`*.intropy.json` the host hands each component through `INTROPY__CONFIG`.
The pilot set is not published; the workspace needs a `nuget.config` pointing
at the pilot feed (`pilot-system/pack-feed.sh`). A template release and its
pin are one atomic unit.

`intropy sys create` does not pass `organization` yet, so its renders use the
system name as the organization until it does.
