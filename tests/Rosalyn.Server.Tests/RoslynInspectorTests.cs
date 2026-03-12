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

            var inspector = new RoslynInspector(repositoryRoot);
            var success = inspector.TrySummarize("Sample.cs", out var summary, out var error);

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
            var inspector = new RoslynInspector(repositoryRoot);
            var success = inspector.TrySummarize("not-csharp.txt", out var summary, out var error);

            Assert.False(success);
            Assert.Null(summary);
            Assert.Equal("Only .cs files are supported.", error);
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
