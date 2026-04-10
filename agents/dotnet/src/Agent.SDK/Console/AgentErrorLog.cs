namespace Agent.SDK.Console;

/// <summary>
/// Persistent error log capturing tool failures, scanner errors, and
/// fallback rejections. Writes <c>agent_error.log</c> in the output directory.
/// </summary>
public static class AgentErrorLog
{
    private static readonly AgentFileLog Log = new(
        fileName: "agent_error.log",
        headerLabel: "Agent Error Log",
        autoFlush: true);

    public static Task InitAsync(string outputDirectory) => Log.InitializeAsync(outputDirectory);

    public static async Task LogAsync(string scanner, string message)
    {
        await Log.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{scanner}] {message}").ConfigureAwait(false);
    }

    public static async Task LogAsync(string scanner, string message, Exception ex)
    {
        await Log.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{scanner}] {message}").ConfigureAwait(false);
        await Log.WriteLineAsync($"  {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
    }

    public static ValueTask CloseAsync() => Log.DisposeAsync();
}
