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
    void UpdateStatus(string status);
    void ScannerStarted(string scannerName, string modelName);
    void ScannerCompleted(string scannerName, TimeSpan elapsed, bool success);
    void ToolStarted(string toolName, string? detail = null);
    void ToolCompleted(string toolName, TimeSpan elapsed);
    void AppendThinking(string token);
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
    public bool IsInteractive => false;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        Log.Information("Starting {AgentName}...", agentName);
        return Task.CompletedTask;
    }

    public void UpdateStatus(string status)
        => Log.Information("{Status}", status);

    public void ScannerStarted(string scannerName, string modelName)
        => Log.Information("Running: {ScannerName} [{Model}]", scannerName, modelName);

    public void ScannerCompleted(string scannerName, TimeSpan elapsed, bool success)
    {
        if (success)
            Log.Information("Completed: {ScannerName} in {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
        else
            Log.Warning("Failed: {ScannerName} after {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
    }

    public void ToolStarted(string toolName, string? detail = null)
    {
        ToolCallCount++;
        Log.Information("  {ToolName} {Detail}", toolName, detail ?? "");
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
        => Log.Debug("  {ToolName} completed in {Elapsed:F1}s", toolName, elapsed.TotalSeconds);

    public void AppendThinking(string token) { } // silent in headless

    public Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        Log.Information("Completed in {Duration:F1}s - {ToolCalls} tool calls, output: {Output}",
            summary.Duration.TotalSeconds, summary.ToolCallCount, summary.OutputPath);
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
/// Spectre.Console interactive output with scanner-centric layout.
/// Each scanner gets its own panel showing model, tool calls, and status.
/// </summary>
public sealed class SpectreAgentOutput : IAgentOutput
{
    private readonly object _lock = new();

    // Scanner tracking
    private readonly List<string> _scannerOrder = [];
    private readonly Dictionary<string, ScannerWork> _scannerWork = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeScanner;

    private sealed class ScannerWork
    {
        public required string ModelName { get; init; }
        public List<(string Tool, string? Detail, double Seconds)> Tools { get; } = [];
        public StringBuilder Thinking { get; } = new();
        public TimeSpan? Elapsed { get; set; }
        public bool? Success { get; set; }
    }

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
            await AnsiConsole.Live(BuildLayout())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx =>
                {
                    while (!_stopSignal.Task.IsCompleted)
                    {
                        lock (_lock) { _spinnerFrame = (_spinnerFrame + 1) % Spinner.Length; }
                        ctx.UpdateTarget(BuildLayout());
                        try { await Task.Delay(140, ct); }
                        catch (OperationCanceledException) { break; }
                    }
                    ctx.UpdateTarget(BuildLayout());
                });
        }, ct);

        return Task.CompletedTask;
    }

    public void UpdateStatus(string status)
    {
        lock (_lock) { _status = status; }
    }

    public void ScannerStarted(string scannerName, string modelName)
    {
        lock (_lock)
        {
            _activeScanner = scannerName;
            _status = $"{scannerName}...";

            if (!_scannerWork.ContainsKey(scannerName))
            {
                _scannerWork[scannerName] = new ScannerWork { ModelName = modelName };
                _scannerOrder.Add(scannerName);
            }
        }
    }

    public void ScannerCompleted(string scannerName, TimeSpan elapsed, bool success)
    {
        lock (_lock)
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
    }

    public void ToolStarted(string toolName, string? detail = null)
    {
        lock (_lock)
        {
            ToolCallCount++;
            _status = $"{toolName}...";
        }
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
    {
        lock (_lock)
        {
            // Associate tool call with the active scanner
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Tools.Add((toolName, null, elapsed.TotalSeconds));
            }

            _status = "Thinking...";
        }
    }

    public void AppendThinking(string token)
    {
        lock (_lock)
        {
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Thinking.Append(token);
            }
        }
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _summary = summary;
            _status = "Done";
            _activeScanner = null;
        }

        _stopSignal?.TrySetResult();
        if (_renderTask is not null)
        {
            await _renderTask;
        }

        WriteReasoningFile(summary);
    }

    public void WriteResponse(string text) { }
    public void Dispose() { _stopSignal?.TrySetResult(); }

    // ── Layout ─────────────────────────────────────────────────────────

    private IRenderable BuildLayout()
    {
        lock (_lock)
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
    }

    private IRenderable BuildHeader()
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
    private IRenderable? BuildScannerTree()
    {
        if (_scannerOrder.Count == 0) return null;

        var tree = new Tree($"[{AgentTheme.CyanHex} bold]Scanners[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(new Style(AgentTheme.Dim));

        foreach (var name in _scannerOrder)
        {
            if (!_scannerWork.TryGetValue(name, out var work)) continue;

            var isActive = _activeScanner == name && _summary is null;
            var isDone = work.Elapsed.HasValue;

            // Icon: spinner (active), check (done+success), cross (done+failed), circle (pending)
            string icon;
            if (isActive)
                icon = $"[{AgentTheme.OrangeHex}]{Spinner[_spinnerFrame]}[/]";
            else if (isDone && work.Success == true)
                icon = $"[{AgentTheme.GreenHex}]✓[/]";
            else if (isDone && work.Success == false)
                icon = $"[{AgentTheme.RedHex}]✗[/]";
            else
                icon = $"[{AgentTheme.DimHex}]○[/]";

            var nameColor = isActive ? AgentTheme.CyanHex : (isDone ? AgentTheme.DimLightHex : AgentTheme.DimHex);
            var modelShort = ShortenModelName(work.ModelName);
            var elapsed = work.Elapsed.HasValue ? $" {work.Elapsed.Value.TotalSeconds:F1}s" : "";

            var scannerNode = tree.AddNode(
                $"{icon} [{nameColor} bold]{Markup.Escape(name)}[/] [{AgentTheme.DimHex}]{Markup.Escape(modelShort)}{elapsed}[/]");

            // Tool calls
            foreach (var (tool, _, secs) in work.Tools)
            {
                scannerNode.AddNode($"[{AgentTheme.DimHex}]{Markup.Escape(tool)} [{AgentTheme.DimLightHex}]{secs:F1}s[/][/]");
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
    private IRenderable BuildScannerProgress()
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

    /// <summary>Writes reasoning trace organized by scanner.</summary>
    private void WriteReasoningFile(AgentRunSummary summary)
    {
        try
        {
            var outputDir = Path.GetDirectoryName(summary.FullOutputPath);
            var reasoningPath = Path.Combine(outputDir ?? ".", "REASONING.md");

            var sb = new StringBuilder();
            sb.AppendLine("# Reasoning Trace");
            sb.AppendLine();
            sb.AppendLine($"> Agent: {_agentName}");
            sb.AppendLine($"> Duration: {summary.Duration.TotalSeconds:F1}s");
            sb.AppendLine($"> Tools: {summary.ToolCallCount} calls");
            sb.AppendLine($"> Status: {(summary.Success ? "Success" : "Failed")}");
            sb.AppendLine();

            foreach (var name in _scannerOrder)
            {
                if (!_scannerWork.TryGetValue(name, out var work)) continue;

                var status = work.Success switch
                {
                    true => "Success",
                    false => "Failed",
                    null => "Pending",
                };
                var elapsed = work.Elapsed.HasValue ? $"{work.Elapsed.Value.TotalSeconds:F1}s" : "-";

                sb.AppendLine($"## {name}");
                sb.AppendLine();
                sb.AppendLine($"- **Model:** {work.ModelName}");
                sb.AppendLine($"- **Duration:** {elapsed}");
                sb.AppendLine($"- **Status:** {status}");
                sb.AppendLine($"- **Tool calls:** {work.Tools.Count}");
                sb.AppendLine();

                if (work.Tools.Count > 0)
                {
                    foreach (var (tool, _, secs) in work.Tools)
                    {
                        sb.AppendLine($"  - {tool} ({secs:F1}s)");
                    }
                    sb.AppendLine();
                }

                var thinking = work.Thinking.ToString().Trim();
                if (thinking.Length > 0)
                {
                    sb.AppendLine("### Thinking");
                    sb.AppendLine();
                    sb.AppendLine(thinking);
                    sb.AppendLine();
                }
            }

            File.WriteAllText(reasoningPath, sb.ToString());
        }
        catch
        {
            // Best effort
        }
    }
}
