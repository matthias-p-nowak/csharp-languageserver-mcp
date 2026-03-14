# Tool: find_references

## Purpose
Find all usage sites of a named symbol across a project's C# source files using semantic analysis.

## Input
| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `name` | string | yes | Exact symbol name to find references for (case-sensitive). |
| `project` | string | no | Repository-relative path to a `.csproj` file. Optional when only one project exists under the session root. Required when multiple projects exist. |

## Output
`references`: array of objects with `file` (repo-relative), `line` (1-based), `context` (trimmed source line).

## Errors
- `"Missing required argument: name"` — name not provided.
- `"No projects loaded."` — `set_root` was not called or failed.
- `"Multiple projects found. Specify 'project': ..."` — project disambiguation required.
- `"Project not found: ..."` — specified project key not recognized.

## Behaviour
- Requires semantic compilation loaded by `set_root`.
- Only identifiers that resolve to a symbol via the semantic model are included (unresolved names are excluded).
