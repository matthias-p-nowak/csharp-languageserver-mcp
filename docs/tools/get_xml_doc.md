# Tool: get_xml_doc

## Purpose
Return the XML doc comment(s) for a named symbol across `.cs` files under a directory. Returns all matches — multiple results occur for overloads and partial types.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | string | yes | Exact symbol name (case-sensitive). |
| `directory` | string | yes | Repository-relative directory to scan. |

## Returns
```json
{ "results": [ { "file": "...", "line": 1, "symbolName": "...", "doc": "/// <summary>...</summary>" } ] }
```

- Returns an empty `results` array when the symbol exists but has no doc comment.

## Errors
- `"Argument 'name' is required."` — name is missing or blank.
- `"Missing required argument: directory"` — directory is missing or blank.
- `"Directory not found: <directory>"` — directory does not exist.
- `"The provided directory is not within any allowed directory."` — access denied.
