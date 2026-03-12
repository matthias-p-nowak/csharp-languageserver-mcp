# Design

## Product intent
Rosalyn is an MCP server built with C#/.NET that provides codebase insight using Roslyn.

## Canonical requirements
- DR-001 (runtime): Implement the server as a C#/.NET 9 application.
- DR-002 (analysis engine): Use Roslyn as the primary mechanism for code insight.
- DR-003 (dogfooding loop): Use the server on this repository during development and treat discovered gaps as tracked work. Gaps are first noted in `tmp/dogfood.md`; after triage (duplicate check and severity assessment) they are promoted to `docs/issues.md` tagged `[dogfood]` and entered into the shared priority queue.
- DR-004 (retrieval policy): Prefer this MCP tool for C# code retrieval in this project; use fallback file reads only when the MCP route is not possible.
- DR-005 (initial insight operation): Expose a `roslyn_syntax_summary` MCP tool that analyzes one C# file and returns declaration-level counts.

## MCP tool behavior
- Tool name: `roslyn_syntax_summary`
- Input: repository-relative C# file path.
- Output: counts for namespaces, classes, records, interfaces, enums, structs, and methods from Roslyn syntax analysis.
- Error behavior: return a tool error when the path is missing, outside the repository root, does not exist, or is not a `.cs` file.

## MCP transport behavior
- Request framing: accept both `Content-Length` framed JSON-RPC messages and line-delimited JSON-RPC messages.
- Response framing: mirror the inbound framing mode for each request (`Content-Length` for framed requests, newline-delimited JSON for line-delimited requests).
- `initialize` protocol negotiation: accept client `protocolVersion` in `yyyy-MM-dd` format, negotiate to the requested version when supported, default to the server highest supported version when omitted, and return a JSON-RPC invalid-params error for malformed or unsupported future versions.
- Lifecycle methods: accept `initialized`, `notifications/initialized`, `shutdown`, and `exit` as both notifications and requests to support client startup/teardown sequencing.
- Capability introspection: support `server/capabilities` requests with machine-readable protocol/server/capability metadata.
- Discovery calls: support `resources/list` and `resources/templates/list`; return empty arrays when no resources/templates are exposed.
- Runtime diagnostics: keep MCP stdout/stderr protocol channels clean; avoid temporary handshake debug logging by default.
- `ping` method: respond with an empty result `{}` to keep the connection alive and satisfy MCP clients that require it.

## Acceptance boundaries
- AB-001: The repository contains a runnable .NET MCP server entrypoint.
- AB-002: At least one Roslyn-backed insight capability is exposed through MCP.
- AB-003: Newly discovered missing capabilities are recorded in `docs/issues.md`.
- AB-004: Developer workflows default to MCP-based C# retrieval.
- AB-005: `roslyn_syntax_summary` can be invoked through MCP `tools/call` and returns a JSON result.
