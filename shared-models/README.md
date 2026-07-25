# shared-models

Scaffolds the shared models class library of an integration system: the
contract records (`Order`, `OrderLine`) that cross component boundaries.
Components reference it as a sibling `ProjectReference`
(`../<name>/<name>.csproj`), so the folder name, csproj name, and namespace
all come from the single `name` parameter.

This template is normally **not rendered directly**. Component templates
declare it under `spec.dependencies`:

```yaml
dependencies:
  - template: shared-models
    output: '{{ .organization }}.Models'
    values:
      name: '{{ .organization }}.Models'
      empty: '{{ .empty }}'
```

The first `intropy int create` into a system directory scaffolds it as a
sibling; every later component render finds its `.intropy/scaffold.json` and
skips it, so the library is created exactly once. The
`intropy.dev/template-role: shared-library` label is recorded in the scaffold
record so system assembly knows this project is not a block.

## Parameters

| Name    | Required | Description                                                                        |
| ------- | -------- | ---------------------------------------------------------------------------------- |
| `name`  | yes      | PascalCase project/namespace/assembly name (dots allowed, e.g. `Fluxia.Models`).   |
| `empty` | no       | Strip the sample contract fields (`Order` stays as an empty shell so pipeline generics compile). |

## Render (standalone)

```bash
intropy int create shared-models -o /tmp/shared-models-out \
  -f shared-models/examples/minimal.yaml --version main --no-input
```
