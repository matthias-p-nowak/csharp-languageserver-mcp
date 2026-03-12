using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rosalyn.Server;

/// <summary>
/// Performs Roslyn-backed syntax analysis for C# files.
/// </summary>
internal sealed class RoslynInspector
{
    private readonly string repositoryRoot;

    /// <summary>
    /// Creates a new inspector bound to a repository root.
    /// </summary>
    /// <param name="repositoryRoot">Absolute repository root path.</param>
    public RoslynInspector(string repositoryRoot)
    {
        this.repositoryRoot = repositoryRoot;
    }

    /// <summary>
    /// Produces a declaration-level syntax summary for one repository-relative C# file.
    /// </summary>
    /// <param name="relativePath">Repository-relative path to a .cs file.</param>
    /// <param name="summary">Output syntax summary on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TrySummarize(string relativePath, out SyntaxSummary? summary, out string? error)
    {
        summary = null;
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Argument 'path' is required.";
            return false;
        }

        if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only .cs files are supported.";
            return false;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));

        // Prevent path traversal by enforcing repository-root containment.
        if (!IsWithinRoot(absolutePath))
        {
            error = "The provided path must be inside the repository root.";
            return false;
        }

        if (!File.Exists(absolutePath))
        {
            error = $"File not found: {relativePath}";
            return false;
        }

        var source = File.ReadAllText(absolutePath);
        var tree = CSharpSyntaxTree.ParseText(source, path: absolutePath);
        var root = tree.GetCompilationUnitRoot();

        summary = new SyntaxSummary(
            relativePath,
            root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().Count() +
                root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<RecordDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<EnumDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<StructDeclarationSyntax>().Count(),
            root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count());

        return true;
    }

    private bool IsWithinRoot(string absolutePath)
    {
        var rootWithSeparator = repositoryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repositoryRoot
            : repositoryRoot + Path.DirectorySeparatorChar;

        return absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(absolutePath, repositoryRoot, StringComparison.OrdinalIgnoreCase);
    }
}
