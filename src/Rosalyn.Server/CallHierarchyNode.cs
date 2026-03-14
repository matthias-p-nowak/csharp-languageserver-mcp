namespace Rosalyn.Server;

/// <summary>
/// Represents a node in a call hierarchy tree (caller or callee).
/// </summary>
internal sealed record CallHierarchyNode(
    string File,
    int Line,
    string ContainingType,
    string MethodName,
    IReadOnlyList<CallHierarchyNode> Children);
