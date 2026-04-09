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
/// Spectre.Console interactive output. Single-column layout:
/// - Header: logo + agent name + figlet + status
/// - Reasoning tree: each file is a branch, LLM thinking nests under it
/// - File tree: directory listing with status indicators
/// - Progress bar + summary
/// </summary>
public sealed class SpectreAgentOutput : IAgentOutput
{
    private readonly object _lock = new();
    private readonly HashSet<string> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenFiles = new(StringComparer.OrdinalIgnoreCase);

    // Per-file work unit: tool calls + thinking text
    private readonly Dictionary<string, FileWork> _workByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _fileOrder = []; // insertion order
    private string? _activeFile; // current file receiving thinking tokens
    private readonly StringBuilder _pendingThinking = new(); // tokens before any file starts

    private sealed class FileWork
    {
        public List<(string Tool, double Seconds)> Tools { get; } = [];
        public StringBuilder Thinking { get; } = new();
    }

    private TaskCompletionSource? _stopSignal;
    private Task? _renderTask;
    private string _agentName = "Agent";
    private string _status = "Initializing...";
    private string _currentTool = "";
    private string _currentPath = "";
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

    public void ToolStarted(string toolName, string? detail = null)
    {
        lock (_lock)
        {
            ToolCallCount++;
            _currentTool = toolName;
            _currentPath = detail ?? "";
            _status = $"{toolName}...";

            if (detail is not null && detail.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                _seenFiles.Add(detail);
                _activeFile = detail;

                if (!_workByFile.ContainsKey(detail))
                {
                    _workByFile[detail] = new FileWork();
                    _fileOrder.Add(detail);
                }

                // Flush any pending thinking into this file
                if (_pendingThinking.Length > 0)
                {
                    _workByFile[detail].Thinking.Append(_pendingThinking);
                    _pendingThinking.Clear();
                }
            }
        }
    }

    public void ToolCompleted(string toolName, TimeSpan elapsed)
    {
        lock (_lock)
        {
            if (_currentPath.Length > 0 && _currentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                _processedFiles.Add(_currentPath);

                if (_workByFile.TryGetValue(_currentPath, out var work))
                {
                    work.Tools.Add((toolName, elapsed.TotalSeconds));
                }
            }

            _currentTool = "";
            _currentPath = "";
            _status = "Thinking...";
        }
    }

    public void AppendThinking(string token)
    {
        lock (_lock)
        {
            if (_activeFile is not null && _workByFile.TryGetValue(_activeFile, out var work))
            {
                work.Thinking.Append(token);
            }
            else
            {
                _pendingThinking.Append(token);
            }
        }
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _summary = summary;
            _status = "Done";
            _currentTool = "";
            _activeFile = null;
        }

        _stopSignal?.TrySetResult();
        if (_renderTask is not null)
        {
            await _renderTask;
        }

