# Tool: list_projects

## Purpose
Return all known project keys (relative `.csproj` paths) loaded for the current session. Useful for orienting in multi-project solutions and for supplying the optional `project` argument to semantic tools.

## Arguments
None.

## Returns
```json
{ "projects": ["src/Foo/Foo.csproj", "tests/Foo.Tests/Foo.Tests.csproj"] }
```

## Errors
- `"Projects not loaded. Call set_root first."` — `set_root` has not been called or no `.csproj` files were found under the session root.
