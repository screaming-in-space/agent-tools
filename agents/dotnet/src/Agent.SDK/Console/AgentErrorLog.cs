namespace Agent.SDK.Console;

/// <summary>
/// Persistent error log capturing tool failures, scanner errors, and
/// fallback rejections. Writes <c>agent_error.log</c> in the output directory.
/// </summary>
public sealed class AgentErrorLog : AgentFileLog
{
    private static readonly AgentErrorLog Instance = new();

    protected override string FileName => "agent_error.log";
    protected override string HeaderLabel => "Agent Error Log";
    protected override bool AutoFlush => true;

    public static Task InitAsync(string outputDirectory) => Instance.InitializeAsync(outputDirectory);

    public static async Task LogAsync(string scanner, string message)
    {
        await Instance.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{scanner}] {message}").ConfigureAwait(false);
    }

    public static async Task LogAsync(string scanner, string message, Exception ex)
    {
        await Instance.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{scanner}] {message}").ConfigureAwait(false);
        await Instance.WriteLineAsync($"  {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
    }

    public static ValueTask CloseAsync() => Instance.DisposeAsync();
}
