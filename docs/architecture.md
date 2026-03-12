# Architecture

## Implemented components
- `src/Rosalyn.Server/Program.cs`: console entrypoint that runs the server over stdio, accepts optional `--root <path>`, and appends startup/runtime diagnostics to `~/tmp/rosalyn/new-rosalyn.log` with non-fatal fallback to `/tmp/rosalyn/new-rosalyn.log` when the primary path is not writable.
- `src/Rosalyn.Server/McpServer.cs`: JSON-RPC/MCP message loop supporting `initialize`, `initialized`, `notifications/initialized`, `shutdown`, `exit`, `server/capabilities`, `resources/list`, `resources/templates/list`, `ping`, `tools/list`, and `tools/call`; `initialize` negotiates `protocolVersion` against a highest-supported date (`2025-11-25`) and response framing mirrors inbound framing mode.
- `src/Rosalyn.Server/RoslynInspector.cs`: Roslyn-backed syntax analyzer used by tools.
- `src/Rosalyn.Server/SyntaxSummary.cs`: typed output contract for declaration counts.

## Implemented tool
- Tool name: `roslyn_syntax_summary`
- Input: repository-relative `.cs` file path.
- Output: counts of namespaces, classes, records, interfaces, enums, structs, and methods.
- Validation: rejects missing path, non-`.cs` files, out-of-root paths, and missing files.

## Constraints
- Runtime target: .NET 9 console application.
- Roslyn dependency: `Microsoft.CodeAnalysis.CSharp`.
- Transport: stdio, accepting both `Content-Length` framed JSON-RPC and line-delimited JSON-RPC requests.
- Response framing: mirrors inbound framing mode (`Content-Length` or line-delimited JSON).
- Stderr policy: no runtime handshake diagnostics are emitted to stderr.
- Initialize negotiation: defaults to highest supported protocol when client omits `protocolVersion`, accepts supported requested versions, and returns JSON-RPC invalid-params errors for malformed or unsupported future versions.
- Resource discovery shape: returns empty `resources` and `resourceTemplates` arrays because this server does not publish MCP resources.
