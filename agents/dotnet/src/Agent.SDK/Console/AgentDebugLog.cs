namespace Agent.SDK.Console;

/// <summary>
/// Buffered debug log for streaming LLM traces. Writes
/// <c>agent_streaming_debug.log</c> in the output directory. Rotates the
/// previous log when it exceeds 10 MB.
/// </summary>
public static class AgentDebugLog
{
    private static readonly AgentFileLog Log = new(
        fileName: "agent_streaming_debug.log",
        headerLabel: "Streaming Debug Log",
        autoFlush: false,
        maxBytesBeforeRotation: 10 * 1024 * 1024);

    public static Task InitAsync(string outputDirectory) => Log.InitializeAsync(outputDirectory);

    public static Task WriteAsync(string text) => Log.WriteAsync(text);

    public static Task FlushAsync() => Log.FlushAsync();

    public static ValueTask CloseAsync() => Log.DisposeAsync();
}
