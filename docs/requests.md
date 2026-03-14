# Requests

## Accepted requirements
- RQ-001: The project must be an MCP server implemented in C#/.NET.
- RQ-002: The server must use Roslyn-based analysis to provide insight into this repository's C# codebase.
- RQ-003: Project development should be driven by using this server on this repository and capturing discovered functionality gaps. Gaps are staged in `tmp/dogfood.md`, triaged, then added to `docs/issues.md` as `[dogfood]`-tagged issues in the shared queue.
- RQ-004: Retrieval of C# code for this project should use this MCP tool whenever feasible.
- RQ-005: The initial MCP insight tool must be `roslyn_syntax_summary` with repository-relative C# file input and declaration-count output.
- RQ-006: The server must support semantic analysis tools (`find_references`, `get_symbol_definition`, `get_semantic_diagnostics`) backed by a per-project Roslyn `CSharpCompilation`.
- RQ-007: Project compilations must be built automatically on `set_root` by discovering all `.csproj` files under the session root and loading their `obj/**/*.dll` reference assemblies — no explicit `load_project` call required.
- RQ-008: Semantic tools must accept an optional `project` argument (`.csproj`-relative path). If omitted and exactly one project exists, use it. If omitted and multiple exist, return an error listing the available projects.
