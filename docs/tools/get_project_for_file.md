# Tool: get_project_for_file

## Purpose
Return the `.csproj` project(s) that own a given `.cs` file. Useful before invoking tools that accept an optional `project` argument.

## Input
| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `path` | string | yes | Repository-relative path to the `.cs` file. |

## Output
`projects`: array of repository-relative `.csproj` paths that include the file in their source tree.

## Errors
- `"Missing required argument: path"` — path not provided.
- `"No projects loaded."` — `set_root` not called or failed.
- `"File not found in any loaded project: ..."` — file not part of any discovered project.

## Behaviour
- Syntax-only — uses the source file lists built during `set_root`.
- Returns multiple entries if the file appears in more than one project (e.g. shared source files).
