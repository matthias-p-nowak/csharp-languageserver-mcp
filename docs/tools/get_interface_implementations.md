# Tool: get_interface_implementations

## Purpose
Find all classes, records, and structs that implement a named interface, across `.cs` files under a directory. Matches by simple name so `IFoo` matches `IFoo<T>` as well. Syntax-only.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | string | yes | Exact interface name — simple (unqualified) name, case-sensitive. |
| `directory` | string | yes | Repository-relative directory to scan. |

## Returns
```json
{
  "implementors": [
    {
      "file": "src/Handlers/FooHandler.cs",
      "line": 5,
      "typeName": "FooHandler",
      "typeKind": "Class"
    }
  ]
}
```

- `typeKind` is one of: `"Class"`, `"Record"`, `"Record Struct"`, `"Struct"`.
- Interface declarations are excluded from results.

## Errors
- `"Argument 'name' is required."` — `name` is missing or blank.
- `"Argument 'directory' is required."` — `directory` is missing or blank.
- `"Directory not found: <directory>"` — specified directory does not exist.
- `"The provided directory must be inside the repository root."` — path traversal attempt.
