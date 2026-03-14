using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rosalyn.Server;

/// <summary>
/// Performs Roslyn-backed syntax and semantic analysis for C# files.
/// </summary>
internal sealed class RoslynInspector
{
    private readonly string[] allowedDirectories;

    /// <summary>Key: absolute .csproj path. Value: loaded compilation.</summary>
    private Dictionary<string, CSharpCompilation>? projectCompilations;

    /// <summary>Key: absolute .csproj path. Value: syntax trees in that project.</summary>
    private Dictionary<string, IReadOnlyList<SyntaxTree>>? projectTrees;

    /// <summary>
    /// Creates a new inspector restricted to the given allowed directories.
    /// </summary>
    /// <param name="allowedDirectories">Absolute paths the inspector may access.</param>
    public RoslynInspector(string[] allowedDirectories)
    {
        this.allowedDirectories = allowedDirectories;
    }

    /// <summary>
    /// Discovers all .csproj files under <paramref name="sessionRoot"/>, loads NuGet
    /// references from <c>project.assets.json</c> plus BCL ref packs, and builds an
    /// in-memory <see cref="CSharpCompilation"/> per project. Results are cached for
    /// the session.
    /// </summary>
    /// <param name="sessionRoot">Absolute session root path.</param>
    public void LoadProjects(string sessionRoot)
    {
        var compilations = new Dictionary<string, CSharpCompilation>(StringComparer.OrdinalIgnoreCase);
        var trees = new Dictionary<string, IReadOnlyList<SyntaxTree>>(StringComparer.OrdinalIgnoreCase);
        var bclRefs = ResolveBclReferences();

        foreach (var csproj in Directory.EnumerateFiles(sessionRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var projectDir = Path.GetDirectoryName(csproj)!;

            var references = new List<MetadataReference>(bclRefs);
            foreach (var dll in ResolveNuGetReferences(projectDir))
            {
                try { references.Add(MetadataReference.CreateFromFile(dll)); }
                catch { /* skip unreadable assemblies */ }
            }

            var objPath = Path.Combine(projectDir, "obj") + Path.DirectorySeparatorChar;
            var syntaxTrees = new List<SyntaxTree>();
            foreach (var cs in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (cs.StartsWith(objPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = File.ReadAllText(cs);
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, path: cs));
            }

            var assemblyName = Path.GetFileNameWithoutExtension(csproj);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var relativeCsproj = Path.GetRelativePath(sessionRoot, csproj);
            compilations[relativeCsproj] = compilation;
            trees[relativeCsproj] = syntaxTrees;
        }

        projectCompilations = compilations;
        projectTrees = trees;
    }

    /// <summary>
    /// Resolves BCL reference assemblies from the .NET shared runtime folder
    /// (<c>shared/Microsoft.NETCore.App/&lt;version&gt;/</c>).
    /// Locates the dotnet root via <c>dotnet --info</c>.
    /// Returns an empty list if the runtime folder cannot be found.
    /// </summary>
    private static IReadOnlyList<MetadataReference> ResolveBclReferences()
    {
        try
        {
            // Get SDK base path, e.g. /usr/lib64/dotnet-sdk-9.0/sdk/9.0.111/
            var info = RunProcess("dotnet", "--info");
            var basePath = info
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("Base Path:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l["Base Path:".Length..].Trim())
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
            {
                return Array.Empty<MetadataReference>();
            }

            // Walk up to dotnet root: .../sdk/9.0.111/ -> .../
            var sdkRoot = Path.GetFullPath(Path.Combine(basePath, "..", ".."));
            var sharedDir = Path.Combine(sdkRoot, "shared", "Microsoft.NETCore.App");

            if (!Directory.Exists(sharedDir))
            {
                return Array.Empty<MetadataReference>();
            }

            // Pick highest version directory.
            var runtimeDir = Directory.EnumerateDirectories(sharedDir)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (runtimeDir is null || !Directory.Exists(runtimeDir))
            {
                return Array.Empty<MetadataReference>();
            }

            var refs = new List<MetadataReference>();
            foreach (var dll in Directory.EnumerateFiles(runtimeDir, "*.dll"))
            {
                try { refs.Add(MetadataReference.CreateFromFile(dll)); }
                catch { /* skip unreadable */ }
            }

            return refs;
        }
        catch
        {
            return Array.Empty<MetadataReference>();
        }
    }

    /// <summary>
    /// Resolves NuGet compile-time reference DLL paths from <c>project.assets.json</c>.
    /// Returns absolute paths to all compile-time assemblies listed in the asset file.
    /// Returns an empty enumerable if the asset file is missing or malformed.
    /// </summary>
    private static IEnumerable<string> ResolveNuGetReferences(string projectDir)
    {
        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            yield break;
        }

        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(assetsPath));
        }
        catch
        {
            yield break;
        }

