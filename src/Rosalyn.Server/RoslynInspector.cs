using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rosalyn.Server;

/// <summary>
/// Performs Roslyn-backed syntax analysis for C# files.
/// </summary>
internal sealed class RoslynInspector
{
    private readonly string[] allowedDirectories;

    /// <summary>
    /// Creates a new inspector restricted to the given allowed directories.
    /// </summary>
    /// <param name="allowedDirectories">Absolute paths the inspector may access.</param>
    public RoslynInspector(string[] allowedDirectories)
    {
        this.allowedDirectories = allowedDirectories;
    }

    /// <summary>
    /// Returns true when <paramref name="absolutePath"/> is within any allowed directory.
    /// </summary>
    public bool IsWithinAllowedDirectory(string absolutePath)
    {
        return allowedDirectories.Any(dir => IsWithinDirectory(absolutePath, dir));
    }

    /// <summary>
    /// Produces a declaration-level syntax summary for one repository-relative C# file.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to a .cs file.</param>
    /// <param name="summary">Output syntax summary on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TrySummarize(string repositoryRoot, string relativePath, out SyntaxSummary? summary, out string? error)
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

        if (!IsWithinDirectory(absolutePath, repositoryRoot))
        {
            error = "The provided path must be inside the repository root.";
            return false;
        }

        if (!IsWithinAllowedDirectory(absolutePath))
        {
            error = "The provided path is not within any allowed directory.";
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
        var descendants = root.DescendantNodes().ToList();

        summary = new SyntaxSummary(
            relativePath,
            descendants.OfType<NamespaceDeclarationSyntax>().Count() +
                descendants.OfType<FileScopedNamespaceDeclarationSyntax>().Count(),
            descendants.OfType<ClassDeclarationSyntax>().Count(),
            descendants.OfType<RecordDeclarationSyntax>().Count(),
            descendants.OfType<InterfaceDeclarationSyntax>().Count(),
            descendants.OfType<EnumDeclarationSyntax>().Count(),
            descendants.OfType<StructDeclarationSyntax>().Count(),
            descendants.OfType<MethodDeclarationSyntax>().Count());

        return true;
    }

    /// <summary>
    /// Scans all .cs files under <paramref name="relativeDirectory"/> and returns methods
    /// sorted descending by cyclomatic complexity, capped at <paramref name="topN"/> results.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="topN">Maximum number of results to return.</param>
    /// <param name="results">Ranked complexity results on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryAnalyzeComplexity(
        string repositoryRoot,
        string relativeDirectory,
        int topN,
        out IReadOnlyList<ComplexityResult>? results,
        out string? error)
    {
        results = null;
        error = null;

        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            error = "Argument 'directory' is required.";
            return false;
        }

        var absoluteDir = Path.GetFullPath(Path.Combine(repositoryRoot, relativeDirectory));

        if (!IsWithinDirectory(absoluteDir, repositoryRoot))
        {
            error = "The provided directory must be inside the repository root.";
            return false;
        }

        if (!IsWithinAllowedDirectory(absoluteDir))
        {
            error = "The provided directory is not within any allowed directory.";
            return false;
        }

        if (!Directory.Exists(absoluteDir))
        {
            error = $"Directory not found: {relativeDirectory}";
            return false;
        }

        var entries = new List<ComplexityResult>();

        foreach (var filePath in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var namespaceName = method.Ancestors()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .FirstOrDefault()?.Name.ToString() ?? string.Empty;

                var typeName = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.Text ?? string.Empty;

                var line = tree.GetLineSpan(method.Identifier.Span).StartLinePosition.Line + 1;

                var complexity = ComputeCyclomaticComplexity(method);

                entries.Add(new ComplexityResult(relativeFilePath, namespaceName, typeName, method.Identifier.Text, line, complexity));
            }
        }

        results = entries
            .OrderByDescending(e => e.Complexity)
            .Take(topN)
            .ToList();

        return true;
    }

    /// <summary>
    /// Computes cyclomatic complexity for a method: 1 (base) + 1 per branching node.
    /// Branching nodes: if, else if, for, foreach, while, do, case, catch, &amp;&amp;, ||, ??, ?:.
    /// </summary>
    private static int ComputeCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1;

        foreach (var node in method.DescendantNodes())
        {
            complexity += node switch
            {
                IfStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                WhileStatementSyntax => 1,
                DoStatementSyntax => 1,
                SwitchSectionSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                BinaryExpressionSyntax b when
                    b.IsKind(SyntaxKind.LogicalAndExpression) ||
                    b.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0
            };
        }

        return complexity;
    }

    private static bool IsWithinDirectory(string absolutePath, string directory)
    {
        var dirWithSeparator = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        return absolutePath.StartsWith(dirWithSeparator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(absolutePath, directory, StringComparison.OrdinalIgnoreCase);
    }
}
