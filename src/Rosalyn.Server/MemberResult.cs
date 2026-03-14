namespace Rosalyn.Server;

/// <summary>
/// Represents a single member (field, property, method, constructor, or event) found in a type declaration.
/// </summary>
internal sealed record MemberResult(
    string File,
    int Line,
    string TypeName,
    string MemberKind,
    string Name,
    string Signature);
