# Tool: `roslyn_syntax_summary`

## Input
Repository-relative C# file path.

## Output
Counts for namespaces, classes, records, interfaces, enums, structs, and methods from Roslyn syntax analysis.

## Error behavior
Return a tool error when the path is missing, outside the repository root, does not exist, or is not a `.cs` file.
