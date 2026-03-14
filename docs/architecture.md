# Architecture

## Implemented components
- `src/Rosalyn.Server/Program.cs`: console entrypoint that runs the server over stdio; CLI args are a list of allowed absolute directory paths.
- `src/Rosalyn.Server/McpServer.cs`: JSON-RPC/MCP message loop; see [docs/handshake.md](handshake.md) for supported methods and transport behavior.
- `src/Rosalyn.Server/RoslynInspector.cs`: Roslyn-backed syntax analyzer used by tools.
- `src/Rosalyn.Server/SyntaxSummary.cs`: typed output contract for declaration counts.
- `src/Rosalyn.Server/ComplexityResult.cs`: typed output contract for per-method complexity entries.
- `src/Rosalyn.Server/SymbolMatch.cs`: typed output contract for symbol search and document symbol results.
- `src/Rosalyn.Server/ReferenceMatch.cs`: typed output contract for `find_references` usage sites.
- `src/Rosalyn.Server/SemanticDiagnostic.cs`: typed output contract for `get_semantic_diagnostics`.
- `src/Rosalyn.Server/MethodBodyResult.cs`: typed output contract for `get_method_body`.
- `src/Rosalyn.Server/MemberResult.cs`: typed output contract for `get_members`.
- `src/Rosalyn.Server/ImplementorResult.cs`: typed output contract for `get_interface_implementations`.
- `src/Rosalyn.Server/CallHierarchyNode.cs`: typed output contract for `get_call_hierarchy`.

## Implemented tools
Tool specs live in `docs/tools/<tool-name>.md`. See the registry in `docs/design.md`.

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
| `get_project_for_file` | [docs/tools/get_project_for_file.md](tools/get_project_for_file.md) |
| `get_method_body` | [docs/tools/get_method_body.md](tools/get_method_body.md) |
| `get_namespace_for_file` | [docs/tools/get_namespace_for_file.md](tools/get_namespace_for_file.md) |
| `list_source_files` | [docs/tools/list_source_files.md](tools/list_source_files.md) |
| `get_members` | [docs/tools/get_members.md](tools/get_members.md) |
| `get_interface_implementations` | [docs/tools/get_interface_implementations.md](tools/get_interface_implementations.md) |
| `get_call_hierarchy` | [docs/tools/get_call_hierarchy.md](tools/get_call_hierarchy.md) |
| `get_lines` | [docs/tools/get_lines.md](tools/get_lines.md) |

## Semantic compilation
- On `set_root`, `RoslynInspector.LoadProjects` discovers all `.csproj` files under the session root.
- BCL reference assemblies are resolved by running `dotnet --info` to find the SDK base path, then locating `packs/Microsoft.NETCore.App.Ref/<version>/ref/net*/` relative to the dotnet root.
- For each project, NuGet compile-time references are resolved from `obj/project.assets.json` (package folders × compile asset paths). BCL refs are prepended. All `.cs` files outside `obj/` in the project directory are parsed into `SyntaxTree`s.
- One `CSharpCompilation` per project is built in-memory and cached for the session lifetime.
- Semantic tools resolve a project by relative `.csproj` path (`project` argument). If omitted and exactly one project exists, it is used automatically. If multiple projects exist, the tool returns an error listing available keys.

## Constraints
- Runtime target: .NET 9 console application.
- Roslyn dependency: `Microsoft.CodeAnalysis.CSharp`.
- Transport and handshake constraints: see [docs/handshake.md](handshake.md).