        using (doc)
        {
            // packageFolders: { "/home/me/.nuget/packages/": {} }
            var packageFolders = doc.RootElement
                .GetProperty("packageFolders")
                .EnumerateObject()
                .Select(p => p.Name)
                .ToList();

            // targets: { "net9.0": { "PackageName/version": { "compile": { "lib/net8.0/Foo.dll": {} } } } }
            var targets = doc.RootElement.GetProperty("targets");
            // Pick first target (e.g. "net9.0"); skip RID-specific targets (contain "/")
            var targetEntry = targets.EnumerateObject()
                .FirstOrDefault(t => !t.Name.Contains('/'));

            if (targetEntry.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var package in targetEntry.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("compile", out var compileAssets))
                {
                    continue;
                }

                // package.Name is "PackageName/version"
                var slash = package.Name.LastIndexOf('/');
                if (slash < 0) continue;
                var packageId = package.Name[..slash].ToLowerInvariant();
                var version = package.Name[(slash + 1)..];

                foreach (var asset in compileAssets.EnumerateObject())
                {
                    // asset.Name is a relative path like "lib/net8.0/Foo.dll"
                    if (asset.Name.EndsWith("/_._", StringComparison.Ordinal)) continue;

                    foreach (var folder in packageFolders)
                    {
                        var candidate = Path.Combine(folder, packageId, version, asset.Name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(candidate))
                        {
                            yield return candidate;
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs a process and returns its stdout as a string.
    /// </summary>
    private static string RunProcess(string fileName, string arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    /// <summary>
    /// Returns the list of known project keys (relative .csproj paths).
    /// Returns null if <see cref="LoadProjects"/> has not been called yet.
    /// </summary>
    public IReadOnlyList<string>? KnownProjects =>
        projectCompilations is null ? null : projectCompilations.Keys.ToList();

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
    /// Searches all .cs files under <paramref name="relativeDirectory"/> for declarations
    /// whose name exactly matches <paramref name="symbolName"/>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="symbolName">Exact symbol name to find (case-sensitive).</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="matches">Symbol matches on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryFindSymbol(
        string repositoryRoot,
        string symbolName,
        string relativeDirectory,
        out IReadOnlyList<SymbolMatch>? matches,
        out string? error)
    {
        matches = null;
        error = null;

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            error = "Argument 'name' is required.";
            return false;
        }

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

        var results = new List<SymbolMatch>();

        foreach (var filePath in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var (name, kind, span) in GetDeclaredSymbols(root, tree))
            {
                if (name == symbolName)
                {
                    results.Add(new SymbolMatch(relativeFilePath, span, kind));
                }
            }
        }

        matches = results;
        return true;
    }

    /// <summary>
    /// Returns all named symbols declared in a single repository-relative C# file.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to a .cs file.</param>
    /// <param name="symbols">Symbols in source order on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetDocumentSymbols(
        string repositoryRoot,
        string relativePath,
        out IReadOnlyList<SymbolMatch>? symbols,
        out string? error)
    {
        symbols = null;
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

        symbols = GetDeclaredSymbols(root, tree)
            .Select(t => new SymbolMatch(relativePath, t.Line, t.Kind))
            .ToList();

        return true;
    }

    /// <summary>
    /// Resolves a <see cref="CSharpCompilation"/> given an optional relative .csproj hint.
    /// Returns false and sets <paramref name="error"/> when the project cannot be resolved.
    /// </summary>
    private bool TryResolveCompilation(
        string? relativeProject,
        out CSharpCompilation? compilation,
        out string? error)
    {
        compilation = null;
        error = null;

        if (projectCompilations is null)
        {
            error = "No projects loaded. Ensure set_root was called successfully.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(relativeProject))
        {
            if (!projectCompilations.TryGetValue(relativeProject, out compilation))
            {
                error = $"Project not found: {relativeProject}. Known projects: {string.Join(", ", projectCompilations.Keys)}";
                return false;
            }

            return true;
        }

        if (projectCompilations.Count == 1)
        {
            compilation = projectCompilations.Values.First();
            return true;
        }

        if (projectCompilations.Count == 0)
        {
            error = "No .csproj files found under the session root.";
            return false;
        }

        error = $"Multiple projects found. Specify 'project': {string.Join(", ", projectCompilations.Keys)}";
        return false;
    }

    /// <summary>
    /// Finds all usage sites of <paramref name="symbolName"/> across the resolved project.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="symbolName">Exact symbol name to find references for.</param>
    /// <param name="relativeProject">Optional relative .csproj path to select project.</param>
    /// <param name="references">Reference matches on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryFindReferences(
        string repositoryRoot,
        string symbolName,
        string? relativeProject,
        out IReadOnlyList<ReferenceMatch>? references,
        out string? error)
    {
        references = null;

        if (!TryResolveCompilation(relativeProject, out var compilation, out error))
        {
            return false;
        }

        var results = new List<ReferenceMatch>();

        foreach (var tree in compilation!.SyntaxTrees)
        {
            var root = tree.GetCompilationUnitRoot();
            var model = compilation.GetSemanticModel(tree);
            var relativeFile = Path.GetRelativePath(repositoryRoot, tree.FilePath);

            foreach (var node in root.DescendantNodes())
            {
                // Skip IdentifierNameSyntax whose parent is a MemberAccessExpressionSyntax —
                // the member access node itself will be matched, avoiding double-counting.
                if (node is IdentifierNameSyntax && node.Parent is MemberAccessExpressionSyntax)
                {
                    continue;
                }

                string? nodeName = node switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null
                };

                if (nodeName != symbolName)
                {
                    continue;
                }

                var symbolInfo = model.GetSymbolInfo(node);
                if (symbolInfo.Symbol is null && symbolInfo.CandidateSymbols.IsEmpty)
                {
                    continue;
                }

                var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                var lineText = tree.GetText().Lines[line - 1].ToString().Trim();
                results.Add(new ReferenceMatch(relativeFile, line, lineText));
            }
        }

        references = results;
        return true;
    }