        // Write reasoning to markdown file next to the output
        WriteReasoningFile(summary);
    }

    /// <summary>
    /// Writes the full reasoning trace as a markdown file next to the agent output.
    /// e.g. CONTEXT.md → CONTEXT.reasoning.md
    /// </summary>
    private void WriteReasoningFile(AgentRunSummary summary)
    {
        try
        {
            var outputDir = Path.GetDirectoryName(summary.FullOutputPath);
            var reasoningPath = Path.Combine(outputDir ?? ".", "REASONING.md");

            var sb = new StringBuilder();
            sb.AppendLine($"# Reasoning Trace");
            sb.AppendLine();
            sb.AppendLine($"> Agent: {_agentName}");
            sb.AppendLine($"> Duration: {summary.Duration.TotalSeconds:F1}s");
            sb.AppendLine($"> Tools: {summary.ToolCallCount} calls");
            sb.AppendLine($"> Status: {(summary.Success ? "Success" : "Failed")}");
            sb.AppendLine();

            foreach (var file in _fileOrder)
            {
                if (!_workByFile.TryGetValue(file, out var work))
                {
                    continue;
                }

                sb.AppendLine($"## {file}");
                sb.AppendLine();

                if (work.Tools.Count > 0)
                {
                    foreach (var (tool, secs) in work.Tools)
                    {
                        sb.AppendLine($"- **{tool}** ({secs:F1}s)");
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

            // Pending thinking that wasn't associated with a file
            var pending = _pendingThinking.ToString().Trim();
            if (pending.Length > 0)
            {
                sb.AppendLine("## General");
                sb.AppendLine();
                sb.AppendLine(pending);
                sb.AppendLine();
            }

            File.WriteAllText(reasoningPath, sb.ToString());
        }
        catch
        {
            // Best effort — don't fail the agent if reasoning write fails
        }
    }

    public void WriteResponse(string text) { }
    public void Dispose() { _stopSignal?.TrySetResult(); }

    // ── Layout ─────────────────────────────────────────────────────────

    private IRenderable BuildLayout()
    {
        lock (_lock)
        {
            var sections = new List<IRenderable>();

            // Header
            sections.Add(BuildHeader());

            // Reasoning tree (files + thinking)
            var reasoningTree = BuildReasoningTree();
            if (reasoningTree is not null)
            {
                sections.Add(reasoningTree);
            }

            // File tree + progress + summary
            sections.Add(BuildStatusPanel());

            return new Rows(sections);
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

    /// <summary>Builds a tree where each file is a branch with tool calls + reasoning nested under it.</summary>
    private IRenderable? BuildReasoningTree()
    {
        if (_fileOrder.Count == 0 && _pendingThinking.Length == 0)
        {
            return null;
        }

        var tree = new Tree($"[{AgentTheme.CyanHex} bold]Reasoning[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(new Style(AgentTheme.Dim));

        // Show pending thinking (before any file-specific work starts)
        if (_pendingThinking.Length > 0 && _fileOrder.Count == 0)
        {
            var pendingNode = tree.AddNode($"[{AgentTheme.OrangeHex}]{Spinner[_spinnerFrame]} Analyzing...[/]");
            foreach (var line in _pendingThinking.ToString().Trim().Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    pendingNode.AddNode($"[{AgentTheme.DimHex}]{Markup.Escape(trimmed)}[/]");
                }
            }
        }

        foreach (var file in _fileOrder)
        {
            if (!_workByFile.TryGetValue(file, out var work))
            {
                continue;
            }

            var isActive = _activeFile?.Equals(file, StringComparison.OrdinalIgnoreCase) == true
                           && _summary is null;
            var icon = isActive
                ? $"[{AgentTheme.OrangeHex}]{Spinner[_spinnerFrame]}[/]"
                : $"[{AgentTheme.GreenHex}]✓[/]";
            var nameColor = isActive ? AgentTheme.CyanHex : AgentTheme.DimLightHex;

            var fileNode = tree.AddNode($"{icon} [{nameColor}]{Markup.Escape(file)}[/]");

            // Tool calls performed on this file
            foreach (var (tool, secs) in work.Tools)
            {
                fileNode.AddNode($"[{AgentTheme.DimHex}]{Markup.Escape(tool)} [{AgentTheme.DimLightHex}]{secs:F1}s[/][/]");
            }

            // Any LLM thinking text about this file
            var text = work.Thinking.ToString().Trim();
            if (text.Length > 0)
            {
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        fileNode.AddNode($"[{AgentTheme.OrangeHex}]{Markup.Escape(trimmed)}[/]");
                    }
                }
            }
        }

        return new Panel(tree)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim));
    }

    /// <summary>File tree + progress + summary in a compact panel.</summary>
    private IRenderable BuildStatusPanel()
    {
        var rows = new List<IRenderable>();

        // File tree
        rows.Add(BuildFileTree());

        // Progress bar
        var processed = _processedFiles.Count;
        var total = _seenFiles.Count;
        if (total > 0)
        {
            var pct = (int)(processed * 100.0 / total);
            var filled = pct / 5;
            var bar = new string('█', filled) + new string('░', 20 - filled);
            rows.Add(new Markup($" [{AgentTheme.CyanHex}]{bar}[/] [{AgentTheme.DimLightHex}]{processed}/{total} files[/]"));
        }
        else
        {
            rows.Add(new Markup($" [{AgentTheme.DimHex}]Discovering files...[/]"));
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
            .Header($"[{AgentTheme.CyanHex} bold]Files[/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(AgentTheme.Dim));
    }

    private Tree BuildFileTree()
    {
        var tree = new Tree($"[{AgentTheme.CyanHex}]📁 .[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(new Style(AgentTheme.Dim));

        var dirs = new Dictionary<string, List<(string File, bool Done, bool Active)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in _processedFiles)
        {
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? ".";
            if (dir.Length == 0) { dir = "."; }
            var file = Path.GetFileName(path);
            if (!dirs.ContainsKey(dir)) { dirs[dir] = []; }
            dirs[dir].Add((file, Done: true, Active: false));
        }

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

        foreach (var (dir, files) in dirs.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
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
}
