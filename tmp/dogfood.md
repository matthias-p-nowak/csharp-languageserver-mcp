# Dogfood notes

## 2026-03-14 — roslyn_complexity_report on src/

Tool: `roslyn_complexity_report`, directory: `src`, top_n: 15

| Method | File | Line | Complexity |
|--------|------|------|------------|
| `CallTool` | McpServer.cs | 354 | 24 |
| `HandleRequest` | McpServer.cs | 86 | 17 |
| `ReadMessageWithFramingAsync` | McpServer.cs | 523 | 14 |
| `TryAnalyzeComplexity` | RoslynInspector.cs | 105 | 9 |
| `TryNegotiateProtocolVersion` | McpServer.cs | 299 | 8 |
| `TrySummarize` | RoslynInspector.cs | 39 | 6 |
| `RunAsync` | McpServer.cs | 46 | 6 |

### Observations

- `CallTool` (24) and `HandleRequest` (17) are the clear hotspots — both are string-dispatch chains that grow with every new tool/method. Known architectural pattern; complexity reflects breadth, not logic depth.
- `ReadMessageWithFramingAsync` (14) handles dual framing modes in one method — candidate for splitting if framing modes grow.
- Tool output is correct and actionable.

### Gaps / issues to triage

- None identified.

---

## 2026-03-14 — get_semantic_diagnostics on src/Rosalyn.Server

### Findings (resolved)

- BCL ref pack fix: resolved — SDK ref assemblies loaded via `dotnet --info`.
- ISS-002 (duplicate attributes): resolved — `.cs` files under `obj/` are excluded.
- ISS-003 (duplicate find_references hits): resolved — same `obj/` exclusion fix.

---

## 2026-03-16 — set_root MCP result shape

### Findings (resolved)

- `set_root` was the only success path without `structuredContent`, while the Codex MCP bridge expects a structured tool result payload.
- Success shape aligned to `{ "isError": false, "structuredContent": {}, "content": [] }` and covered by a server-level regression test.

---

## 2026-03-16 — semantic diagnostics BCL refs

### Findings (resolved)

- `get_semantic_diagnostics` loaded BCL metadata from `shared/Microsoft.NETCore.App`, which caused widespread missing-type errors in semantic compilation.
- Reference resolution now uses `packs/Microsoft.NETCore.App.Ref/<version>/ref/net*`, matching the documented architecture and producing clean diagnostics for a valid test project.

---

## 2026-03-16 — hosted MCP dotnet discovery

### Findings (resolved)

- The hosted MCP process could reproduce missing-BCL diagnostics when `dotnet` was not discoverable on `PATH`.
- Reference-pack discovery now accepts a `DOTNET_EXE` environment override and also probes well-known install paths before falling back to `PATH`.
