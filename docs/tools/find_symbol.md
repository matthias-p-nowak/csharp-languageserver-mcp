# Tool: `find_symbol`

## Purpose
Search for a named symbol across all `.cs` files under a directory. Returns file, line, and kind for each match. Syntax-only — no compilation required.

## Input

| Argument | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `name` | string | yes | — | Symbol name to search for (exact match, case-sensitive). |
| `directory` | string | yes | — | Repository-relative directory to scan (recursive). |

## Output

```json
{
  "matches": [
    {
      "file": "src/Foo/Bar.cs",
      "line": 12,
      "kind": "Class"
    }
  ]
}
```

`kind` is one of: `Namespace`, `Class`, `Record`, `Struct`, `Interface`, `Enum`, `Method`, `Property`, `Field`, `EnumMember`.

## Error conditions
- `name` or `directory` argument missing → error
- Directory outside repository root or allowed directories → error
- Directory not found → error
