namespace Rosalyn.Server;

/// <summary>
/// A named symbol declaration found in a C# source file.
/// </summary>
internal sealed record SymbolMatch(
    string File,
    int Line,
    string Kind);
