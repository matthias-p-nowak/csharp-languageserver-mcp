namespace Rosalyn.Server;

/// <summary>
/// Represents a type that implements a given interface.
/// </summary>
internal sealed record ImplementorResult(
    string File,
    int Line,
    string TypeName,
    string TypeKind);
