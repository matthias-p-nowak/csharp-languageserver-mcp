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
