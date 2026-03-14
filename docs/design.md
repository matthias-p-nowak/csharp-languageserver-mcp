# Design

## Product intent
Rosalyn is an MCP server built with C#/.NET that provides codebase insight using Roslyn.

## Canonical requirements
- DR-001 (runtime): Implement the server as a C#/.NET 9 application.
- DR-002 (analysis engine): Use Roslyn as the primary mechanism for code insight.
- DR-003 (dogfooding loop): Use the server on this repository during development and treat discovered gaps as tracked work. Gaps are first noted in `tmp/dogfood.md`; after triage (duplicate check and severity assessment) they are promoted to `docs/issues.md` tagged `[dogfood]` and entered into the shared priority queue.
- DR-004 (retrieval policy): Prefer this MCP tool for C# code retrieval in this project; use fallback file reads only when the MCP route is not possible.
- DR-005 (initial insight operation): Expose a `roslyn_syntax_summary` MCP tool that analyzes one C# file and returns declaration-level counts.

## MCP tool registry
Each tool has its own spec in `docs/tools/<tool-name>.md`. Load only the relevant file when working on a specific tool.

| Tool | Spec |
|------|------|
| `roslyn_syntax_summary` | [docs/tools/roslyn_syntax_summary.md](tools/roslyn_syntax_summary.md) |

## MCP transport behavior
See [docs/handshake.md](handshake.md) for the full transport and handshake spec.

## Acceptance boundaries
See [docs/acceptance.md](acceptance.md). Not needed during implementation.
