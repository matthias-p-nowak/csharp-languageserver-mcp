# Tool: get_call_hierarchy

## Purpose
Build a call hierarchy rooted at every declaration of a named method. Direction `"down"` shows callees; direction `"up"` shows callers. Recursion is bounded by `max_depth` and protected against cycles. Syntax-only.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | string | yes | Exact method name (case-sensitive). |
| `directory` | string | yes | Repository-relative directory to scan. |
| `direction` | string | no | `"up"` for callers or `"down"` for callees. Default: `"down"`. |
| `max_depth` | integer | no | Recursion depth, clamped to 1–5. Default: 2. |

## Returns
```json
{
  "nodes": [
    {
      "file": "src/C.cs",
      "line": 5,
      "containingType": "C",
      "methodName": "Outer",
      "children": [
        {
          "file": "src/C.cs",
          "line": 8,
          "containingType": "C",
          "methodName": "Inner",
          "children": []
        }
      ]
    }
  ]
}
```

- Returns an empty array when no declarations are found for the given method name.
- Cycle detection: when the same `{containingType}.{methodName}@{file}:{line}` key is encountered again on the current path, the node is emitted with `children: []`.

## Errors
- `"Argument 'name' is required."` — `name` is missing or blank.
- `"Argument 'directory' is required."` — `directory` is missing or blank.
- `"Argument 'direction' must be 'up' or 'down'."` — invalid direction value.
- `"Directory not found: <directory>"` — specified directory does not exist.
- `"The provided directory must be inside the repository root."` — path traversal attempt.
