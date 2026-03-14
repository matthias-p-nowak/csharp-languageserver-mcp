# Tool: find_test_methods

## Purpose
Return all test methods (decorated with `[Fact]`, `[Test]`, `[Theory]`, or `[TestMethod]`) across `.cs` files under a directory, with file path, line number, containing type, and method name.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `directory` | string | yes | Repository-relative directory to scan. |

## Returns
```json
{ "results": [ { "file": "...", "line": 1, "containingType": "...", "methodName": "..." } ] }
```

## Errors
- `"Missing required argument: directory"` — directory is missing or blank.
- `"Directory not found: <directory>"` — directory does not exist.
- `"The provided directory is not within any allowed directory."` — access denied.
