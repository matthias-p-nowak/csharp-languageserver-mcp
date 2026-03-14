# Architecture

## Implemented components
- `src/Rosalyn.Server/Program.cs`: console entrypoint that runs the server over stdio, accepts optional `--root <path>`, and appends startup/runtime diagnostics to `~/tmp/rosalyn/new-rosalyn.log` with non-fatal fallback to `/tmp/rosalyn/new-rosalyn.log` when the primary path is not writable.
- `src/Rosalyn.Server/McpServer.cs`: JSON-RPC/MCP message loop; see [docs/handshake.md](handshake.md) for supported methods and transport behavior.
- `src/Rosalyn.Server/RoslynInspector.cs`: Roslyn-backed syntax analyzer used by tools.
- `src/Rosalyn.Server/SyntaxSummary.cs`: typed output contract for declaration counts.

## Implemented tools
Tool specs live in `docs/tools/<tool-name>.md`. See the registry in `docs/design.md`.

| Tool | Spec |
|------|------|
| `roslyn_syntax_summary` | [docs/tools/roslyn_syntax_summary.md](tools/roslyn_syntax_summary.md) |

## Constraints
- Runtime target: .NET 9 console application.
- Roslyn dependency: `Microsoft.CodeAnalysis.CSharp`.
- Transport and handshake constraints: see [docs/handshake.md](handshake.md).
