# shared-contracts

Scaffolds the shared contracts class library of an integration system: the
contract records (the canonical `contract` record — `Order` by convention —
plus `OrderLine`) that cross component boundaries.
Components reference it as a sibling `ProjectReference`
(`../<name>/<name>.csproj`), so the folder name, csproj name, and namespace
all come from the single `name` parameter.

This template is normally **not rendered directly**. Component templates
declare it under `spec.dependencies`:

```yaml
dependencies:
  - template: shared-contracts
    output: '{{ .contracts }}'        # a derived value, conventionally plain "Contracts"
    values:
      name: '{{ .contracts }}'
      contract: '{{ .contract }}'
      empty: '{{ .empty }}'
```

The name is plain `Contracts` by convention: the project is scoped by the system
directory it lives in, so an organization prefix would overstate its reach.

The first `intropy int create` into a system directory scaffolds it as a
sibling; every later component render finds its `.intropy/scaffold.json` and
skips it, so the library is created exactly once. The
`intropy.dev/template-role: shared-library` label is recorded in the scaffold
record so system assembly knows this project is not a block.

## Parameters

| Name       | Required | Description                                                                                            |
| ---------- | -------- | ------------------------------------------------------------------------------------------------------ |
| `name`     | yes      | PascalCase project/namespace/assembly name (dots allowed; conventionally `Contracts`).                  |
| `contract` | no       | PascalCase name of the canonical contract record (`Order` by default); component templates pass theirs down. |
| `empty`    | no       | Strip the sample contract fields (the contract record stays as an empty shell so pipeline generics compile). |

## Render (standalone)

```bash
intropy int create shared-contracts -o /tmp/shared-contracts-out \
  -f shared-contracts/examples/minimal.yaml --version main --no-input
```
