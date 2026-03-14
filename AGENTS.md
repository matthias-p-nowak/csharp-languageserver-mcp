# Agent guidance

## Documentation authority order
When sources conflict, prefer in this order:
1. `docs/design.md` — canonical requirements and decisions
2. `docs/architecture.md` — implemented behavior and constraints
3. `docs/handshake.md` — MCP transport and handshake detail
4. `docs/tools/<tool-name>.md` — per-tool input/output/error contracts
5. `docs/issues.md` — active work queue

## MCP tool specs
- Each tool has its own spec file at `docs/tools/<tool-name>.md`.
- Load only the spec for the tool you are working on; do not load all tool specs.
- The tool registry in `docs/design.md` lists all tools with links to their spec files.

## Dogfooding loop
- Use the `roslyn_syntax_summary` MCP tool for C# code retrieval in this repo; fall back to file reads only when MCP is not available.
- When you discover a missing capability or a gap, note it in `tmp/dogfood.md`.
- Do not promote dogfood notes directly to `docs/issues.md`; triage happens separately.
