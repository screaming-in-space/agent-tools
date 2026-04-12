namespace Agent.SDK.Console;

// ── Data ───────────────────────────────────────────────────────────────

/// <summary>End-of-run summary data rendered by <see cref="IAgentOutput.StopAsync"/>.</summary>
public sealed record AgentRunSummary(
    int ToolCallCount,
    int FilesProcessed,
    TimeSpan Duration,
    string OutputPath,
    string FullOutputPath,
    bool Success);

/// <summary>Lightweight accuracy check result for UI display (SDK-level, no ModelBoss dependency).</summary>
public sealed record TestCheckResult(string Name, double Score, string Detail);

// ── Interface ──────────────────────────────────────────────────────────

/// <summary>
/// Agent terminal output abstraction. Interactive mode uses Spectre.Console
/// live rendering; headless mode passes through to Serilog.
/// </summary>
public interface IAgentOutput : IDisposable
{
    bool IsInteractive { get; }
    int ToolCallCount { get; }

    Task StartAsync(string agentName, CancellationToken ct = default);
    Task UpdateStatusAsync(string status);
    Task ScannerStartedAsync(string scannerName, string modelName);
    Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success);
    Task ScannerSkippedAsync(string scannerName, string reason);
    Task ToolStartedAsync(string toolName, string? detail = null);
    Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true);
    Task AppendThinkingAsync(string token);

    /// <summary>Reports a per-prompt benchmark metric (tok/s and accuracy) for the active scanner.</summary>
    Task ReportPromptResultAsync(string promptName, double tokensPerSecond, double accuracyScore);

    Task StopAsync(AgentRunSummary summary, CancellationToken ct = default);
    Task WriteResponseAsync(string text);

    // ── V2: Benchmark lifecycle (default implementations for backward compatibility) ──

    /// <summary>Reports that a benchmark test is starting.</summary>
    Task ReportTestStartedAsync(string promptName, string category, string description, string modelId)
        => Task.CompletedTask;

    /// <summary>Reports that a benchmark test has completed with results.</summary>
    Task ReportTestCompletedAsync(string promptName, double tokensPerSecond, TimeSpan ttft,
        double accuracyScore, bool passed, IReadOnlyList<TestCheckResult> checks)
        => Task.CompletedTask;

    /// <summary>Reports an error to the UI.</summary>
    Task ReportErrorAsync(string source, string message)
        => Task.CompletedTask;
}

// ── Static Accessor ────────────────────────────────────────────────────

/// <summary>
/// Static accessor for the agent output strategy. Call <see cref="AgentConsole.Configure"/>
/// once at startup. Mirrors the <see cref="Agent.SDK.Logging.AgentLogging"/> pattern.
/// </summary>
public static class AgentConsole
{
    private static IAgentOutput _output = new PlainAgentOutput();

    public static IAgentOutput Output => _output;

    public static void Configure(bool headless = false, bool channelMode = false)
    {
        // Fall back to plain output if stdout is redirected (piped, CI, etc.)
        _output = headless || System.Console.IsOutputRedirected
            ? new PlainAgentOutput()
            : channelMode
                ? new ChannelAgentOutput()
                : new SpectreAgentOutput();
    }
}
