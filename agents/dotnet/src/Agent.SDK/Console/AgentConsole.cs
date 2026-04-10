using System.Text;
using Serilog;
using Spectre.Console;
using Spectre.Console.Rendering;

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
    Task StopAsync(AgentRunSummary summary, CancellationToken ct = default);
    Task WriteResponseAsync(string text);
}

// ── Static Accessor ────────────────────────────────────────────────────

/// <summary>
/// Static accessor for the agent output strategy. Call <see cref="Configure"/>
/// once at startup. Mirrors the <see cref="Agent.SDK.Logging.AgentLogging"/> pattern.
/// </summary>
public static class AgentConsole
{
    private static IAgentOutput _output = new PlainAgentOutput();

    public static IAgentOutput Output => _output;

    public static void Configure(bool headless = false)
    {
        // Fall back to plain output if stdout is redirected (piped, CI, etc.)
        _output = headless || System.Console.IsOutputRedirected
            ? new PlainAgentOutput()
            : new SpectreAgentOutput();
    }
}

// ── Headless Implementation ────────────────────────────────────────────

/// <summary>Routes all output through Serilog. No Spectre rendering.</summary>
public sealed class PlainAgentOutput : IAgentOutput
{
    private readonly List<ScannerTrace> _scanners = [];
    private readonly Dictionary<string, ScannerTrace> _scannerWork = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeScanner;
    private string _agentName = "Agent";

    public bool IsInteractive => false;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        _agentName = agentName;
        Log.Information("Starting {AgentName}...", agentName);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string status)
    {
        Log.Information("{Status}", status);
        return Task.CompletedTask;
    }

    public Task ScannerSkippedAsync(string scannerName, string reason)
    {
        var trace = new ScannerTrace { Name = scannerName, ModelName = "—" };
        trace.Elapsed = TimeSpan.Zero;
        trace.Success = null;
        trace.Response.Append(reason);
        _scanners.Add(trace);
        Log.Warning("Skipped: {ScannerName} — {Reason}", scannerName, reason);
        return Task.CompletedTask;
    }

    public Task ScannerStartedAsync(string scannerName, string modelName)
    {
        _activeScanner = scannerName;
        if (!_scannerWork.ContainsKey(scannerName))
        {
            var trace = new ScannerTrace { Name = scannerName, ModelName = modelName };
            _scannerWork[scannerName] = trace;
            _scanners.Add(trace);
        }

        Log.Information("Running: {ScannerName} [{Model}]", scannerName, modelName);
        return Task.CompletedTask;
    }

    public Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success)
    {
        if (_scannerWork.TryGetValue(scannerName, out var work))
        {
            work.Elapsed = elapsed;
            work.Success = success;
        }

        if (_activeScanner == scannerName)
        {
            _activeScanner = null;
        }

        if (success)
        {
            Log.Information("Completed: {ScannerName} in {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
        }
        else
        {
            Log.Warning("Failed: {ScannerName} after {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
        }

        return Task.CompletedTask;
    }

    public Task ToolStartedAsync(string toolName, string? detail = null)
    {
        ToolCallCount++;
        Log.Information("  {ToolName} {Detail}", toolName, detail ?? "");
        return Task.CompletedTask;
    }

    public Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true)
    {
        if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
        {
            work.Tools.Add((toolName, detail, elapsed.TotalSeconds, success));
        }

        if (success)
        {
            Log.Debug("  {ToolName} completed in {Elapsed:F1}s", toolName, elapsed.TotalSeconds);
        }
        else
        {
            Log.Warning("  {ToolName} failed after {Elapsed:F1}s", toolName, elapsed.TotalSeconds);
        }

        return Task.CompletedTask;
    }

    public Task AppendThinkingAsync(string token) => Task.CompletedTask;

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        Log.Information("Completed in {Duration:F1}s - {ToolCalls} tool calls, output: {Output}",
            summary.Duration.TotalSeconds, summary.ToolCallCount, summary.OutputPath);
        await AgentReasoningLog.WriteAsync(summary.FullOutputPath, _agentName, summary, _scanners);
    }

    public Task WriteResponseAsync(string text) => Task.CompletedTask;

    public void Dispose() { }
}

