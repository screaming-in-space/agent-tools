using Serilog;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

// ── Data ───────────────────────────────────────────────────────────────

/// <summary>End-of-run summary data rendered by <see cref="IAgentOutput.StopAsync"/>.</summary>
public sealed record AgentRunSummary(
    int FilesProcessed,
    TimeSpan Duration,
    string OutputPath,
    bool Success);

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
    void UpdateStatus(string status);
    void ToolStarted(string toolName, string? detail = null);
    void ToolCompleted(string toolName, TimeSpan elapsed);
    Task StopAsync(AgentRunSummary summary, CancellationToken ct = default);
    void WriteResponse(string text);
}

// ── Static Accessor ────────────────────────────────────────────────────

/// <summary>
/// Static accessor for the agent output strategy. Call <see cref="Configure"/>
/// once at startup. Mirrors the <see cref="Agent.SDK.Logging.AgentLogging"/> pattern.
/// </summary>
public static class AgentConsole
{
    private static IAgentOutput _output = new PlainAgentOutput();

    /// <summary>The active output strategy.</summary>
    public static IAgentOutput Output => _output;

    /// <summary>
    /// Selects interactive (Spectre) or headless (Serilog) output.
    /// Call once at startup, after logging is configured.
    /// </summary>
    public static void Configure(bool headless = false)
    {
        _output = headless ? new PlainAgentOutput() : new SpectreAgentOutput();
    }
}

// ── Headless Implementation ────────────────────────────────────────────

/// <summary>Routes all output through Serilog. No Spectre rendering.</summary>
public sealed class PlainAgentOutput : IAgentOutput
{
    public bool IsInteractive => false;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        Log.Information("Starting {AgentName}...", agentName);
        return Task.CompletedTask;
    }

    public void UpdateStatus(string status)
        => Log.Information("{Status}", status);

    public void ToolStarted(string toolName, string? detail = null)
    {
        ToolCallCount++;
        Log.Information("  {ToolName} {Detail}", toolName, detail ?? "");
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
        => Log.Debug("  {ToolName} completed in {Elapsed:F1}s", toolName, elapsed.TotalSeconds);

    public Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        Log.Information("Completed in {Duration:F1}s - {ToolCalls} tool calls, output: {Output}",
            summary.Duration.TotalSeconds, ToolCallCount, summary.OutputPath);
        return Task.CompletedTask;
    }

    public void WriteResponse(string text)
    {
        System.Console.WriteLine();
        System.Console.WriteLine(text);
        System.Console.WriteLine();
    }

    public void Dispose() { }
}

// ── Interactive Implementation ─────────────────────────────────────────

/// <summary>
/// Spectre.Console live rendering: spinner, tool status, summary panel.
/// Uses a background render loop at ~7 FPS that reads thread-safe state.
/// </summary>
public sealed class SpectreAgentOutput : IAgentOutput
{
    private readonly object _lock = new();
    private readonly List<(string Name, double Seconds)> _completedTools = [];

    private TaskCompletionSource? _stopSignal;
    private Task? _renderTask;
    private string _agentName = "Agent";
    private string _status = "Initializing...";
    private string _currentTool = "";
    private string _currentDetail = "";
    private int _spinnerFrame;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public bool IsInteractive => true;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        _agentName = agentName;
        _stopSignal = new TaskCompletionSource();

        _renderTask = Task.Run(async () =>
        {
            await AnsiConsole.Live(BuildRenderable())
                .AutoClear(true)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx =>
                {
                    while (!_stopSignal.Task.IsCompleted)
                    {
                        lock (_lock)
                        {
                            _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
                        }
                        ctx.UpdateTarget(BuildRenderable());
                        try { await Task.Delay(140, ct); }
                        catch (OperationCanceledException) { break; }
                    }
                    ctx.UpdateTarget(BuildRenderable());
                });
        }, ct);

        return Task.CompletedTask;
    }

    public void UpdateStatus(string status)
    {
        lock (_lock) { _status = status; }
    }

    public void ToolStarted(string toolName, string? detail = null)
    {
        lock (_lock)
        {
            ToolCallCount++;
            _currentTool = toolName;
            _currentDetail = detail ?? "";
            _status = $"{toolName}...";
        }
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
    {
        lock (_lock)
        {
            _completedTools.Add((toolName, elapsed.TotalSeconds));
            _currentTool = "";
            _currentDetail = "";
            _status = "Thinking...";
        }
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        _stopSignal?.TrySetResult();
        if (_renderTask is not null)
        {
            await _renderTask;
        }

        AnsiConsole.WriteLine();
        RenderSummary(summary);
        AnsiConsole.WriteLine();
    }

    public void WriteResponse(string text)
    {
        // Response is written to file by WriteOutput tool - don't dump raw LLM text to terminal
    }

    public void Dispose()
    {
        _stopSignal?.TrySetResult();
    }

    // ── Rendering ──────────────────────────────────────────────────────

    private IRenderable BuildRenderable()
    {
        lock (_lock)
        {
            var rows = new List<IRenderable>();

            // Current status with spinner
            var spinner = SpinnerFrames[_spinnerFrame];
            rows.Add(new Markup(
                $"  [{AgentTheme.OrangeHex}]{spinner}[/] [{AgentTheme.OrangeHex}]{Markup.Escape(_status)}[/]"));

            // Active tool
            if (_currentTool.Length > 0)
            {
                var detail = _currentDetail.Length > 0 ? $"  [{AgentTheme.SkyHex}]{Markup.Escape(_currentDetail)}[/]" : "";
                rows.Add(new Markup(
                    $"    [{AgentTheme.CyanHex}]{Markup.Escape(_currentTool)}[/]{detail}"));
            }

            rows.Add(new Text(""));

            // Completed tool log (last 5)
            var recent = _completedTools.TakeLast(5).ToList();
            if (recent.Count > 0)
            {
                foreach (var (name, secs) in recent)
                {
                    rows.Add(new Markup(
                        $"    {AgentTheme.Check} [{AgentTheme.DimHex}]{Markup.Escape(name),-28} {secs:F1}s[/]"));
                }
                rows.Add(new Text(""));
            }

            // Counter
            rows.Add(new Markup(
                $"    [{AgentTheme.DimHex}]Tool calls: {ToolCallCount}[/]"));

            return AgentTheme.LivePanel(_agentName, new Rows(rows));
        }
    }

    private static void RenderSummary(AgentRunSummary summary)
    {
        var statusText = summary.Success
            ? $"[{AgentTheme.GreenHex} bold]Success[/]"
            : $"[{AgentTheme.RedHex} bold]Failed[/]";

        var content = AgentTheme.InfoTable(
            ("Status", ""),
            ("Duration", $"{summary.Duration.TotalSeconds:F1}s"),
            ("Files", $"{summary.FilesProcessed} processed"),
            ("Output", summary.OutputPath));

        // Replace the status row with colored markup
        var rows = new Rows(
            new Markup($"  [{AgentTheme.SkyHex}]Status[/]     {statusText}"),
            new Markup($"  [{AgentTheme.SkyHex}]Duration[/]   [white]{summary.Duration.TotalSeconds:F1}s[/]"),
            new Markup($"  [{AgentTheme.SkyHex}]Files[/]      [white]{summary.FilesProcessed} processed[/]"),
            new Markup($"  [{AgentTheme.SkyHex}]Output[/]     [white]{Markup.Escape(summary.OutputPath)}[/]"));

        AnsiConsole.Write(AgentTheme.SummaryPanel("Run Summary", rows));
    }
}
