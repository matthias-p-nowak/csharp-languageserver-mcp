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

            // Include GlobalUsings.g.cs from obj/ to replicate ImplicitUsings behaviour.
            var objDir = objPath.TrimEnd(Path.DirectorySeparatorChar);
            foreach (var globalUsings in Directory.Exists(objDir)
                ? Directory.EnumerateFiles(objDir, "*.GlobalUsings.g.cs", SearchOption.AllDirectories)
                : [])
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(globalUsings), path: globalUsings));
            }

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
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

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
    /// Returns the relative .csproj paths whose source tree contains
    /// <paramref name="relativePath"/>. Returns all matches when a file
    /// appears in multiple projects.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to a .cs file.</param>
    /// <param name="projects">Matching project keys on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetProjectForFile(
        string repositoryRoot,
        string relativePath,
        out IReadOnlyList<string>? projects,
        out string? error)
    {
        projects = null;
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Argument 'path' is required.";
            return false;
        }

        if (projectTrees is null)
        {
            error = "No projects loaded. Ensure set_root was called successfully.";
            return false;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));

        var matches = projectTrees
            .Where(kv => kv.Value.Any(t =>
                string.Equals(t.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key)
            .ToList();

        if (matches.Count == 0)
        {
            error = $"File not found in any loaded project: {relativePath}";
            return false;
        }

        projects = matches;
        return true;
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
    /// Returns the full source text of every method whose name matches
    /// <paramref name="methodName"/>. Scoped to a single file when
    /// <paramref name="relativePath"/> is provided, otherwise to the
    /// <paramref name="relativeDirectory"/> subtree.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="methodName">Exact method name to find (case-sensitive).</param>
    /// <param name="relativePath">Optional repository-relative path to a single .cs file.</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan; used only when <paramref name="relativePath"/> is null. Defaults to <c>"."</c>.</param>
    /// <param name="results">Method body results on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetMethodBody(
        string repositoryRoot,
        string methodName,
        string? relativePath,
        string relativeDirectory,
        out IReadOnlyList<MethodBodyResult>? results,
        out string? error)
    {
        results = null;
        error = null;

        if (string.IsNullOrWhiteSpace(methodName))
        {
            error = "Argument 'name' is required.";
            return false;
        }

        IEnumerable<string> files;

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                error = "Only .cs files are supported.";
                return false;
            }

            var absoluteFilePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));

            if (!IsWithinDirectory(absoluteFilePath, repositoryRoot))
            {
                error = "The provided path must be inside the repository root.";
                return false;
            }

            if (!IsWithinAllowedDirectory(absoluteFilePath))
            {
                error = "The provided path is not within any allowed directory.";
                return false;
            }

            if (!File.Exists(absoluteFilePath))
            {
                error = $"File not found: {relativePath}";
                return false;
            }

            files = [absoluteFilePath];
        }
        else
        {
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

            files = Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories);
        }

        var matches = new List<MethodBodyResult>();

        foreach (var filePath in files)
        {
            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Identifier.Text != methodName)
                {
                    continue;
                }

                var startLine = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
                var endLine = tree.GetLineSpan(method.Span).EndLinePosition.Line + 1;
                var text = method.ToFullString().TrimEnd();

                matches.Add(new MethodBodyResult(relativeFilePath, startLine, endLine, text));
            }
        }

        results = matches;
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

        var objPrefix = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

        diagnostics = raw
            .Where(d =>
            {
                if (d.Severity < DiagnosticSeverity.Warning || !d.Location.IsInSource) return false;
                var path = d.Location.GetLineSpan().Path;
                return !path.StartsWith(objPrefix, StringComparison.OrdinalIgnoreCase);
            })
            .Select(d =>
            {
                var loc = d.Location.GetLineSpan();
                var file = Path.GetRelativePath(repositoryRoot, loc.Path);
                var diagLine = loc.StartLinePosition.Line + 1;
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

    /// <summary>
    /// Returns the namespace names declared in a single repository-relative C# file.
    /// Returns an empty list (success) if the file declares no namespace.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to a .cs file.</param>
    /// <param name="namespaces">Namespace names in source order on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetNamespacesForFile(
        string repositoryRoot,
        string relativePath,
        out IReadOnlyList<string>? namespaces,
        out string? error)
    {
        namespaces = null;
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

        var names = root.DescendantNodes()
            .Select(n => n switch
            {
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fns => fns.Name.ToString(),
                _ => null
            })
            .Where(n => n is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        namespaces = names;
        return true;
    }

    /// <summary>
    /// Returns repository-relative paths for all C# source files.
    /// If projects are loaded, returns files from the specified (or auto-selected) project,
    /// excluding those under <c>obj/</c>. If no projects are loaded, falls back to a
    /// directory scan of <paramref name="repositoryRoot"/>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativeProject">Optional relative .csproj path to select project.</param>
    /// <param name="files">Repository-relative file paths on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryListSourceFiles(
        string repositoryRoot,
        string? relativeProject,
        out IReadOnlyList<string>? files,
        out string? error)
    {
        files = null;
        error = null;

        if (projectTrees is not null)
        {
            // Projects are loaded — use project trees, respecting optional project arg.
            if (!string.IsNullOrWhiteSpace(relativeProject))
            {
                if (!projectTrees.TryGetValue(relativeProject, out var trees))
                {
                    error = $"Project not found: {relativeProject}. Known projects: {string.Join(", ", projectTrees.Keys)}";
                    return false;
                }

                files = trees
                    .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
                    .Select(t => Path.GetRelativePath(repositoryRoot, t.FilePath))
                    .Where(p => !p.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return true;
            }

            if (projectTrees.Count == 1)
            {
                var trees = projectTrees.Values.First();
                files = trees
                    .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
                    .Select(t => Path.GetRelativePath(repositoryRoot, t.FilePath))
                    .Where(p => !p.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return true;
            }

            if (projectTrees.Count == 0)
            {
                files = new List<string>();
                return true;
            }

            error = $"Multiple projects found. Specify 'project': {string.Join(", ", projectTrees.Keys)}";
            return false;
        }

        // Fallback: directory scan.
        var objPath = Path.Combine(repositoryRoot, "obj") + Path.DirectorySeparatorChar;
        files = Directory
            .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(cs => !cs.StartsWith(objPath, StringComparison.OrdinalIgnoreCase))
            .Select(cs => Path.GetRelativePath(repositoryRoot, cs))
            .ToList();
        return true;
    }

    /// <summary>
    /// Returns all members of a named type across all .cs files under
    /// <paramref name="relativeDirectory"/>, excluding <c>obj/</c>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="typeName">Exact type name (case-sensitive).</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="members">Members in source order on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetMembers(
        string repositoryRoot,
        string typeName,
        string relativeDirectory,
        out IReadOnlyList<MemberResult>? members,
        out string? error)
    {
        members = null;
        error = null;

        if (string.IsNullOrWhiteSpace(typeName))
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

        var results = new List<MemberResult>();
        var objPath = absoluteDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

        foreach (var filePath in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.StartsWith(objPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (typeDecl.Identifier.Text != typeName)
                {
                    continue;
                }

                foreach (var member in typeDecl.Members)
                {
                    switch (member)
                    {
                        case FieldDeclarationSyntax field:
                            foreach (var variable in field.Declaration.Variables)
                            {
                                var line = tree.GetLineSpan(variable.Identifier.Span).StartLinePosition.Line + 1;
                                var sig = NormalizeSignature(field.ToFullString());
                                results.Add(new MemberResult(relativeFilePath, line, typeName, "Field", variable.Identifier.Text, sig));
                            }
                            break;

                        case PropertyDeclarationSyntax prop:
                        {
                            var line = tree.GetLineSpan(prop.Identifier.Span).StartLinePosition.Line + 1;
                            var sig = NormalizeSignature(prop.ToFullString());
                            results.Add(new MemberResult(relativeFilePath, line, typeName, "Property", prop.Identifier.Text, sig));
                            break;
                        }

                        case MethodDeclarationSyntax method:
                        {
                            var line = tree.GetLineSpan(method.Identifier.Span).StartLinePosition.Line + 1;
                            var sig = NormalizeSignature(method.ToFullString());
                            results.Add(new MemberResult(relativeFilePath, line, typeName, "Method", method.Identifier.Text, sig));
                            break;
                        }

                        case ConstructorDeclarationSyntax ctor:
                        {
                            var line = tree.GetLineSpan(ctor.Identifier.Span).StartLinePosition.Line + 1;
                            var sig = NormalizeSignature(ctor.ToFullString());
                            results.Add(new MemberResult(relativeFilePath, line, typeName, "Constructor", ctor.Identifier.Text, sig));
                            break;
                        }

                        case EventFieldDeclarationSyntax eventField:
                        {
                            var firstVar = eventField.Declaration.Variables.First();
                            var line = tree.GetLineSpan(firstVar.Identifier.Span).StartLinePosition.Line + 1;
                            var sig = NormalizeSignature(eventField.ToFullString());
                            results.Add(new MemberResult(relativeFilePath, line, typeName, "Event", firstVar.Identifier.Text, sig));
                            break;
                        }
                    }
                }
            }
        }

        members = results;
        return true;
    }

    /// <summary>
    /// Normalizes a member's full text into a single-line signature by truncating at
    /// the first <c>{</c>, <c>=&gt;</c>, or <c>;</c> and collapsing whitespace.
    /// </summary>
    private static string NormalizeSignature(string fullText)
    {
        var end = fullText.Length;
        foreach (var ch in new[] { '{', ';' })
        {
            var idx = fullText.IndexOf(ch);
            if (idx >= 0 && idx < end) end = idx;
        }

        var arrowIdx = fullText.IndexOf("=>", StringComparison.Ordinal);
        if (arrowIdx >= 0 && arrowIdx < end) end = arrowIdx;

        return System.Text.RegularExpressions.Regex.Replace(fullText[..end].Trim(), @"\s+", " ");
    }

    /// <summary>
    /// Finds all types (classes, records, structs) that implement a named interface,
    /// scanning all .cs files under <paramref name="relativeDirectory"/>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="interfaceName">Exact interface name (simple name, case-sensitive).</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="implementors">Matching types on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetInterfaceImplementations(
        string repositoryRoot,
        string interfaceName,
        string relativeDirectory,
        out IReadOnlyList<ImplementorResult>? implementors,
        out string? error)
    {
        implementors = null;
        error = null;

        if (string.IsNullOrWhiteSpace(interfaceName))
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

        var results = new List<ImplementorResult>();
        var objPath = absoluteDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

        foreach (var filePath in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.StartsWith(objPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                // Skip interfaces and enums — only classes, records, structs.
                if (typeDecl is InterfaceDeclarationSyntax)
                {
                    continue;
                }

                if (typeDecl.BaseList is null)
                {
                    continue;
                }

                var implements = typeDecl.BaseList.Types
                    .Select(bt => bt.Type)
                    .Any(t => ExtractSimpleName(t) == interfaceName);

                if (!implements)
                {
                    continue;
                }

                var line = tree.GetLineSpan(typeDecl.Identifier.Span).StartLinePosition.Line + 1;

                var typeKind = typeDecl switch
                {
                    RecordDeclarationSyntax rec =>
                        rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "Record Struct" : "Record",
                    StructDeclarationSyntax => "Struct",
                    _ => "Class"
                };

                results.Add(new ImplementorResult(relativeFilePath, line, typeDecl.Identifier.Text, typeKind));
            }
        }

        implementors = results;
        return true;
    }

    /// <summary>
    /// Extracts the simple (rightmost) identifier from a type name syntax node.
    /// </summary>
    private static string ExtractSimpleName(TypeSyntax type) => type switch
    {
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => string.Empty
    };

    /// <summary>
    /// Builds a call hierarchy rooted at all declarations of <paramref name="methodName"/>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="methodName">Exact method name (case-sensitive).</param>
    /// <param name="direction">"down" for callees, "up" for callers.</param>
    /// <param name="maxDepth">Maximum recursion depth (clamped 1–5).</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="roots">Root nodes on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetCallHierarchy(
        string repositoryRoot,
        string methodName,
        string direction,
        int maxDepth,
        string relativeDirectory,
        out IReadOnlyList<CallHierarchyNode>? roots,
        out string? error)
    {
        roots = null;
        error = null;

        if (string.IsNullOrWhiteSpace(methodName))
        {
            error = "Argument 'name' is required.";
            return false;
        }

        if (direction != "up" && direction != "down")
        {
            error = "Argument 'direction' must be 'up' or 'down'.";
            return false;
        }

        maxDepth = Math.Clamp(maxDepth, 1, 5);

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

        // Pre-scan: build index of all method declarations keyed by method name.
        var objPath = absoluteDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        var index = new Dictionary<string, List<(string file, int line, string containingType, MethodDeclarationSyntax node, SyntaxTree tree)>>(StringComparer.Ordinal);

        foreach (var filePath in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.StartsWith(objPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            var relativeFilePath = Path.GetRelativePath(repositoryRoot, filePath);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var name = method.Identifier.Text;
                var containingType = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.Text ?? string.Empty;
                var lineNum = tree.GetLineSpan(method.Identifier.Span).StartLinePosition.Line + 1;

                if (!index.TryGetValue(name, out var list))
                {
                    list = new List<(string, int, string, MethodDeclarationSyntax, SyntaxTree)>();
                    index[name] = list;
                }

                list.Add((relativeFilePath, lineNum, containingType, method, tree));
            }
        }

        if (!index.TryGetValue(methodName, out var rootDecls) || rootDecls.Count == 0)
        {
            roots = new List<CallHierarchyNode>();
            return true;
        }

        var rootNodes = new List<CallHierarchyNode>();

        if (direction == "down")
        {
            foreach (var (file, line, containingType, node, tree) in rootDecls)
            {
                var visited = new HashSet<string>();
                var key = $"{containingType}.{methodName}@{file}:{line}";
                visited.Add(key);
                var children = BuildCalleeChildren(node, tree, index, repositoryRoot, visited, maxDepth - 1);
                rootNodes.Add(new CallHierarchyNode(file, line, containingType, methodName, children));
            }
        }
        else
        {
            foreach (var (file, line, containingType, _, _) in rootDecls)
            {
                var visited = new HashSet<string>();
                var key = $"{containingType}.{methodName}@{file}:{line}";
                visited.Add(key);
                var callers = BuildCallerChildren(methodName, index, visited, maxDepth - 1);
                rootNodes.Add(new CallHierarchyNode(file, line, containingType, methodName, callers));
            }
        }

        roots = rootNodes;
        return true;
    }

    /// <summary>
    /// Recursively builds callee nodes for a method body.
    /// </summary>
    private static IReadOnlyList<CallHierarchyNode> BuildCalleeChildren(
        MethodDeclarationSyntax method,
        SyntaxTree tree,
        Dictionary<string, List<(string file, int line, string containingType, MethodDeclarationSyntax node, SyntaxTree tree)>> index,
        string repositoryRoot,
        HashSet<string> visited,
        int remainingDepth)
    {
        var children = new List<CallHierarchyNode>();

        if (remainingDepth < 0 || method.Body is null && method.ExpressionBody is null)
        {
            return children;
        }

        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            string calleeName = inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(calleeName) || !index.TryGetValue(calleeName, out var decls))
            {
                continue;
            }

            foreach (var (file, line, containingType, node, calleeTree) in decls)
            {
                var key = $"{containingType}.{calleeName}@{file}:{line}";
                if (visited.Contains(key))
                {
                    children.Add(new CallHierarchyNode(file, line, containingType, calleeName, new List<CallHierarchyNode>()));
                    continue;
                }

                visited.Add(key);
                var grandChildren = BuildCalleeChildren(node, calleeTree, index, repositoryRoot, visited, remainingDepth - 1);
                visited.Remove(key);
                children.Add(new CallHierarchyNode(file, line, containingType, calleeName, grandChildren));
            }
        }

        return children;
    }

    /// <summary>
    /// Recursively builds caller nodes for a method name.
    /// </summary>
    private static IReadOnlyList<CallHierarchyNode> BuildCallerChildren(
        string targetMethod,
        Dictionary<string, List<(string file, int line, string containingType, MethodDeclarationSyntax node, SyntaxTree tree)>> index,
        HashSet<string> visited,
        int remainingDepth)
    {
        var callers = new List<CallHierarchyNode>();

        if (remainingDepth < 0)
        {
            return callers;
        }

        foreach (var (callerName, decls) in index)
        {
            foreach (var (file, line, containingType, node, tree) in decls)
            {
                var callsTarget = node.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        string name = inv.Expression switch
                        {
                            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                            IdentifierNameSyntax id => id.Identifier.Text,
                            _ => string.Empty
                        };
                        return name == targetMethod;
                    });

                if (!callsTarget)
                {
                    continue;
                }

                var key = $"{containingType}.{callerName}@{file}:{line}";
                if (visited.Contains(key))
                {
                    callers.Add(new CallHierarchyNode(file, line, containingType, callerName, new List<CallHierarchyNode>()));
                    continue;
                }

                visited.Add(key);
                var grandCallers = BuildCallerChildren(callerName, index, visited, remainingDepth - 1);
                visited.Remove(key);
                callers.Add(new CallHierarchyNode(file, line, containingType, callerName, grandCallers));
            }
        }

        return callers;
    }

    /// <summary>
    /// Returns the requested line range (1-based, inclusive) from a repository-relative file.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativePath">Repository-relative path to the file.</param>
    /// <param name="startLine">First line to return (1-based).</param>
    /// <param name="endLine">Last line to return (1-based, inclusive).</param>
    /// <param name="text">The requested lines joined by newline on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; otherwise <c>false</c>.</returns>
    public bool TryGetLines(
        string repositoryRoot,
        string relativePath,
        int startLine,
        int endLine,
        out string? text,
        out string? error)
    {
        text = null;
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Argument 'path' is required.";
            return false;
        }

        if (startLine < 1)
        {
            error = "Argument 'start_line' must be >= 1.";
            return false;
        }

        if (endLine < startLine)
        {
            error = "Argument 'end_line' must be >= start_line.";
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

        var lines = File.ReadAllLines(absolutePath);

        if (startLine > lines.Length)
        {
            error = $"start_line {startLine} exceeds file length ({lines.Length} lines).";
            return false;
        }

        var clampedEnd = Math.Min(endLine, lines.Length);
        text = string.Join("\n", lines[(startLine - 1)..clampedEnd]);
        return true;
    }

    /// <summary>
    /// Returns all test methods (decorated with [Fact], [Test], [Theory], or [TestMethod])
    /// across all .cs files under <paramref name="relativeDirectory"/>.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="results">Test method results in source order on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; <c>false</c> with <paramref name="error"/> set on failure.</returns>
    public bool TryFindTestMethods(
        string repositoryRoot,
        string relativeDirectory,
        out IReadOnlyList<TestMethodResult>? results,
        out string? error)
    {
        results = null;
        error = null;

        var absoluteDir = Path.GetFullPath(Path.Combine(repositoryRoot, relativeDirectory));

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

        var testAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Fact", "Test", "Theory", "TestMethod"
        };

        var output = new List<TestMethodResult>();

        foreach (var file in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var relPath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code, path: file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var hasTestAttr = method.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(a => testAttributeNames.Contains(ExtractSimpleName(a.Name)));

                if (!hasTestAttr) continue;

                var containingType = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.Text ?? "<global>";

                var line = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
                output.Add(new TestMethodResult(relPath, line, containingType, method.Identifier.Text));
            }
        }

        results = output;
        return true;
    }

    /// <summary>
    /// Returns the XML doc comment for every declaration of a named symbol found under
    /// <paramref name="relativeDirectory"/>. Multiple results occur for overloads and partial types.
    /// </summary>
    /// <param name="repositoryRoot">Absolute session root path.</param>
    /// <param name="symbolName">Exact symbol name (case-sensitive).</param>
    /// <param name="relativeDirectory">Repository-relative directory to scan.</param>
    /// <param name="results">Doc comment results on success.</param>
    /// <param name="error">Error details on failure.</param>
    /// <returns><c>true</c> on success; <c>false</c> with <paramref name="error"/> set on failure.</returns>
    public bool TryGetXmlDoc(
        string repositoryRoot,
        string symbolName,
        string relativeDirectory,
        out IReadOnlyList<XmlDocResult>? results,
        out string? error)
    {
        results = null;
        error = null;

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            error = "Argument 'name' is required.";
            return false;
        }

        var absoluteDir = Path.GetFullPath(Path.Combine(repositoryRoot, relativeDirectory));

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

        var output = new List<XmlDocResult>();

        foreach (var file in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var relPath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code, path: file);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                string? name = node switch
                {
                    MethodDeclarationSyntax m => m.Identifier.Text,
                    PropertyDeclarationSyntax p => p.Identifier.Text,
                    FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
                    EventDeclarationSyntax e => e.Identifier.Text,
                    TypeDeclarationSyntax t => t.Identifier.Text,
                    ConstructorDeclarationSyntax c => c.Identifier.Text,
                    _ => null
                };

                if (name != symbolName) continue;

                var docTrivia = node.GetLeadingTrivia()
                    .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                             || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                    .ToList();

                if (docTrivia.Count == 0) continue;

                var docText = string.Concat(docTrivia.Select(t => t.ToFullString())).Trim();
                var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                output.Add(new XmlDocResult(relPath, line, name, docText));
            }
        }

        results = output;
        return true;
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
