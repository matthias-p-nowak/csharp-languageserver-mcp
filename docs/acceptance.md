# Acceptance boundaries

Pass/fail checks that define the minimum bar for a working server. Each AB maps to a canonical requirement in `docs/design.md`.

- AB-001: The repository contains a runnable .NET MCP server entrypoint.
- AB-002: At least one Roslyn-backed insight capability is exposed through MCP.
- AB-003: Newly discovered missing capabilities are recorded in `docs/issues.md`.
- AB-004: Developer workflows default to MCP-based C# retrieval.
- AB-005: `roslyn_syntax_summary` can be invoked through MCP `tools/call` and returns a JSON result.
