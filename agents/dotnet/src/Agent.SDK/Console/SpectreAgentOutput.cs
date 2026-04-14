using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

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

    public async Task ReportPromptResultAsync(string promptName, double tokensPerSecond, double accuracyScore)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
            {
                work.Metrics.Add((promptName, tokensPerSecond, accuracyScore));
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
        if (commit is not null) { versionText += $" ({commit})"; }

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
        if (_scannerOrder.Count == 0) { return null; }

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

            // Per-prompt benchmark metrics (tok/s + accuracy)
            if (work.Metrics.Count > 0)
            {
                var metricsNode = scannerNode.AddNode($"[{AgentTheme.SkyHex}]Results[/]");
                foreach (var (prompt, tokS, score) in work.Metrics)
                {
                    var scoreColor = score >= 0.8 ? AgentTheme.GreenHex
                        : score >= 0.5 ? AgentTheme.OrangeHex
                        : AgentTheme.RedHex;
                    var shortPrompt = prompt.Length > 24 ? prompt[..24] + "…" : prompt;
                    metricsNode.AddNode(
                        $"[{AgentTheme.DimLightHex}]{Markup.Escape(shortPrompt)}[/]  " +
                        $"[{AgentTheme.CyanHex}]{tokS:F1}[/] [{AgentTheme.DimHex}]tok/s[/]  " +
                        $"[{scoreColor}]{score:P0}[/]");
                }
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
        if (string.IsNullOrEmpty(model)) { return "?"; }

        // Strip org prefix
        var idx = model.LastIndexOf('/');
        var name = idx >= 0 ? model[(idx + 1)..] : model;

        // Common shortenings
        if (name.Contains("nano-4b", StringComparison.OrdinalIgnoreCase)) { return "nano-4b"; }
        if (name.Contains("gemma-4-26b", StringComparison.OrdinalIgnoreCase)) { return "gemma-26b"; }
        if (name.Contains("gemma-4-31b", StringComparison.OrdinalIgnoreCase)) { return "gemma-31b"; }
        if (name.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase)) { return "gemma-e4b"; }
        if (name.Contains("qwen3", StringComparison.OrdinalIgnoreCase)) { return "qwen3"; }

        // Generic: take last segment after last hyphen cluster
        return name.Length > 20 ? name[..20] + "..." : name;
    }
}
