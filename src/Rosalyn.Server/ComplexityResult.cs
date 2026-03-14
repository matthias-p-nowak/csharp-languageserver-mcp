namespace Rosalyn.Server;

/// <summary>
/// Cyclomatic complexity entry for one method.
/// </summary>
internal sealed record ComplexityResult(
    string File,
    string Namespace,
    string Type,
    string Method,
    int Line,
    int Complexity);
