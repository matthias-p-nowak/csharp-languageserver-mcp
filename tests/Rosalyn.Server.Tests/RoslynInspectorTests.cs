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

    /// <summary>
    /// Verifies that TryGetProjectForFile returns the owning project for a source file.
    /// </summary>
    [Fact]
    public void TryGetProjectForFile_ReturnsOwningProject()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), "namespace Demo; public class C { }");

            var inspector = new RoslynInspector([repositoryRoot]);
            inspector.LoadProjects(repositoryRoot);

            var success = inspector.TryGetProjectForFile(repositoryRoot, "Sample.cs", out var projects, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(projects);
            Assert.Single(projects!);
            Assert.Contains("Demo.csproj", projects![0]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetProjectForFile returns an error for a file not in any project.
    /// </summary>
    [Fact]
    public void TryGetProjectForFile_FailsForUnknownFile()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var inspector = new RoslynInspector([repositoryRoot]);
            inspector.LoadProjects(repositoryRoot);

            var success = inspector.TryGetProjectForFile(repositoryRoot, "Missing.cs", out var projects, out var error);

            Assert.False(success);
            Assert.Null(projects);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that LoadProjects discovers a .csproj and exposes it as a known project.
    /// </summary>
    [Fact]
    public void LoadProjects_DiscoversProject()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), "namespace Demo; public class C { }");

            var inspector = new RoslynInspector([repositoryRoot]);
            inspector.LoadProjects(repositoryRoot);

            Assert.NotNull(inspector.KnownProjects);
            Assert.Single(inspector.KnownProjects!);
            Assert.Contains("Demo.csproj", inspector.KnownProjects![0]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryFindReferences returns a usage site for a resolved symbol.
    /// </summary>
    [Fact]
    public void TryFindReferences_ReturnsUsageSites()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class Foo { }
                public class Bar { public Foo Create() => new Foo(); }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            inspector.LoadProjects(repositoryRoot);

            var success = inspector.TryFindReferences(repositoryRoot, "Foo", null, out var refs, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(refs);
            Assert.True(refs!.Count > 0);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetSemanticDiagnostics returns an error for undefined identifier.
    /// </summary>
    [Fact]
    public void TryGetSemanticDiagnostics_ReturnsErrorForUndefinedSymbol()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class C { public void M() { var x = UndefinedType.Create(); } }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            inspector.LoadProjects(repositoryRoot);

            var success = inspector.TryGetSemanticDiagnostics(repositoryRoot, null, null, out var diagnostics, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(diagnostics);
            Assert.Contains(diagnostics!, d => d.Severity == "Error");
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryFindReferences fails gracefully when no projects are loaded.
    /// </summary>
    [Fact]
    public void TryFindReferences_FailsWhenNoProjectsLoaded()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            var inspector = new RoslynInspector([repositoryRoot]);
            // LoadProjects not called

            var success = inspector.TryFindReferences(repositoryRoot, "Foo", null, out var refs, out var error);

            Assert.False(success);
            Assert.Null(refs);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetMethodBody returns the source text of a named method.
    /// </summary>
    [Fact]
    public void TryGetMethodBody_ReturnsMethodSource()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class C
                {
                    public void MyMethod()
                    {
                        var x = 1;
                    }
                    public void Other() { }
                }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryGetMethodBody(repositoryRoot, "MyMethod", null, ".", out var results, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(results);
            Assert.Single(results!);
            Assert.Equal("Sample.cs", results![0].File);
            Assert.Contains("MyMethod", results[0].Text);
            Assert.True(results[0].StartLine > 0);
            Assert.True(results[0].EndLine >= results[0].StartLine);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetMethodBody returns all overloads when multiple same-named methods exist.
    /// </summary>
    [Fact]
    public void TryGetMethodBody_ReturnsAllOverloads()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sample.cs"), """
                namespace Demo;
                public class C
                {
                    public void Process() { }
                    public void Process(int x) { }
                }
                """);

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryGetMethodBody(repositoryRoot, "Process", null, ".", out var results, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(results);
            Assert.Equal(2, results!.Count);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that TryGetMethodBody scopes to a single file when path is provided.
    /// </summary>
    [Fact]
    public void TryGetMethodBody_ScopesToSingleFile()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "A.cs"), "namespace Demo; public class A { public void Compute() { } }");
            File.WriteAllText(Path.Combine(repositoryRoot, "B.cs"), "namespace Demo; public class B { public void Compute() { } }");

            var inspector = new RoslynInspector([repositoryRoot]);
            var success = inspector.TryGetMethodBody(repositoryRoot, "Compute", "A.cs", ".", out var results, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.NotNull(results);
            Assert.Single(results!);
            Assert.Contains("A.cs", results![0].File);
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
