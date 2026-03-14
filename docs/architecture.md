# Architecture

## Implemented components
- `src/Rosalyn.Server/Program.cs`: console entrypoint that runs the server over stdio; CLI args are a list of allowed absolute directory paths.
- `src/Rosalyn.Server/McpServer.cs`: JSON-RPC/MCP message loop; see [docs/handshake.md](handshake.md) for supported methods and transport behavior.
- `src/Rosalyn.Server/RoslynInspector.cs`: Roslyn-backed syntax analyzer used by tools.
- `src/Rosalyn.Server/SyntaxSummary.cs`: typed output contract for declaration counts.
- `src/Rosalyn.Server/ComplexityResult.cs`: typed output contract for per-method complexity entries.
- `src/Rosalyn.Server/SymbolMatch.cs`: typed output contract for symbol search and document symbol results.

## Implemented tools
Tool specs live in `docs/tools/<tool-name>.md`. See the registry in `docs/design.md`.

| Tool | Spec |
|------|------|
| `set_root` | [docs/tools/set_root.md](tools/set_root.md) |
| `roslyn_syntax_summary` | [docs/tools/roslyn_syntax_summary.md](tools/roslyn_syntax_summary.md) |
| `roslyn_complexity_report` | [docs/tools/roslyn_complexity_report.md](tools/roslyn_complexity_report.md) |
| `find_symbol` | [docs/tools/find_symbol.md](tools/find_symbol.md) |
| `get_document_symbols` | [docs/tools/get_document_symbols.md](tools/get_document_symbols.md) |

## Constraints
- Runtime target: .NET 9 console application.
- Roslyn dependency: `Microsoft.CodeAnalysis.CSharp`.
- Transport and handshake constraints: see [docs/handshake.md](handshake.md).
