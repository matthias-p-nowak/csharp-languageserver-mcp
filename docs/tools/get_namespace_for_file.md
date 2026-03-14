# Tool: get_namespace_for_file

## Purpose
Return the namespace names declared in a single repository-relative `.cs` file, in source order. Returns an empty list when no namespace is declared. Syntax-only — no compilation required.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | string | yes | Repository-relative path to a `.cs` file. |

## Returns
```json
{
  "namespaces": ["Demo.Core", "Demo.Utils"]
}
```

- Returns an empty array when the file has no namespace declaration.

## Errors
- `"Argument 'path' is required."` — `path` is missing or blank.
- `"Only .cs files are supported."` — `path` does not end in `.cs`.
- `"File not found: <path>"` — specified file does not exist.
- `"The provided path must be inside the repository root."` — path traversal attempt.
- `"The provided path is not within any allowed directory."` — access denied.
