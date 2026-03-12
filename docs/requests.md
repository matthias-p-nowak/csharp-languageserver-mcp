# Requests

## Accepted requirements
- RQ-001: The project must be an MCP server implemented in C#/.NET.
- RQ-002: The server must use Roslyn-based analysis to provide insight into this repository's C# codebase.
- RQ-003: Project development should be driven by using this server on this repository and capturing discovered functionality gaps. Gaps are staged in `tmp/dogfood.md`, triaged, then added to `docs/issues.md` as `[dogfood]`-tagged issues in the shared queue.
- RQ-004: Retrieval of C# code for this project should use this MCP tool whenever feasible.
- RQ-005: The initial MCP insight tool must be `roslyn_syntax_summary` with repository-relative C# file input and declaration-count output.
