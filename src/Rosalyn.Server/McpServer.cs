using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rosalyn.Server;

/// <summary>
/// Minimal MCP server implementation over stdio using JSON-RPC messages.
/// </summary>
internal sealed class McpServer
{
    private enum MessageFramingMode
    {
        Unknown = 0,
        ContentLength = 1,
        LineDelimitedJson = 2
    }

    private const string HighestSupportedMcpProtocolVersion = "2025-11-25";
    private static readonly DateOnly HighestSupportedMcpProtocolDate =
        DateOnly.ParseExact(HighestSupportedMcpProtocolVersion, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private readonly RoslynInspector inspector;
    private readonly JsonSerializerOptions jsonOptions;
    private MessageFramingMode framingMode;
    private bool exitRequested;
    private string? sessionRoot;

    /// <summary>
    /// Creates a server with a list of allowed directories.
    /// </summary>
    /// <param name="allowedDirectories">Absolute paths the server may access.</param>
    public McpServer(string[] allowedDirectories)
    {
        inspector = new RoslynInspector(allowedDirectories);
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Processes MCP JSON-RPC requests until input ends.
    /// </summary>
    /// <param name="input">Input stream carrying MCP messages.</param>
    /// <param name="output">Output stream for MCP responses.</param>
    public async Task RunAsync(Stream input, Stream output)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (true)
        {
            var payload = await ReadMessageWithFramingAsync(reader);
            if (payload is null)
            {
                return;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload);
            }
            catch
            {
                var parseError = CreateErrorResponse(id: null, -32700, "Invalid JSON payload.");
                await WriteMessageAsync(output, parseError);
                continue;
            }

            using (document)
            {
                var response = HandleRequest(document.RootElement);
                if (response is not null)
                {
                    await WriteMessageAsync(output, response);
                }

                if (exitRequested)
                {
                    return;
                }
            }
        }
    }

    private object? HandleRequest(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return CreateErrorResponse(GetId(request), -32600, "Missing JSON-RPC method.");
        }

        var method = methodElement.GetString() ?? string.Empty;
        var id = GetId(request);

        // Notifications have no id and do not receive responses.
        if (id is null)
        {
            HandleNotification(method);
            return null;
        }

        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            if (!TryNegotiateProtocolVersion(request, out var protocolVersion, out var protocolError, out var requestedVersion))
            {
                return CreateErrorResponse(id, -32602, protocolError);
            }

