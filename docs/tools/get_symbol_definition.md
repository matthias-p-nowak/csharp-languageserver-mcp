# Tool: get_symbol_definition

## Purpose
Return the definition site (file + line + kind) of the symbol at a given file and line number.

## Input
| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `path` | string | yes | Repository-relative path to the `.cs` file. |
| `line` | integer | yes | 1-based line number of the identifier to resolve. |
| `project` | string | no | Repository-relative path to a `.csproj` file. Optional when only one project exists. |

## Output
Single object with `file` (repo-relative), `line` (1-based), `kind` (e.g. `Method`, `Class`).

## Errors
- `"Missing required argument: path"` / `"line"` — required arguments not provided.
- `"No identifier found on line N"` — no resolvable identifier on that line.
- `"Could not resolve symbol on line N"` — identifier found but not bound by the semantic model.
- `"Symbol has no source location"` — symbol is from a referenced assembly, not source.

## Behaviour
- Uses the first identifier on the given line.
- Requires semantic compilation loaded by `set_root`.
