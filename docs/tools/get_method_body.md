# Tool: get_method_body

## Purpose
Return the full source text of every method whose name matches the given name, with file path and start/end line numbers. Syntax-only — no compilation required.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | string | yes | Exact method name to find (case-sensitive). |
| `path` | string | no | Repository-relative `.cs` file path. When provided, search is restricted to that file. |
| `directory` | string | no | Repository-relative directory to scan. Ignored when `path` is provided. Defaults to `.`. |

## Returns
```json
{
  "methods": [
    {
      "file": "src/Foo/Bar.cs",
      "startLine": 42,
      "endLine": 55,
      "text": "public void DoThing() { ... }"
    }
  ]
}
```

- Returns an empty array when no matching method is found.
- Returns multiple entries when overloads or same-named methods exist across files.

## Errors
- `"Argument 'name' is required."` — `name` is missing or blank.
- `"Only .cs files are supported."` — `path` does not end in `.cs`.
- `"File not found: <path>"` — specified file does not exist.
- `"Directory not found: <directory>"` — specified directory does not exist.
- `"The provided path must be inside the repository root."` — path traversal attempt.
