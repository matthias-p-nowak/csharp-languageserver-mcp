# MCP Handshake Note

This document summarizes the expected MCP startup handshake for `Rosalyn.Server`.
Canonical requirements remain in `docs/design.md`.

## Expected flow
1. Client sends `initialize` request with `protocolVersion`.
2. Server responds with negotiated `protocolVersion`, `serverInfo`, and `capabilities`.
3. Client sends `notifications/initialized` (or `initialized`).
4. Client may call discovery endpoints:
   - `tools/list`
   - `resources/list`
   - `resources/templates/list`
   - `server/capabilities`

## Server behavior
- Accepts both request framing modes:
  - `Content-Length` framed JSON-RPC
  - line-delimited JSON-RPC
- Mirrors response framing to inbound request framing mode.
- Supports lifecycle methods as requests and notifications:
  - `notifications/initialized` ✓ (spec-compliant)
  - `initialized` ⚠️ **non-standard** — MCP spec only defines `notifications/initialized`; bare `initialized` is a custom alias. Kept for compatibility with clients that send it.
  - `shutdown` ⚠️ **non-standard** — MCP spec defines no shutdown method; termination is transport-level (close stdio pipe). Kept for compatibility with LSP-style clients that expect it.
  - `exit` ⚠️ **non-standard** — same as above; this pattern is borrowed from LSP, not MCP. Kept for the same reason.
- For this server, resources are not exposed, so discovery calls return empty arrays:
  - `resources = []`
  - `resourceTemplates = []`
- Keeps protocol channels clean: no handshake diagnostics are written to stdout/stderr.

## Discovery endpoints
- `tools/list` ✓ (spec-compliant)
- `resources/list` ✓ (spec-compliant)
- `resources/templates/list` ✓ (spec-compliant)
- `server/capabilities` ⚠️ **non-standard** — MCP spec has no such endpoint; capabilities are declared once in the `initialize` response only. Kept for compatibility with clients that query it post-handshake.
