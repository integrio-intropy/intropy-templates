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
| `Messages.cs` | `messages` — one `MessageDefinition` per internal message; an empty (or absent) list renders an empty registry |
| `Ports.cs` | `ports` — one `PortRef` per port (the name is the whole identity; the deployed binding type is environment-owned deployment configuration) |
| `<Project>Development.cs` | `ports` — one `development.Files(...).RootPath("./test/<name>")` resolution per port, plus OpenAPI-backed mocks for both platform services (the skeleton's `Services.cs` + `mocks/` exist regardless of payload) |
| `<Project>System.cs` | `components` — one `builder.Add<Kind>(...)` chain per component |
| `<Project>.SystemHost.csproj` | `sharedContracts.include` — the `ProjectReference` to the workspace's shared contracts project |
| `Program.cs`, `MessageGraph.cs`, `Taskfile.yml`, `Properties/launchSettings.json`, `Services.cs`, `mocks/`, `sample-data/`, `AGENTS.md`, `README.md`, `.gitignore` | static shell |

Contract types are not generated: the host references the workspace's shared
contracts project (the `shared-library` scaffold, typically `Contracts/`),
whose path arrives in the payload as `sharedContracts.include`.

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

`Messages.cs` declares the messages the components publish by name, and the
`graph` verb emits them as the record's `messagegroups` section. It is
disjoint from `Topics.cs`, which declares the transport those messages travel
on: an externally published message has a topic here and no message entry,
because the registry owns its definition. The topology model
(`Intropy.Topology`) knows only topics, so the section is composed in the host
— `MessageGraph.cs` folds `Messages` into the JSON the generation backend
prints, and every other verb delegates untouched.

A transactional integration's internal pub/sub hop — the receive side
publishes each source file for the send side to process — is component-owned:
the topic lives in the component's own constants and is deliberately invisible
to the topology, so `Topics.cs` and the payload's `topics` list never mention
it. The host declares only the two ports.

## The payload contract

`spec.parameters` declares the payload as required — `messages` excepted:
an older CLI that renders this release with only `name` fails validation
loudly instead of producing an empty system, but one that predates message
wiring sends every other key and must still render.

The payload is **facts-only**: each component carries the raw topic/port
names it touches, and the skeleton derives the `Topics`/`Ports` field
identifiers and the joins from components to them.

| key | shape | notes |
|---|---|---|
| `name` | string | DNS-1123 system name; becomes `SystemName`. |
| `topics` | list of `{pubsub, name, contract}` | `contract` is the shared-contracts record. The skeleton derives the PascalCase `Topics` identifier. Sorted by (pubsub, name). |
| `messages` | list of `{name, type, contract?, dataschema?, publisher?}` — optional | The system's internal messagegroup: what the components declare by name. `type` equals `name` for an internal message. Externally published messages are excluded — the registry serves their definition — so a topic can have a `Publishers` marker and no message entry. Optional, so a CLI older than message wiring still renders; absent and empty both render an empty registry. |
| `ports` | list of `{name}` | The skeleton derives the PascalCase `Ports` identifier. Sorted by name. |
| `components` | list of `{appId, kind, …}` | `kind` is `extractor`, `loader`, or `transactional-integration`. The wiring fields follow the component's shape: a topic block carries `topic: {pubsub, name}` plus `port` when it has a port; a transactional integration — port-to-port, no topic — carries `fromPort`/`toPort`. All are raw names; the skeleton joins them to the `Topics`/`Ports` fields. |
| `sharedContracts` | `{name, include}` — optional | `name` is the contracts project/namespace (the `using` in `Topics.cs`); `include` is the slash-separated `ProjectReference` path from the host's output directory to the contracts csproj. A topics-free system (e.g. only transactional integrations) has no shared library: the CLI omits the key, and `hasKey` guards in the skeleton skip the `using`, the `ProjectReference`, and the contracts paragraphs. |

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

Do not lower the pin below 0.4.1. The topology carries only minted facts
(port identity is the name alone, no declared transport; activation cadence
lives in deployment configuration), the transactional integration builder
exists (`From`/`To` ports, enforced by a completeness rule), and the Aspire
host treats run-to-completion kinds' finished sidecars as their intended
terminal state. Earlier versions cannot compile or run this skeleton's
declarations.
