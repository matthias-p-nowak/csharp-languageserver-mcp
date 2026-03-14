using Rosalyn.Server;
using Xunit;

namespace Rosalyn.Server.Tests;

/// <summary>
/// Unit tests for Roslyn-backed syntax analysis.
/// </summary>
public sealed class RoslynInspectorTests
{
    /// <summary>
    /// Verifies declaration counts for a simple sample C# file.
    /// </summary>
    [Fact]
    public void TrySummarize_ReturnsExpectedDeclarationCounts()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            var samplePath = Path.Combine(repositoryRoot, "Sample.cs");
            File.WriteAllText(samplePath, "namespace Demo; public class C { public void M(){} }");

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TrySummarize(repositoryRoot, "Sample.cs", out var summary, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(summary);
            Assert.Equal(1, summary!.NamespaceCount);
            Assert.Equal(1, summary.ClassCount);
            Assert.Equal(1, summary.MethodCount);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies file-extension validation rejects non-C# files.
    /// </summary>
    [Fact]
    public void TrySummarize_RejectsNonCsFile()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TrySummarize(repositoryRoot, "not-csharp.txt", out var summary, out var error);

            Assert.False(success);
            Assert.Null(summary);
            Assert.Equal("Only .cs files are supported.", error);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryAnalyzeComplexity ranks methods by cyclomatic complexity.
    /// </summary>
    [Fact]
    public void TryAnalyzeComplexity_RanksByComplexityDescending()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            // Simple method: complexity 1. Complex method: complexity 1 + 3 branches = 4.
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class C
                {
                    public void Simple() { }
                    public void Complex(int x)
                    {
                        if (x > 0) { }
                        else if (x < 0) { }
                        for (int i = 0; i < x; i++) { }
                    }
                }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryAnalyzeComplexity(repositoryRoot, ".", 10, out var results, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(results);
            Assert.Equal(2, results!.Count);
            Assert.Equal("Complex", results[0].Method);
            Assert.Equal("Simple", results[1].Method);
            Assert.True(results[0].Complexity > results[1].Complexity);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryAnalyzeComplexity rejects a directory outside the allowed root.
    /// </summary>
    [Fact]
    public void TryAnalyzeComplexity_RejectsDirectoryOutsideRoot()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryAnalyzeComplexity(repositoryRoot, "../outside", 10, out var results, out var error);

            Assert.False(success);
            Assert.Null(results);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryFindSymbol returns matches for a named symbol with correct kind and file.
    /// </summary>
    [Fact]
    public void TryFindSymbol_ReturnsMatchesForNamedSymbol()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class Foo { public void Bar() { } }
                public class Baz { }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryFindSymbol(repositoryRoot, "Foo", ".", out var matches, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(matches);
            Assert.Single(matches!);
            Assert.Equal("Class", matches![0].Kind);
            Assert.Contains("Sample.cs", matches[0].File);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetDocumentSymbols returns all declared symbols in source order.
    /// </summary>
    [Fact]
    public void TryGetDocumentSymbols_ReturnsAllSymbolsInOrder()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class MyClass
                {
                    public string MyProp { get; set; }
                    public void MyMethod() { }
                }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryGetDocumentSymbols(repositoryRoot, "Sample.cs", out var symbols, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(symbols);
            var kinds = symbols!.Select(s => s.Kind).ToList();
            Assert.Contains("Namespace", kinds);
            Assert.Contains("Class", kinds);
            Assert.Contains("Property", kinds);
            Assert.Contains("Method", kinds);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "rosalyn-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