            return CreateResultResponse(
                id.Value,
                new
                {
                    protocolVersion,
                    serverInfo = new
                    {
                        name = "rosalyn-server",
                        version = "0.1.0"
                    },
                    capabilities = new
                    {
                        tools = new
                        {
                            listChanged = false
                        }
                    }
                });
        }

        if (string.Equals(method, "initialized", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new { });
        }

        if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new { });
        }

        if (string.Equals(method, "server/capabilities", StringComparison.Ordinal))
        {
            return CreateResultResponse(
                id.Value,
                new
                {
                    protocolVersion = HighestSupportedMcpProtocolVersion,
                    serverInfo = new
                    {
                        name = "rosalyn-server",
                        version = "0.1.0"
                    },
                    capabilities = new
                    {
                        tools = new { }
                    }
                });
        }

        if (string.Equals(method, "shutdown", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new { });
        }

        if (string.Equals(method, "exit", StringComparison.Ordinal))
        {
            exitRequested = true;
            return CreateResultResponse(id.Value, new { });
        }

        if (string.Equals(method, "resources/list", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new
            {
                resources = Array.Empty<object>()
            });
        }

        if (string.Equals(method, "resources/templates/list", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new
            {
                resourceTemplates = Array.Empty<object>()
            });
        }

        if (string.Equals(method, "tools/list", StringComparison.Ordinal))
        {
            return CreateResultResponse(
                id.Value,
                new
                {
                    tools = new object[]
                    {
                        new
                        {
                            name = "set_root",
                            description = "Set the workspace root for this session. Must be called before any analysis tool.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new
                                    {
                                        type = "string",
                                        description = "Absolute path to the workspace root."
                                    }
                                },
                                required = new[] { "path" }
                            }
                        },
                        new
                        {
                            name = "roslyn_syntax_summary",
                            description = "Return Roslyn syntax declaration counts for a C# file.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new
                                    {
                                        type = "string",
                                        description = "Repository-relative path to a .cs file."
                                    }
                                },
                                required = new[] { "path" }
                            }
                        },
                        new
                        {
                            name = "find_symbol",
                            description = "Search for a named symbol across all .cs files under a directory. Returns file, line, and kind for each match.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new
                                    {
                                        type = "string",
                                        description = "Exact symbol name to search for (case-sensitive)."
                                    },
                                    directory = new
                                    {
                                        type = "string",
                                        description = "Repository-relative directory to scan."
                                    }
                                },
                                required = new[] { "name", "directory" }
                            }
                        },
                        new
                        {
                            name = "get_document_symbols",
                            description = "Return all named symbols in a single .cs file with their kind, name, and line number.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new
                                    {
                                        type = "string",
                                        description = "Repository-relative path to a .cs file."
                                    }
                                },
                                required = new[] { "path" }
                            }
                        },
                        new
                        {
                            name = "roslyn_complexity_report",
                            description = "Scan all .cs files under a directory and return the top N methods ranked by cyclomatic complexity.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    directory = new
                                    {
                                        type = "string",
                                        description = "Repository-relative path to the directory to scan."
                                    },
                                    top_n = new
                                    {
                                        type = "integer",
                                        description = "Maximum number of results to return (default: 20)."
                                    }
                                },
                                required = new[] { "directory" }
                            }
                        },
                        new
                        {
                            name = "get_project_for_file",
                            description = "Return the .csproj project(s) that own a given .cs file. Useful before invoking tools that accept an optional 'project' argument.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new { type = "string", description = "Repository-relative path to the .cs file." }
                                },
                                required = new[] { "path" }
                            }
                        },
                        new
                        {
                            name = "find_references",
                            description = "Find all usage sites of a named symbol across a project. Requires semantic compilation.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Exact symbol name to find references for." },
                                    project = new { type = "string", description = "Repository-relative .csproj path. Optional when only one project exists." }
                                },
                                required = new[] { "name" }
                            }
                        },
                        new
                        {
                            name = "get_symbol_definition",
                            description = "Return the definition site of the symbol at a given file and line. Requires semantic compilation.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new { type = "string", description = "Repository-relative path to the .cs file." },
                                    line = new { type = "integer", description = "1-based line number." },
                                    project = new { type = "string", description = "Repository-relative .csproj path. Optional when only one project exists." }
                                },
                                required = new[] { "path", "line" }
                            }
                        },
                        new
                        {
                            name = "get_semantic_diagnostics",
                            description = "Return compiler errors and warnings for a .cs file or whole project. Requires semantic compilation.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new { type = "string", description = "Repository-relative .cs file path. Optional; omit for project-wide diagnostics." },
                                    project = new { type = "string", description = "Repository-relative .csproj path. Optional when only one project exists." }
                                },
                                required = Array.Empty<string>()
                            }
                        },
                        new
                        {
                            name = "get_method_body",
                            description = "Return the full source text of a named method with its file path and start/end line numbers. Syntax-only. Returns all overloads or same-named methods across files.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Exact method name to find (case-sensitive)." },
                                    path = new { type = "string", description = "Repository-relative .cs file to restrict search to. Optional." },
                                    directory = new { type = "string", description = "Repository-relative directory to scan. Ignored when 'path' is provided. Defaults to '.'." }
                                },
                                required = new[] { "name" }
                            }
                        },
                        new
                        {
                            name = "get_namespace_for_file",
                            description = "Return the namespace names declared in a repository-relative .cs file. Returns an empty list when no namespace is declared.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    path = new { type = "string", description = "Repository-relative path to a .cs file." }
                                },
                                required = new[] { "path" }
                            }
                        },
                        new
                        {
                            name = "list_source_files",
                            description = "Return repository-relative paths for all C# source files. Uses loaded project trees when available; falls back to a directory scan otherwise.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    project = new { type = "string", description = "Optional relative .csproj path. Required when multiple projects are loaded." }
                                },
                                required = Array.Empty<string>()
                            }
                        },
                        new
                        {
                            name = "get_members",
                            description = "Return all members (fields, properties, methods, constructors, events) of a named type across .cs files under a directory.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Exact type name (case-sensitive)." },
                                    directory = new { type = "string", description = "Repository-relative directory to scan." }
                                },
                                required = new[] { "name", "directory" }
                            }
                        },
                        new
                        {
                            name = "get_interface_implementations",
                            description = "Find all classes, records, and structs that implement a named interface, across .cs files under a directory.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Exact interface name (simple name, case-sensitive)." },
                                    directory = new { type = "string", description = "Repository-relative directory to scan." }
                                },
                                required = new[] { "name", "directory" }
                            }
                        },
                        new
                        {
                            name = "get_call_hierarchy",
                            description = "Build a call hierarchy (callers or callees) for a named method across .cs files under a directory.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Exact method name (case-sensitive)." },
                                    direction = new { type = "string", description = "up or down, default down" },
                                    max_depth = new { type = "integer", description = "1-5, default 2" },
                                    directory = new { type = "string", description = "Repository-relative directory to scan." }
                                },
                                required = new[] { "name", "directory" }
                            }
                        }
                    }
                });
        }

        if (string.Equals(method, "ping", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, new { });
        }

        if (string.Equals(method, "tools/call", StringComparison.Ordinal))
        {
            return CreateResultResponse(id.Value, CallTool(request));
        }

        return CreateErrorResponse(id, -32601, $"Unknown method: {method}");
    }

    /// <summary>
    /// Handles notification methods that do not carry an id.
    /// </summary>
    private void HandleNotification(string method)
    {
        if (string.Equals(method, "initialized", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(method, "shutdown", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(method, "exit", StringComparison.Ordinal))
        {
            exitRequested = true;
        }
    }

    /// <summary>
    /// Negotiates MCP protocol version using initialize params.
    /// </summary>
    private static bool TryNegotiateProtocolVersion(
        JsonElement request,
        out string negotiatedVersion,
        out string errorMessage,
        out string? requestedVersion)
    {
        negotiatedVersion = HighestSupportedMcpProtocolVersion;
        errorMessage = string.Empty;
        requestedVersion = null;

        if (!request.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!parameters.TryGetProperty("protocolVersion", out var protocolVersionElement))
        {
            return true;
        }

        if (protocolVersionElement.ValueKind != JsonValueKind.String)
        {
            errorMessage = "Invalid initialize params: 'protocolVersion' must be a non-empty string.";
            return false;
        }

        requestedVersion = protocolVersionElement.GetString();
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            errorMessage = "Invalid initialize params: 'protocolVersion' must be a non-empty string.";
            return false;
        }

        if (!DateOnly.TryParseExact(
                requestedVersion,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var requestedDate))
        {
            errorMessage = "Invalid initialize params: 'protocolVersion' must use yyyy-MM-dd format.";
            return false;
        }

        if (requestedDate > HighestSupportedMcpProtocolDate)
        {
            errorMessage =
                $"Unsupported protocolVersion '{requestedVersion}'. Highest supported version: {HighestSupportedMcpProtocolVersion}.";
            return false;
        }

        negotiatedVersion = requestedVersion;
        return true;
    }

    private object CallTool(JsonElement request)
    {
        if (!TryGetStringProperty(request, out var toolName, "params", "name") || string.IsNullOrWhiteSpace(toolName))
        {
            return CreateToolError("Missing tool name.");
        }

        if (string.Equals(toolName, "set_root", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var rootPath, "params", "arguments", "path") || string.IsNullOrWhiteSpace(rootPath))
            {
                return CreateToolError("Missing required argument: path");
            }

            var absoluteRoot = Path.GetFullPath(rootPath);
            if (!inspector.IsWithinAllowedDirectory(absoluteRoot))
            {
                return CreateToolError("The provided path is not within any allowed directory.");
            }

            sessionRoot = absoluteRoot;
            inspector.LoadProjects(absoluteRoot);
            return new { isError = false, content = Array.Empty<object>() };
        }

        if (sessionRoot is null)
        {
            return CreateToolError("Root not set. Call set_root before using tools.");
        }

        if (string.Equals(toolName, "roslyn_syntax_summary", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
            {
                return CreateToolError("Missing required argument: path");
            }

            if (!inspector.TrySummarize(sessionRoot, path, out var summary, out var error))
            {
                return CreateToolError(error ?? "Unknown Roslyn analysis error.");
            }

            return new
            {
                isError = false,
                structuredContent = summary,
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(summary, jsonOptions)
                    }
                }
            };
        }

        if (string.Equals(toolName, "roslyn_complexity_report", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var directory, "params", "arguments", "directory") || string.IsNullOrWhiteSpace(directory))
            {
                return CreateToolError("Missing required argument: directory");
            }

            var topN = 20;
            if (request.TryGetProperty("params", out var p) &&
                p.TryGetProperty("arguments", out var args) &&
                args.TryGetProperty("top_n", out var topNElement) &&
                topNElement.ValueKind == JsonValueKind.Number &&
                topNElement.TryGetInt32(out var topNParsed) &&
                topNParsed > 0)
            {
                topN = topNParsed;
            }

            if (!inspector.TryAnalyzeComplexity(sessionRoot, directory, topN, out var results, out var error))
            {
                return CreateToolError(error ?? "Unknown complexity analysis error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { results },
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(new { results }, jsonOptions)
                    }
                }
            };
        }

        if (string.Equals(toolName, "find_symbol", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
            {
                return CreateToolError("Missing required argument: name");
            }

            if (!TryGetStringProperty(request, out var directory, "params", "arguments", "directory") || string.IsNullOrWhiteSpace(directory))
            {
                return CreateToolError("Missing required argument: directory");
            }

            if (!inspector.TryFindSymbol(sessionRoot, name, directory, out var matches, out var error))
            {
                return CreateToolError(error ?? "Unknown symbol search error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { matches },
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(new { matches }, jsonOptions)
                    }
                }
            };
        }

        if (string.Equals(toolName, "get_document_symbols", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
            {
                return CreateToolError("Missing required argument: path");
            }

            if (!inspector.TryGetDocumentSymbols(sessionRoot, path, out var symbols, out var error))
            {
                return CreateToolError(error ?? "Unknown document symbols error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { symbols },
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(new { symbols }, jsonOptions)
                    }
                }
            };
        }

        if (string.Equals(toolName, "get_project_for_file", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
            {
                return CreateToolError("Missing required argument: path");
            }

            if (!inspector.TryGetProjectForFile(sessionRoot, path, out var projects, out var error))
            {
                return CreateToolError(error ?? "Unknown get_project_for_file error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { projects },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { projects }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_method_body", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
                return CreateToolError("Missing required argument: name");
            TryGetStringProperty(request, out var mbPath, "params", "arguments", "path");
            TryGetStringProperty(request, out var mbDir, "params", "arguments", "directory");
            var directory = string.IsNullOrWhiteSpace(mbDir) ? "." : mbDir;
            if (!inspector.TryGetMethodBody(sessionRoot, name, mbPath, directory, out var methods, out var error))
                return CreateToolError(error ?? "Unknown get_method_body error.");
            return new { isError = false, structuredContent = new { methods },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { methods }, jsonOptions) } } };
        }

        if (string.Equals(toolName, "find_references", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
            {
                return CreateToolError("Missing required argument: name");
            }

            TryGetStringProperty(request, out var project, "params", "arguments", "project");

            if (!inspector.TryFindReferences(sessionRoot, name, project, out var refs, out var error))
            {
                return CreateToolError(error ?? "Unknown find_references error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { references = refs },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { references = refs }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_symbol_definition", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
            {
                return CreateToolError("Missing required argument: path");
            }

            var line = 0;
            if (request.TryGetProperty("params", out var p2) &&
                p2.TryGetProperty("arguments", out var args2) &&
                args2.TryGetProperty("line", out var lineEl) &&
                lineEl.ValueKind == JsonValueKind.Number &&
                lineEl.TryGetInt32(out var parsedLine))
            {
                line = parsedLine;
            }

            if (line <= 0)
            {
                return CreateToolError("Missing or invalid required argument: line");
            }

            TryGetStringProperty(request, out var project, "params", "arguments", "project");

            if (!inspector.TryGetSymbolDefinition(sessionRoot, path, line, project, out var definition, out var error))
            {
                return CreateToolError(error ?? "Unknown get_symbol_definition error.");
            }

            return new
            {
                isError = false,
                structuredContent = definition,
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(definition, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_semantic_diagnostics", StringComparison.Ordinal))
        {
            TryGetStringProperty(request, out var path, "params", "arguments", "path");
            TryGetStringProperty(request, out var project, "params", "arguments", "project");

            if (!inspector.TryGetSemanticDiagnostics(sessionRoot, path, project, out var diagnostics, out var error))
            {
                return CreateToolError(error ?? "Unknown get_semantic_diagnostics error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { diagnostics },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { diagnostics }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_namespace_for_file", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
            {
                return CreateToolError("Missing required argument: path");
            }

            if (!inspector.TryGetNamespacesForFile(sessionRoot, path, out var namespaces, out var error))
            {
                return CreateToolError(error ?? "Unknown get_namespace_for_file error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { namespaces },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { namespaces }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "list_source_files", StringComparison.Ordinal))
        {
            TryGetStringProperty(request, out var project, "params", "arguments", "project");

            if (!inspector.TryListSourceFiles(sessionRoot, project, out var files, out var error))
            {
                return CreateToolError(error ?? "Unknown list_source_files error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { files },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { files }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_members", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
            {
                return CreateToolError("Missing required argument: name");
            }

            if (!TryGetStringProperty(request, out var directory, "params", "arguments", "directory") || string.IsNullOrWhiteSpace(directory))
            {
                return CreateToolError("Missing required argument: directory");
            }

            if (!inspector.TryGetMembers(sessionRoot, name, directory, out var members, out var error))
            {
                return CreateToolError(error ?? "Unknown get_members error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { members },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { members }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_interface_implementations", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
            {
                return CreateToolError("Missing required argument: name");
            }

            if (!TryGetStringProperty(request, out var directory, "params", "arguments", "directory") || string.IsNullOrWhiteSpace(directory))
            {
                return CreateToolError("Missing required argument: directory");
            }

            if (!inspector.TryGetInterfaceImplementations(sessionRoot, name, directory, out var implementors, out var error))
            {
                return CreateToolError(error ?? "Unknown get_interface_implementations error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { implementors },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { implementors }, jsonOptions) } }
            };
        }

        if (string.Equals(toolName, "get_call_hierarchy", StringComparison.Ordinal))
        {
            if (!TryGetStringProperty(request, out var name, "params", "arguments", "name") || string.IsNullOrWhiteSpace(name))
            {
                return CreateToolError("Missing required argument: name");
            }

            if (!TryGetStringProperty(request, out var directory, "params", "arguments", "directory") || string.IsNullOrWhiteSpace(directory))
            {
                return CreateToolError("Missing required argument: directory");
            }

            TryGetStringProperty(request, out var directionArg, "params", "arguments", "direction");
            var direction = string.IsNullOrWhiteSpace(directionArg) ? "down" : directionArg;

            var maxDepth = 2;
            if (request.TryGetProperty("params", out var chP) &&
                chP.TryGetProperty("arguments", out var chArgs) &&
                chArgs.TryGetProperty("max_depth", out var mdProp) &&
                mdProp.ValueKind == JsonValueKind.Number)
            {
                maxDepth = Math.Clamp(mdProp.GetInt32(), 1, 5);
            }

            if (!inspector.TryGetCallHierarchy(sessionRoot, name, direction, maxDepth, directory, out var nodes, out var error))
            {
                return CreateToolError(error ?? "Unknown get_call_hierarchy error.");
            }

            return new
            {
                isError = false,
                structuredContent = new { nodes },
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { nodes }, jsonOptions) } }
            };
        }

        return CreateToolError($"Unknown tool: {toolName}");
    }

    private static JsonElement? GetId(JsonElement request)
    {
        if (!request.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        return idElement.Clone();
    }

    private static bool TryGetStringProperty(JsonElement root, out string? value, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                value = null;
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            value = null;
            return false;
        }

        value = current.GetString();
        return true;
    }

    private static object CreateToolError(string message)
    {
        return new
        {
            isError = true,
            content = new[]
            {
                new
                {
                    type = "text",
                    text = message
                }
            }
        };
    }

    private static object CreateResultResponse(JsonElement id, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result
        };
    }

    private static object CreateErrorResponse(JsonElement? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message
            }
        };
    }

    private async Task<string?> ReadMessageWithFramingAsync(StreamReader reader)
    {
        string? firstLine;
        do
        {
            firstLine = await reader.ReadLineAsync();
        }
        while (firstLine is not null && firstLine.Length == 0);

        if (firstLine is null)
        {
            return null;
        }

        var trimmed = firstLine.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            framingMode = MessageFramingMode.LineDelimitedJson;
            return firstLine;
        }

        framingMode = MessageFramingMode.ContentLength;
        var contentLength = 0;
        if (TryParseContentLength(firstLine, out var parsedContentLength))
        {
            contentLength = parsedContentLength;
        }

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Length == 0)
            {
                break;
            }

            if (TryParseContentLength(line, out parsedContentLength))
            {
                contentLength = parsedContentLength;
            }
        }

        if (line is null || contentLength <= 0)
        {
            return null;
        }

        var buffer = new char[contentLength];
        var offset = 0;

        while (offset < contentLength)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(offset, contentLength - offset));
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return new string(buffer);
    }

    private static bool TryParseContentLength(string line, out int contentLength)
    {
        contentLength = 0;
        if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawLength = line["Content-Length:".Length..].Trim();
        return int.TryParse(rawLength, out contentLength) && contentLength > 0;
    }

    private async Task WriteMessageAsync(Stream output, object response)
    {
        var payload = JsonSerializer.Serialize(response, jsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (framingMode == MessageFramingMode.LineDelimitedJson)
        {
            var lineBytes = Encoding.UTF8.GetBytes(payload + "\n");
            await output.WriteAsync(lineBytes);
            await output.FlushAsync();
            return;
        }

        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payloadBytes.Length}\r\n\r\n");
        await output.WriteAsync(headerBytes);
        await output.WriteAsync(payloadBytes);
        await output.FlushAsync();
    }

}
