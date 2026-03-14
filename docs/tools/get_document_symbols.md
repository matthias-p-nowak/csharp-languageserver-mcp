# Tool: `get_document_symbols`

## Purpose
Return all named symbols in a single `.cs` file with their kind, name, and line number. Useful for file outlines and navigation. Syntax-only — no compilation required.

## Input

| Argument | Type | Required | Description |
|----------|------|----------|-------------|
| `path` | string | yes | Repository-relative path to a `.cs` file. |

## Output

```json
{
  "symbols": [
    {
      "name": "MyClass",
      "kind": "Class",
      "line": 5
    },
    {
      "name": "DoThing",
      "kind": "Method",
      "line": 12
    }
  ]
}
```

Symbols are returned in source order. `kind` is one of: `Namespace`, `Class`, `Record`, `Struct`, `Interface`, `Enum`, `Method`, `Property`, `Field`, `EnumMember`.

## Error conditions
- `path` argument missing or not a `.cs` file → error
- Path outside repository root or allowed directories → error
- File not found → error
