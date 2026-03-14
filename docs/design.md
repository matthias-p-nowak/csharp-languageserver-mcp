# Design

## Product intent
Rosalyn is an MCP server built with C#/.NET that provides codebase insight using Roslyn.

## Canonical requirements
- DR-001 (runtime): Implement the server as a C#/.NET 9 application.
- DR-002 (analysis engine): Use Roslyn as the primary mechanism for code insight.
- DR-003 (dogfooding loop): Use the server on this repository during development and treat discovered gaps as tracked work. Gaps are first noted in `tmp/dogfood.md`; after triage (duplicate check and severity assessment) they are promoted to `docs/issues.md` tagged `[dogfood]` and entered into the shared priority queue.
- DR-004 (retrieval policy): Prefer this MCP tool for C# code retrieval in this project; use fallback file reads only when the MCP route is not possible.
- DR-005 (initial insight operation): Expose a `roslyn_syntax_summary` MCP tool that analyzes one C# file and returns declaration-level counts.
- DR-007 (complexity report): Expose a `roslyn_complexity_report` MCP tool that scans all `.cs` files under a directory and returns the top N methods ranked by cyclomatic complexity.
- DR-008 (symbol search): Expose a `find_symbol` MCP tool that searches for a named symbol across all `.cs` files under a directory and returns file, line, and kind for each match. Syntax-only.
- DR-009 (document symbols): Expose a `get_document_symbols` MCP tool that returns all named symbols in a single `.cs` file with their kind, name, and line number. Syntax-only.
- DR-010 (semantic compilation): On `set_root`, auto-discover all `.csproj` files under the session root. For each, glob `obj/**/*.dll` as `MetadataReference`s and parse all `.cs` files in the project directory to build a `CSharpCompilation` in-memory. Cache all compilations keyed by `.csproj` path for the session lifetime.
- DR-011 (find_references): Expose a `find_references` MCP tool that returns all usage sites of a named symbol across a project's `.cs` files. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.
- DR-012 (get_symbol_definition): Expose a `get_symbol_definition` MCP tool that, given a file and line, returns the definition site (file + line) of the symbol at that position. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.
- DR-013 (get_semantic_diagnostics): Expose a `get_semantic_diagnostics` MCP tool that returns compiler errors and warnings for a `.cs` file or whole project. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.

## Server startup
- CLI args are a list of allowed absolute directory paths; no `--root` arg.
- The server rejects file access outside these directories at all times.

## Session root
- DR-006 (session root): The client must call `set_root` before invoking any analysis tool.
- `set_root` accepts an absolute path, validates it is within an allowed directory, and stores it for the session.
- Any tool called before `set_root` returns an error: `"Root not set. Call set_root before using tools."`

## MCP tool registry
Each tool has its own spec in `docs/tools/<tool-name>.md`. Load only the relevant file when working on a specific tool.

| Tool | Spec |
|------|------|
| `set_root` | [docs/tools/set_root.md](tools/set_root.md) |
| `roslyn_syntax_summary` | [docs/tools/roslyn_syntax_summary.md](tools/roslyn_syntax_summary.md) |
| `roslyn_complexity_report` | [docs/tools/roslyn_complexity_report.md](tools/roslyn_complexity_report.md) |
| `find_symbol` | [docs/tools/find_symbol.md](tools/find_symbol.md) |
| `get_document_symbols` | [docs/tools/get_document_symbols.md](tools/get_document_symbols.md) |
| `find_references` | [docs/tools/find_references.md](tools/find_references.md) |
| `get_symbol_definition` | [docs/tools/get_symbol_definition.md](tools/get_symbol_definition.md) |
| `get_semantic_diagnostics` | [docs/tools/get_semantic_diagnostics.md](tools/get_semantic_diagnostics.md) |

## MCP transport behavior
See [docs/handshake.md](handshake.md) for the full transport and handshake spec.

## Acceptance boundaries
See [docs/acceptance.md](acceptance.md). Not needed during implementation.
