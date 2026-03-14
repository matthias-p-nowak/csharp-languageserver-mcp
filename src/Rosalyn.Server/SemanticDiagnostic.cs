namespace Rosalyn.Server;

/// <summary>
/// A compiler diagnostic (error or warning) from Roslyn semantic analysis.
/// </summary>
internal sealed record SemanticDiagnostic(
    string File,
    int Line,
    string Severity,
    string Id,
    string Message);
