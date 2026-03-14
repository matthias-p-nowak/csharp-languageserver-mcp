# Tool: `set_root`

## Input
Absolute path to the workspace root.

## Output
Empty result `{}` on success.

## Behavior
- Validates that the path is within one of the server's allowed directories.
- Stores the root for the session; subsequent tool calls resolve paths relative to it.
- Replaces any previously set root.

## Error behavior
- Return a tool error when the path is missing or empty.
- Return a tool error when the path is not within any allowed directory.
