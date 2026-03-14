# Tool: get_lines

## Purpose
Return a line range from any file without loading the whole content. Useful for reading a section of a large file when the line numbers are already known (e.g. from `get_members` or `get_document_symbols`).

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | string | yes | Repository-relative file path. |
| `start_line` | integer | yes | First line to return (1-based). |
| `end_line` | integer | yes | Last line to return (1-based, inclusive). |

## Returns
```json
{ "text": "line content\nline content\n..." }
```

- Lines are joined with `\n`.
- `end_line` is silently clamped to the file's actual line count.

## Errors
- `"Argument 'path' is required."` — path is missing or blank.
- `"Argument 'start_line' must be >= 1."` — invalid start.
- `"Argument 'end_line' must be >= start_line."` — invalid range.
- `"start_line N exceeds file length (M lines)."` — start beyond EOF.
- `"File not found: <path>"` — file does not exist.
- `"The provided path must be inside the repository root."` — path traversal attempt.
