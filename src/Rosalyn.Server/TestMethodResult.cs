namespace Rosalyn.Server;

/// <summary>
/// Describes a single test method found by <c>find_test_methods</c>.
/// </summary>
/// <param name="File">Repository-relative path to the source file.</param>
/// <param name="Line">1-based line number of the method declaration.</param>
/// <param name="ContainingType">Name of the enclosing type.</param>
/// <param name="MethodName">Name of the test method.</param>
internal sealed record TestMethodResult(string File, int Line, string ContainingType, string MethodName);
