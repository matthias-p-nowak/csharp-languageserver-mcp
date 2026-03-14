namespace Rosalyn.Server;

/// <summary>
/// The full source text and location of a method body returned by <c>get_method_body</c>.
/// </summary>
internal sealed record MethodBodyResult(
    string File,
    int StartLine,
    int EndLine,
    string Text);
