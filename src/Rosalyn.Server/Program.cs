namespace Rosalyn.Server;

/// <summary>
/// Application entrypoint for the Rosalyn MCP server.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the MCP server over standard input and output.
    /// </summary>
    /// <param name="args">List of allowed absolute directory paths.</param>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        var allowedDirs = args.Select(Path.GetFullPath).ToArray();

        try
        {
            var server = new McpServer(allowedDirs);
            await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
