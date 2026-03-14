# Tool: `roslyn_complexity_report`

## Purpose
Scan all `.cs` files under a directory and return the top N methods ranked by cyclomatic complexity. Useful for identifying hotspots across a codebase.

## Input

| Argument | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `directory` | string | yes | — | Repository-relative path to the directory to scan (recursive). |
| `top_n` | integer | no | 20 | Maximum number of results to return. |

## Output

```json
{
  "results": [
    {
      "file": "src/Foo/Bar.cs",
      "namespace": "Foo",
      "type": "Bar",
      "method": "DoThing",
      "line": 42,
      "complexity": 14
    }
  ]
}
```

Results are sorted descending by `complexity`.

## Cyclomatic complexity definition
1 (base) + 1 for each branching node in the method body: `if`, `for`, `foreach`, `while`, `do`, `switch case`, `catch`, `&&`, `||`, `??`, `?:`.

## Error conditions
- `directory` argument missing → error
- Directory outside repository root or allowed directories → error
- Directory not found → error