    /// <summary>
    /// Returns the definition site of the symbol at the given file and line number.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to the .cs file.</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="relativeProject">Optional relative .csproj path to select project.</param>
    /// <param name="definition">Definition site on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetSymbolDefinition(
        string repositoryRoot,
        string relativePath,
        int line,
        string? relativeProject,
        out SymbolMatch? definition,
        out string? error)
    {
        definition = null;

        if (!TryResolveCompilation(relativeProject, out var compilation, out error))
        {
            return false;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
        var tree = compilation!.SyntaxTrees.FirstOrDefault(t =>
            string.Equals(t.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase));

        if (tree is null)
        {
            error = $"File not found in project: {relativePath}";
            return false;
        }

        var text = tree.GetText();
        if (line < 1 || line > text.Lines.Count)
        {
            error = $"Line {line} is out of range (file has {text.Lines.Count} lines).";
            return false;
        }

        // Find the first named node on the given line.
        var lineSpan = text.Lines[line - 1].Span;
        var root = tree.GetCompilationUnitRoot();
        var model = compilation.GetSemanticModel(tree);

        var node = root.DescendantNodes(lineSpan)
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault();

        if (node is null)
        {
            error = $"No identifier found on line {line}.";
            return false;
        }

        var symbolInfo = model.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (symbol is null)
        {
            error = $"Could not resolve symbol on line {line}.";
            return false;
        }

        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null)
        {
            error = "Symbol has no source location (may be from a referenced assembly).";
            return false;
        }

        var defLine = loc.GetLineSpan().StartLinePosition.Line + 1;
        var defFile = Path.GetRelativePath(repositoryRoot, loc.SourceTree!.FilePath);
        var defKind = symbol.Kind.ToString();

        definition = new SymbolMatch(defFile, defLine, defKind);
        return true;
    }

    /// <summary>
    /// Returns compiler diagnostics for a file or the entire project.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Optional repository-relative .cs file path; if null, returns project-wide diagnostics.</param>
    /// <param name="relativeProject">Optional relative .csproj path to select project.</param>
    /// <param name="diagnostics">Diagnostics on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetSemanticDiagnostics(
        string repositoryRoot,
        string? relativePath,
        string? relativeProject,
        out IReadOnlyList<SemanticDiagnostic>? diagnostics,
        out string? error)
    {
        diagnostics = null;

        if (!TryResolveCompilation(relativeProject, out var compilation, out error))
        {
            return false;
        }

        IEnumerable<Diagnostic> raw;

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            var tree = compilation!.SyntaxTrees.FirstOrDefault(t =>
                string.Equals(t.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase));

            if (tree is null)
            {
                error = $"File not found in project: {relativePath}";
                return false;
            }

            raw = compilation.GetSemanticModel(tree).GetDiagnostics();
        }
        else
        {
            raw = compilation!.GetDiagnostics();
        }

        diagnostics = raw
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d =>
            {
                var loc = d.Location.IsInSource ? d.Location.GetLineSpan() : default;
                var file = d.Location.IsInSource
                    ? Path.GetRelativePath(repositoryRoot, loc.Path)
                    : string.Empty;
                var diagLine = d.Location.IsInSource ? loc.StartLinePosition.Line + 1 : 0;
                return new SemanticDiagnostic(file, diagLine, d.Severity.ToString(), d.Id, d.GetMessage());
            })
            .ToList();

        return true;
    }

    /// <summary>
    /// Enumerates all named declaration nodes in <paramref name="root"/>,
    /// yielding (name, kind, line) tuples in source order.
    /// </summary>
    private static IEnumerable<(string Name, string Kind, int Line)> GetDeclaredSymbols(
        CompilationUnitSyntax root, SyntaxTree tree)
    {
        foreach (var node in root.DescendantNodes())
        {
            var (name, kind) = node switch
            {
                NamespaceDeclarationSyntax n => (n.Name.ToString(), "Namespace"),
                FileScopedNamespaceDeclarationSyntax n => (n.Name.ToString(), "Namespace"),
                ClassDeclarationSyntax n => (n.Identifier.Text, "Class"),
                RecordDeclarationSyntax n => (n.Identifier.Text, "Record"),
                StructDeclarationSyntax n => (n.Identifier.Text, "Struct"),
                InterfaceDeclarationSyntax n => (n.Identifier.Text, "Interface"),
                EnumDeclarationSyntax n => (n.Identifier.Text, "Enum"),
                MethodDeclarationSyntax n => (n.Identifier.Text, "Method"),
                PropertyDeclarationSyntax n => (n.Identifier.Text, "Property"),
                FieldDeclarationSyntax n => (n.Declaration.Variables.First().Identifier.Text, "Field"),
                EnumMemberDeclarationSyntax n => (n.Identifier.Text, "EnumMember"),
                _ => (string.Empty, string.Empty)
            };

            if (name.Length > 0)
            {
                var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                yield return (name, kind, line);
            }
        }
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
