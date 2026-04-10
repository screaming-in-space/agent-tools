namespace Agent.SDK.Console;

/// <summary>
/// Buffered debug log for streaming LLM traces. Writes
/// <c>streaming-debug.log</c> in the output directory. Rotates the
/// previous log when it exceeds 10 MB.
/// </summary>
public sealed class AgentDebugLog : AgentFileLog
{
    private static readonly AgentDebugLog Instance = new();

    protected override string FileName => "agent_streaming_debug.log";
    protected override string HeaderLabel => "Streaming Debug Log";
    protected override bool AutoFlush => false;
    protected override long MaxBytesBeforeRotation => 10 * 1024 * 1024;

    public static Task InitAsync(string outputDirectory) => Instance.InitializeAsync(outputDirectory);

    public static void Write(string text) => Instance.WriteDirect(text);

    public static void Flush() => Instance.FlushDirect();

    public static ValueTask CloseAsync() => Instance.DisposeAsync();
}
