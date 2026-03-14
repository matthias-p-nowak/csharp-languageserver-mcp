# Tool: get_members

## Purpose
Return all members (fields, properties, methods, constructors, events) of a named type across all `.cs` files under a directory. Faster than `get_document_symbols` when the type name is known but its file location is not. Syntax-only.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | string | yes | Exact type name to look up (case-sensitive). |
| `directory` | string | yes | Repository-relative directory to scan. |

## Returns
```json
{
  "members": [
    {
      "file": "src/Foo/Bar.cs",
      "line": 12,
      "typeName": "Bar",
      "memberKind": "Method",
      "name": "DoWork",
      "signature": "public void DoWork(int x)"
    }
  ]
}
```

- `memberKind` is one of: `"Field"`, `"Property"`, `"Method"`, `"Constructor"`, `"Event"`.
- When multiple files declare a type with the same name, all matches are returned.
- `signature` is the text before the first `{`, `=>`, or `;`, with internal whitespace collapsed to a single space.

## Errors
- `"Argument 'name' is required."` — `name` is missing or blank.
- `"Argument 'directory' is required."` — `directory` is missing or blank.
- `"Directory not found: <directory>"` — specified directory does not exist.
- `"The provided directory must be inside the repository root."` — path traversal attempt.
