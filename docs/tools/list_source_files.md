# Tool: list_source_files

## Purpose
Return repository-relative paths for all C# source files. When projects are loaded (via `set_root`), returns files from the selected (or auto-selected) project, excluding `obj/` trees. Falls back to a full directory scan of the session root when no projects are loaded.

## Arguments
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | string | no | Repository-relative `.csproj` path. Required when multiple projects are loaded. |

## Returns
```json
{
  "files": [
    "src/MyProject/Foo.cs",
    "src/MyProject/Bar.cs"
  ]
}
```

- `obj/` files are always excluded.

## Errors
- `"Project not found: <project>. Known projects: ..."` — specified project does not match any loaded project.
- `"Multiple projects found. Specify 'project': ..."` — `project` is omitted but multiple projects are loaded.
