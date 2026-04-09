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
/// Spectre.Console interactive output using Live display with:
/// - Layout: left panel (file tree + progress) / right panel (reasoning)
/// - Tree widget for directory/file status with guide lines
/// - Progress bar for overall tool completion
/// - Summary embedded in the layout when done
/// </summary>
public sealed class SpectreAgentOutput : IAgentOutput
{
    private readonly object _lock = new();
    private readonly List<(string ToolName, string? Path, double Seconds)> _completedTools = [];
    private readonly HashSet<string> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _thinking = new();

    private TaskCompletionSource? _stopSignal;
    private Task? _renderTask;
    private string _agentName = "Agent";
    private string _status = "Initializing...";
    private string _currentTool = "";
    private string _currentPath = "";
    private int _spinnerFrame;
    private readonly HashSet<string> _seenFiles = new(StringComparer.OrdinalIgnoreCase);
    private AgentRunSummary? _summary;

    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private const int MaxThinkingLines = 20;

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

                    // Final frame with summary embedded
                    ctx.UpdateTarget(BuildLayout());
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
            _currentPath = detail ?? "";
            _status = $"{toolName}...";

            // Track unique files encountered (for progress denominator)
            if (detail is not null && detail.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                _seenFiles.Add(detail);
            }
        }
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
    {
        lock (_lock)
        {
            var path = _currentPath;
            _completedTools.Add((toolName, path.Length > 0 ? path : null, elapsed.TotalSeconds));

            // Track unique files processed
            if (path.Length > 0 && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                _processedFiles.Add(path);
            }

            _currentTool = "";
            _currentPath = "";
            _status = "Thinking...";
        }
    }

    public void AppendThinking(string token)
    {
        lock (_lock) { _thinking.Append(token); }
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _summary = summary;
            _status = "Done";
            _currentTool = "";
        }

        _stopSignal?.TrySetResult();
        if (_renderTask is not null)
        {
            await _renderTask;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{AgentTheme.DimHex}]Press Enter to exit...[/]");
        System.Console.ReadLine();
    }

    public void WriteResponse(string text) { }

    public void Dispose() { _stopSignal?.TrySetResult(); }

    // ── Layout Builder ─────────────────────────────────────────────────

    private IRenderable BuildLayout()
    {
        lock (_lock)
        {
            var layout = new Layout("root")
                .SplitRows(
                    new Layout("header").Size(10),
                    new Layout("body"));

            layout["body"].SplitColumns(
                new Layout("left").Ratio(1),
                new Layout("right").Ratio(2));

            // Header: logo + agent name + status
            layout["header"].Update(BuildHeader());

            // Left: tree + progress + summary
            layout["left"].Update(BuildLeftPanel());

            // Right: reasoning stream
            layout["right"].Update(BuildRightPanel());

            return layout;
        }
    }

    private IRenderable BuildHeader()
    {
        var spin = _summary is null ? Spinner[_spinnerFrame] : "✓";
        var spinColor = _summary is null ? AgentTheme.OrangeHex : AgentTheme.GreenHex;

        var (version, commit) = AgentTheme.GetVersionInfo();
        var versionText = $"v{version}";
        if (commit is not null)
        {
            versionText += $" ({commit})";
        }

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

    private IRenderable BuildLeftPanel()
    {
        var rows = new List<IRenderable>();

        // File tree
        rows.Add(BuildFileTree());
        rows.Add(new Text(""));

        // Progress bar
        var processed = _processedFiles.Count;
        var total = _seenFiles.Count;
        if (total > 0)
        {
            var pct = (int)(processed * 100.0 / total);
            var filled = pct / 5; // 20 chars wide
            var bar = new string('█', filled) + new string('░', 20 - filled);
            rows.Add(new Markup($" [{AgentTheme.CyanHex}]{bar}[/] [{AgentTheme.DimLightHex}]{processed}/{total} files[/]"));
        }
        else
        {
            rows.Add(new Markup($" [{AgentTheme.DimHex}]Discovering files...[/]"));
        }

        rows.Add(new Text(""));

        // Summary (embedded when done)
        if (_summary is not null)
        {
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
            .Header($"[{AgentTheme.CyanHex} bold]{Markup.Escape(_agentName)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim))
            .Expand();
    }

    private Tree BuildFileTree()
    {
        var tree = new Tree($"[{AgentTheme.CyanHex}]📁 .[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(new Style(AgentTheme.Dim));

        // Group processed files by directory
        var dirs = new Dictionary<string, List<(string File, bool Done, bool Active)>>(
            StringComparer.OrdinalIgnoreCase);

        // Add completed files
        foreach (var path in _processedFiles)
        {
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? ".";
            if (dir.Length == 0) { dir = "."; }
            var file = Path.GetFileName(path);

            if (!dirs.ContainsKey(dir)) { dirs[dir] = []; }
            dirs[dir].Add((file, Done: true, Active: false));
        }

        // Add currently active file
        if (_currentPath.Length > 0 && _currentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(_currentPath)?.Replace('\\', '/') ?? ".";
            if (dir.Length == 0) { dir = "."; }
            var file = Path.GetFileName(_currentPath);

            if (!dirs.ContainsKey(dir)) { dirs[dir] = []; }
            if (!dirs[dir].Any(f => f.File.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                dirs[dir].Add((file, Done: false, Active: true));
            }
        }

        // Render tree
        foreach (var (dir, files) in dirs.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
            // Root-level files go directly on tree, subdirs get a folder node
            TreeNode? dirNode = dir == "."
                ? null
                : tree.AddNode($"[{AgentTheme.SkyHex}]📁 {Markup.Escape(dir)}[/]");

            foreach (var (file, done, active) in files.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase))
            {
                var markup = active
                    ? $"[{AgentTheme.OrangeHex}]{Spinner[_spinnerFrame]} {Markup.Escape(file)}[/]"
                    : $"[{AgentTheme.GreenHex}]✓[/] [{AgentTheme.DimLightHex}]{Markup.Escape(file)}[/]";

                if (dirNode is not null)
                {
                    dirNode.AddNode(markup);
                }
                else
                {
                    tree.AddNode(markup);
                }
            }
        }

        if (dirs.Count == 0)
        {
            tree.AddNode($"[{AgentTheme.DimHex}]scanning...[/]");
        }

        return tree;
    }

    private IRenderable BuildRightPanel()
    {
        var thinkText = TruncateThinking();

        // Use Spectre's natural word wrapping — just pass the full text as Markup
        IRenderable content = thinkText.Length > 0
            ? new Markup($"[{AgentTheme.DimLightHex}]{Markup.Escape(thinkText)}[/]")
            : new Markup($"[{AgentTheme.DimHex}]Waiting for model...[/]");

        return new Panel(content)
            .Header($"[{AgentTheme.CyanHex} bold]Reasoning[/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim))
            .Expand();
    }

    private string TruncateThinking()
    {
        var text = _thinking.ToString().TrimEnd();
        if (text.Length == 0) { return ""; }

        var lines = text.Split('\n');
        return lines.Length <= MaxThinkingLines
            ? text
            : string.Join('\n', lines[^MaxThinkingLines..]);
    }
}
