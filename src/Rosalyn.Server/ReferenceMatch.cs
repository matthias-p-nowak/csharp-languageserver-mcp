namespace Rosalyn.Server;

/// <summary>
/// A usage site of a symbol found in a C# source file.
/// </summary>
internal sealed record ReferenceMatch(
    string File,
    int Line,
    string Context);
