# Tool: get_usings

## Purpose
Return all using directives declared in a repository-relative `.cs` file, in source order.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | string | yes | Repository-relative path to a .cs file. |

## Returns
```json
{ "usings": ["System", "System.Collections.Generic"] }
```

- Returns an empty array when no `using` directives are declared.

## Errors
- `"Argument 'path' is required."` — path is missing or blank.
- `"Only .cs files are supported."` — path does not end in `.cs`.
- `"File not found: <path>"` — file does not exist.
- `"The provided path must be inside the repository root."` — path traversal attempt.
- `"The provided path is not within any allowed directory."` — access denied.
