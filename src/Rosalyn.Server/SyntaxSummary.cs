namespace Rosalyn.Server;

/// <summary>
/// Roslyn syntax summary for one C# file.
/// </summary>
internal sealed record SyntaxSummary(
    string Path,
    int NamespaceCount,
    int ClassCount,
    int RecordCount,
    int InterfaceCount,
    int EnumCount,
    int StructCount,
    int MethodCount);
