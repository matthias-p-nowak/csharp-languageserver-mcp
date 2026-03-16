using System.Reflection;
using System.Text.Json;
using Rosalyn.Server;
using Xunit;

namespace Rosalyn.Server.Tests;

/// <summary>
/// Unit tests for MCP server request handling.
/// </summary>
public sealed class McpServerTests
{
    /// <summary>
    /// Verifies that set_root returns an empty structured tool result that the client bridge can parse.
    /// </summary>
    [Fact]
    public void HandleRequest_SetRoot_ReturnsEmptyStructuredToolResult()
    {
        var repositoryRoot = CreateTempRoot();
        try
        {
            var serverType = typeof(RoslynInspector).Assembly.GetType("Rosalyn.Server.McpServer", throwOnError: true)!;
            var server = Activator.CreateInstance(serverType, [new[] { repositoryRoot }]);
            Assert.NotNull(server);

            using var request = JsonDocument.Parse(
                $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"set_root\",\"arguments\":{{\"path\":\"{EscapeJson(repositoryRoot)}\"}}}}}}");

            var handleRequest = serverType.GetMethod("HandleRequest", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(handleRequest);

            var response = handleRequest!.Invoke(server, [request.RootElement]);
            Assert.NotNull(response);

            using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(response));
            var root = responseJson.RootElement;

            Assert.True(root.TryGetProperty("result", out var result));
            Assert.True(result.TryGetProperty("structuredContent", out var structuredContent));
            Assert.Equal(JsonValueKind.Object, structuredContent.ValueKind);
            Assert.Empty(structuredContent.EnumerateObject());

            Assert.True(result.TryGetProperty("content", out var content));
            Assert.Equal(JsonValueKind.Array, content.ValueKind);
            Assert.Empty(content.EnumerateArray());
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Escapes a filesystem path for embedding in JSON.
    /// </summary>
    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates an isolated temporary directory for a test case.
    /// </summary>
    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "rosalyn-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
