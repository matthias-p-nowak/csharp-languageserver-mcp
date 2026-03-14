# Tool: get_semantic_diagnostics

## Purpose
Return compiler errors and warnings for a single `.cs` file or an entire project.

## Input
| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `path` | string | no | Repository-relative `.cs` file path. Omit for project-wide diagnostics. |
| `project` | string | no | Repository-relative `.csproj` path. Optional when only one project exists. |

## Output
`diagnostics`: array of objects with `file`, `line` (1-based), `severity` (`Error`/`Warning`), `id` (e.g. `CS0103`), `message`.

## Errors
- `"No projects loaded."` — `set_root` not called or failed.
- `"File not found in project: ..."` — path not part of the resolved project.

## Behaviour
- Filters to `Warning` severity and above; informational diagnostics are excluded.
- Requires semantic compilation loaded by `set_root`.
