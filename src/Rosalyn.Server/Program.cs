using System.Text;

namespace Rosalyn.Server;

/// <summary>
/// Application entrypoint for the Rosalyn MCP server.
/// </summary>
internal static class Program
{
    private const string RelativeLogPath = "tmp/rosalyn/new-rosalyn.log";
    private const string FallbackLogPath = "/tmp/rosalyn/new-rosalyn.log";
    private static readonly object LogLock = new();

    /// <summary>
    /// Runs the MCP server over standard input and output.
    /// </summary>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var root = ParseRootArg(args) ?? Directory.GetCurrentDirectory();
        Log(
            $"startup cwd='{Directory.GetCurrentDirectory()}' root='{root}' inputRedirected={Console.IsInputRedirected} outputRedirected={Console.IsOutputRedirected} errorRedirected={Console.IsErrorRedirected}");

        try
        {
            var server = new McpServer(root);
            await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), cancellation.Token);
            Log("server exited normally");
            return 0;
        }
        catch (Exception ex)
        {
            Log("fatal server exception", ex);
            return 1;
        }
    }

    /// <summary>
    /// Returns the value of --root &lt;path&gt; from args, or null if not provided.
    /// </summary>
    private static string? ParseRootArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--root")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Appends a diagnostic log entry to ~/tmp/rosalyn/new-rosalyn.log.
    /// </summary>
    private static void Log(string message, Exception? exception = null)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" pid=")
            .Append(Environment.ProcessId)
            .Append(' ')
            .Append(message);

        if (exception is not null)
        {
            line.Append(" :: ").Append(exception);
        }

        lock (LogLock)
        {
            var primaryPath = ResolveLogPath();
            if (TryAppendLog(primaryPath, line.ToString()))
            {
                return;
            }

            TryAppendLog(FallbackLogPath, $"{line} (fallback from '{primaryPath}')");
        }
    }

    /// <summary>
    /// Resolves ~/tmp/rosalyn/new-rosalyn.log into an absolute path.
    /// </summary>
    private static string ResolveLogPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = "/tmp";
        }

        return Path.Combine(home, RelativeLogPath);
    }

    /// <summary>
    /// Appends one line to a log file and suppresses filesystem errors.
    /// </summary>
    private static bool TryAppendLog(string path, string line)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            File.AppendAllText(path, line + Environment.NewLine);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
