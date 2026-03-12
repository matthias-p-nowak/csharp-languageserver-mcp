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

    /// <summary>
    /// Creates a server bound to one repository root.
    /// </summary>
    /// <param name="repositoryRoot">Absolute repository root path.</param>
    public McpServer(string repositoryRoot)
    {
        inspector = new RoslynInspector(Path.GetFullPath(repositoryRoot));
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Processes MCP JSON-RPC requests until input ends or cancellation is requested.
    /// </summary>
    /// <param name="input">Input stream carrying MCP messages.</param>
    /// <param name="output">Output stream for MCP responses.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ReadMessageWithFramingAsync(reader, cancellationToken);
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
                await WriteMessageAsync(output, parseError, cancellationToken);
                continue;
            }

            using (document)
            {
                var response = HandleRequest(document.RootElement);
                if (response is not null)
                {
                    await WriteMessageAsync(output, response, cancellationToken);
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
                    tools = new[]
                    {
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

        if (!string.Equals(toolName, "roslyn_syntax_summary", StringComparison.Ordinal))
        {
            return CreateToolError($"Unknown tool: {toolName}");
        }

        if (!TryGetStringProperty(request, out var path, "params", "arguments", "path") || string.IsNullOrWhiteSpace(path))
        {
            return CreateToolError("Missing required argument: path");
        }

        if (!inspector.TrySummarize(path, out var summary, out var error))
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

    private async Task<string?> ReadMessageWithFramingAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? firstLine;
        do
        {
            firstLine = await reader.ReadLineAsync(cancellationToken);
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
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
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
            cancellationToken.ThrowIfCancellationRequested();
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

    private async Task WriteMessageAsync(Stream output, object response, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(response, jsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (framingMode == MessageFramingMode.LineDelimitedJson)
        {
            var lineBytes = Encoding.UTF8.GetBytes(payload + "\n");
            await output.WriteAsync(lineBytes, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return;
        }

        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payloadBytes.Length}\r\n\r\n");
        await output.WriteAsync(headerBytes, cancellationToken);
        await output.WriteAsync(payloadBytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

}
