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
- DR-010 (semantic compilation): On `set_root`, auto-discover all `.csproj` files under the session root. For each, resolve `MetadataReference`s from BCL shared runtime assemblies, NuGet compile assets via `obj/project.assets.json`, and inject `obj/**/*.GlobalUsings.g.cs` for implicit usings. Parse all non-`obj/` `.cs` files to build a `CSharpCompilation` with nullable enabled. Cache all compilations keyed by `.csproj` path for the session lifetime.
- DR-011 (find_references): Expose a `find_references` MCP tool that returns all usage sites of a named symbol across a project's `.cs` files. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.
- DR-012 (get_symbol_definition): Expose a `get_symbol_definition` MCP tool that, given a file and line, returns the definition site (file + line) of the symbol at that position. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.
- DR-013 (get_semantic_diagnostics): Expose a `get_semantic_diagnostics` MCP tool that returns compiler errors and warnings for a `.cs` file or whole project. Requires semantic compilation (DR-010). Accepts optional `project` argument per RQ-008.
- DR-014 (get_project_for_file): Expose a `get_project_for_file` MCP tool that, given a repository-relative `.cs` file path, returns the relative `.csproj` path(s) whose source tree contains that file. Syntax-only — uses the cached project source file lists from DR-010. Returns all matching projects if a file appears in multiple.
- DR-015 (get_method_body): Expose a `get_method_body` MCP tool that, given a method name and optional scoping arguments (`path` for a single file or `directory` for a subtree), returns the full source text of each matching method with its file path and start/end line numbers. Syntax-only. Multiple matches are returned when overloads or same-named methods exist across files.
- DR-016 (get_namespace_for_file): Expose a `get_namespace_for_file` MCP tool that returns the namespace names declared in a single `.cs` file, in source order. Returns an empty list when no namespace is declared. Syntax-only.
- DR-017 (list_source_files): Expose a `list_source_files` MCP tool that returns repository-relative paths for all C# source files. When projects are loaded (DR-010), returns files from the selected (or auto-selected) project, excluding `obj/` trees. Falls back to a full directory scan when no projects are loaded. Accepts optional `project` argument per RQ-008.
- DR-018 (get_members): Expose a `get_members` MCP tool that, given a type name and directory, returns all members (fields, properties, methods, constructors, events) of that type across all `.cs` files under the directory, with file, line, kind, name, and signature. Syntax-only.
- DR-019 (get_interface_implementations): Expose a `get_interface_implementations` MCP tool that, given an interface name and directory, returns all classes, records, and structs whose base list contains that interface name (matched by simple name). Syntax-only.
- DR-021 (get_lines): Expose a `get_lines` MCP tool that, given a repository-relative file path, start line, and end line (1-based, inclusive), returns the requested line range as a string. No Roslyn required — pure file I/O. Useful for reading a section of a large file without fetching the whole content.
- DR-020 (get_call_hierarchy): Expose a `get_call_hierarchy` MCP tool that, given a method name, direction ("up" for callers / "down" for callees), max depth (1–5, default 2), and directory, returns a tree of `CallHierarchyNode` records. Cycle detection prevents infinite recursion. Syntax-only.
- DR-022 (find_test_methods): Expose a `find_test_methods` MCP tool that, given a directory, returns all test methods decorated with `[Fact]`, `[Test]`, `[Theory]`, or `[TestMethod]`, with file, line, containing type, and method name. Syntax-only.
- DR-023 (get_xml_doc): Expose a `get_xml_doc` MCP tool that, given a symbol name and directory, returns the XML doc comment for every declaration of that symbol found in `.cs` files under the directory. Multiple results occur for overloads and partial types. Returns empty results when the symbol has no doc comment. Syntax-only.

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
| `get_project_for_file` | [docs/tools/get_project_for_file.md](tools/get_project_for_file.md) |
| `get_method_body` | [docs/tools/get_method_body.md](tools/get_method_body.md) |
| `get_namespace_for_file` | [docs/tools/get_namespace_for_file.md](tools/get_namespace_for_file.md) |
| `list_source_files` | [docs/tools/list_source_files.md](tools/list_source_files.md) |
| `get_members` | [docs/tools/get_members.md](tools/get_members.md) |
| `get_interface_implementations` | [docs/tools/get_interface_implementations.md](tools/get_interface_implementations.md) |
| `get_call_hierarchy` | [docs/tools/get_call_hierarchy.md](tools/get_call_hierarchy.md) |
| `get_lines` | [docs/tools/get_lines.md](tools/get_lines.md) |
| `find_test_methods` | [docs/tools/find_test_methods.md](tools/find_test_methods.md) |
| `get_xml_doc` | [docs/tools/get_xml_doc.md](tools/get_xml_doc.md) |

## MCP transport behavior
See [docs/handshake.md](handshake.md) for the full transport and handshake spec.

## Acceptance boundaries
See [docs/acceptance.md](acceptance.md). Not needed during implementation.