// ── Interactive Implementation ─────────────────────────────────────────

/// <summary>
/// Spectre.Console interactive output with scanner-centric layout.
/// Each scanner gets its own panel showing model, tool calls, and status.
/// </summary>
public sealed class SpectreAgentOutput : IAgentOutput
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Scanner tracking
    private readonly List<ScannerTrace> _scannerOrder = [];
    private readonly Dictionary<string, ScannerTrace> _scannerWork = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeScanner;

    private TaskCompletionSource? _stopSignal;
    private Task? _renderTask;
    private string _agentName = "Agent";
    private string _status = "Initializing...";
    private int _spinnerFrame;
    private AgentRunSummary? _summary;

    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public bool IsInteractive => true;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        _agentName = agentName;
        _stopSignal = new TaskCompletionSource();

        _renderTask = Task.Run(async () =>
        {
            await AnsiConsole.Live(await BuildLayoutAsync().ConfigureAwait(false))
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx =>
                {
                    while (!_stopSignal.Task.IsCompleted)
                    {
                        if (await _lock.WaitAsync(0).ConfigureAwait(false))
                        {
                            try { _spinnerFrame = (_spinnerFrame + 1) % Spinner.Length; }
                            finally { _lock.Release(); }
                        }

                        ctx.UpdateTarget(await BuildLayoutAsync().ConfigureAwait(false));
                        try { await Task.Delay(140, ct); }
                        catch (OperationCanceledException) { break; }
                    }
                    ctx.UpdateTarget(await BuildLayoutAsync().ConfigureAwait(false));
                });
        }, ct);

        return Task.CompletedTask;
    }

    public async Task UpdateStatusAsync(string status)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { _status = status; }
        finally { _lock.Release(); }
    }

    public async Task ScannerSkippedAsync(string scannerName, string reason)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var trace = new ScannerTrace { Name = scannerName, ModelName = "—" };
            trace.Elapsed = TimeSpan.Zero;
            trace.Success = null;
            trace.Response.Append(reason);
            _scannerWork[scannerName] = trace;
            _scannerOrder.Add(trace);
        }
        finally { _lock.Release(); }
    }

    public async Task ScannerStartedAsync(string scannerName, string modelName)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _activeScanner = scannerName;
            _status = $"{scannerName}...";

            if (!_scannerWork.ContainsKey(scannerName))
            {
                var trace = new ScannerTrace { Name = scannerName, ModelName = modelName };
                _scannerWork[scannerName] = trace;
                _scannerOrder.Add(trace);
            }
        }
        finally { _lock.Release(); }
    }

    public async Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_scannerWork.TryGetValue(scannerName, out var work))
            {
                work.Elapsed = elapsed;
                work.Success = success;
            }

            if (_activeScanner == scannerName)
            {
                _activeScanner = null;
            }

            _status = "Thinking...";
        }
        finally { _lock.Release(); }
    }

    public async Task ToolStartedAsync(string toolName, string? detail = null)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            ToolCallCount++;
            _status = detail is not null ? $"{toolName} ({detail})..." : $"{toolName}...";
        }
        finally { _lock.Release(); }
    }

    public async Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Tools.Add((toolName, detail, elapsed.TotalSeconds, success));
            }

            _status = "Thinking...";
        }
        finally { _lock.Release(); }
    }

    public async Task AppendThinkingAsync(string token)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Thinking.Append(token);
            }
        }
        finally { _lock.Release(); }
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _summary = summary;
            _status = "Done";
            _activeScanner = null;
        }
        finally { _lock.Release(); }

        _stopSignal?.TrySetResult();
        if (_renderTask is not null)
        {
            await _renderTask.ConfigureAwait(false);
        }

        await AgentReasoningLog.WriteAsync(summary.FullOutputPath, _agentName, summary, _scannerOrder)
            .ConfigureAwait(false);
    }

    public async Task WriteResponseAsync(string text)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Response.Append(text);
            }
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _stopSignal?.TrySetResult();
        _lock.Dispose();
    }

    // ── Layout ─────────────────────────────────────────────────────────

    private async Task<IRenderable> BuildLayoutAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var sections = new List<IRenderable>
            {
                BuildHeader(),
            };

            var scannerTree = BuildScannerTree();
            if (scannerTree is not null)
            {
                sections.Add(scannerTree);
            }

            sections.Add(BuildScannerProgress());

            return new Rows(sections);
        }
        finally { _lock.Release(); }
    }

    private Table BuildHeader()
    {
        var spin = _summary is null ? Spinner[_spinnerFrame] : "✓";
        var spinColor = _summary is null ? AgentTheme.OrangeHex : AgentTheme.GreenHex;

        var (version, commit) = AgentTheme.GetVersionInfo();
        var versionText = $"v{version}";
        if (commit is not null) versionText += $" ({commit})";

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("logo").NoWrap().Width(10))
            .AddColumn(new TableColumn("info"));

        var info = new Rows(
            new Markup($"[{AgentTheme.CyanHex} bold]{Markup.Escape(_agentName)}[/] [{AgentTheme.DimHex}]{Markup.Escape(versionText)}[/]"),
            new FigletText("Agent In Command").Color(AgentTheme.Dim),
            new Markup($"[{spinColor}]{spin}[/] [{AgentTheme.DimLightHex}]{Markup.Escape(_status)}[/]"));

        table.AddRow(AgentTheme.Logo(), info);
        return table;
    }

    /// <summary>Builds a tree where each scanner is a branch with nested tool calls.</summary>
    private Panel? BuildScannerTree()
    {
        if (_scannerOrder.Count == 0) return null;

        var tree = new Tree($"[{AgentTheme.CyanHex} bold]Scanners[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(new Style(AgentTheme.Dim));

        foreach (var work in _scannerOrder)
        {
            var isActive = _activeScanner == work.Name && _summary is null;
            var isDone = work.Elapsed.HasValue;

            var isSkipped = work.Success is null && work.Elapsed.HasValue;

            // Icon: skip (⊘), spinner (active), check (done+success), cross (done+failed), circle (pending)
            string icon;
            if (isSkipped)
            {
                icon = $"[{AgentTheme.DimHex}]⊘[/]";
            }
            else if (isActive)
            {
                icon = $"[{AgentTheme.OrangeHex}]{Spinner[_spinnerFrame]}[/]";
            }
            else if (isDone && work.Success == true)
            {
                icon = $"[{AgentTheme.GreenHex}]✓[/]";
            }
            else if (isDone && work.Success == false)
            {
                icon = $"[{AgentTheme.RedHex}]✗[/]";
            }
            else
            {
                icon = $"[{AgentTheme.DimHex}]○[/]";
            }

            var nameColor = isSkipped ? AgentTheme.DimHex : (isActive ? AgentTheme.CyanHex : (isDone ? AgentTheme.DimLightHex : AgentTheme.DimHex));
            var modelShort = ShortenModelName(work.ModelName);
            var elapsed = work.Elapsed.HasValue && !isSkipped ? $" {work.Elapsed.Value.TotalSeconds:F1}s" : "";

            var skipSuffix = isSkipped ? " [dim]skipped[/]" : "";
            var scannerNode = tree.AddNode(
                $"{icon} [{nameColor} bold]{Markup.Escape(work.Name)}[/] [{AgentTheme.DimHex}]{Markup.Escape(modelShort)}{elapsed}[/]{skipSuffix}");

            if (isSkipped)
            {
                var reason = work.Response.ToString().Trim();
                if (reason.Length > 0)
                {
                    scannerNode.AddNode($"[{AgentTheme.DimHex}]{Markup.Escape(reason)}[/]");
                }

                continue;
            }

            // Tool calls
            foreach (var (tool, detail, secs, toolOk) in work.Tools)
            {
                var color = toolOk ? AgentTheme.DimHex : AgentTheme.RedHex;
                var statusIcon = toolOk ? "" : " ✗";
                var detailText = detail is not null ? $" [{AgentTheme.DimLightHex}]{Markup.Escape(detail)}[/]" : "";
                scannerNode.AddNode($"[{color}]{Markup.Escape(tool)}{detailText} [{AgentTheme.DimLightHex}]{secs:F1}s[/]{statusIcon}[/]");
            }

            // Active scanner thinking preview (last 2 lines)
            if (isActive)
            {
                var thinking = work.Thinking.ToString().TrimEnd();
                if (thinking.Length > 0)
                {
                    var lines = thinking.Split('\n');
                    var preview = lines.Length > 2 ? lines[^2..] : lines;
                    foreach (var line in preview)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0)
                        {
                            var display = trimmed.Length > 80 ? trimmed[..80] + "..." : trimmed;
                            scannerNode.AddNode($"[{AgentTheme.OrangeHex}]{Markup.Escape(display)}[/]");
                        }
                    }
                }
            }
        }

        return new Panel(tree)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim));
    }

    /// <summary>Scanner progress bar and summary.</summary>
    private Panel BuildScannerProgress()
    {
        var rows = new List<IRenderable>();

        // Progress bar
        var completed = _scannerWork.Values.Count(w => w.Elapsed.HasValue);
        var total = _scannerOrder.Count;
        if (total > 0)
        {
            var pct = (int)(completed * 100.0 / total);
            var filled = pct / 5;
            var bar = new string('█', filled) + new string('░', 20 - filled);
            rows.Add(new Markup($" [{AgentTheme.CyanHex}]{bar}[/] [{AgentTheme.DimLightHex}]{completed}/{total} scanners[/]"));
        }
        else
        {
            rows.Add(new Markup($" [{AgentTheme.DimHex}]Planning...[/]"));
        }

        // Summary (when done)
        if (_summary is not null)
        {
            rows.Add(new Text(""));
            rows.Add(AgentTheme.Divider("Summary"));
            var s = _summary;
            var statusText = s.Success ? $"[{AgentTheme.GreenHex}]Success[/]" : $"[{AgentTheme.RedHex}]Failed[/]";
            rows.Add(new Markup($" [{AgentTheme.SkyHex}]Status[/]    {statusText}"));
            rows.Add(new Markup($" [{AgentTheme.SkyHex}]Duration[/]  [white]{s.Duration.TotalSeconds:F1}s[/]"));
            rows.Add(new Markup($" [{AgentTheme.SkyHex}]Tools[/]     [white]{s.ToolCallCount} calls[/]"));
            rows.Add(new Markup($" [{AgentTheme.SkyHex}]Output[/]    [white]{Markup.Escape(s.OutputPath)}[/]"));
        }
        else
        {
            rows.Add(new Markup($" [{AgentTheme.DimHex}]Tool calls: {ToolCallCount}[/]"));
        }

        return new Panel(new Rows(rows))
            .Header($"[{AgentTheme.CyanHex} bold]Progress[/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim));
    }

    /// <summary>Shortens model names for display (e.g. "unsloth/nvidia-nemotron-3-nano-4b" → "nano-4b").</summary>
    private static string ShortenModelName(string model)
    {
        if (string.IsNullOrEmpty(model)) return "?";

        // Strip org prefix
        var idx = model.LastIndexOf('/');
        var name = idx >= 0 ? model[(idx + 1)..] : model;

        // Common shortenings
        if (name.Contains("nano-4b", StringComparison.OrdinalIgnoreCase)) return "nano-4b";
        if (name.Contains("gemma-4-26b", StringComparison.OrdinalIgnoreCase)) return "gemma-26b";
        if (name.Contains("gemma-4-31b", StringComparison.OrdinalIgnoreCase)) return "gemma-31b";
        if (name.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase)) return "gemma-e4b";
        if (name.Contains("qwen3", StringComparison.OrdinalIgnoreCase)) return "qwen3";

        // Generic: take last segment after last hyphen cluster
        return name.Length > 20 ? name[..20] + "..." : name;
    }

}
