using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

/// <summary>
/// Consumes <see cref="UIMessage"/> records from a channel and renders vertically-extending
/// Spectre.Console panels. Each test gets its own panel with live-streaming token content.
/// Completed tests render as finalized panels; the active test streams tokens inline
/// then wraps them in a panel on completion.
/// </summary>
public sealed class ChannelAgentRenderer(ChannelReader<UIMessage> reader)
{
    private readonly StringBuilder _activeTokens = new();
    private readonly Stopwatch _phaseTimer = new();

    private string? _activePromptName;
    private string? _activePromptDescription;
    private string? _activePromptCategory;
    private string? _activeModelId;
    private bool _isThinkingMode;
    private int _completedTests;

    public string AgentName { get; set; } = "ModelBoss";

    /// <summary>
    /// Main render loop. Reads messages from the channel and renders immediately.
    /// Tokens stream inline in real-time; completed tests get wrapped in panels.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        RenderHeader();

        try
        {
            await foreach (var msg in reader.ReadAllAsync(ct))
            {
                ProcessMessage(msg);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public static void RenderSummary(AgentRunSummary summary)
    {
        AnsiConsole.WriteLine();

        var rows = new List<IRenderable>
        {
            new Markup(
                $"[{Fmt(summary.Success ? AgentTheme.Success : AgentTheme.Error)}]" +
                $"{(summary.Success ? "✓ Completed" : "✗ Failed")}[/]"),
            new Markup(
                $"[dim]Duration:[/] {summary.Duration:mm\\:ss}" +
                $"  [dim]Models:[/] {summary.FilesProcessed}" +
                $"  [dim]Tools:[/] {summary.ToolCallCount}"),
            new Markup($"[dim]Output:[/] [{Fmt(AgentTheme.Label)}]{Esc(summary.OutputPath)}[/]"),
        };

        AnsiConsole.Write(new Panel(new Rows(rows))
            .Header($"[{Fmt(AgentTheme.Header)}] Summary [/]")
            .BorderColor(summary.Success ? AgentTheme.Green : AgentTheme.Red)
            .Border(BoxBorder.Double)
            .Expand());
    }

    // ── Message dispatch ──────────────────────────────────────────────

    private void ProcessMessage(UIMessage msg)
    {
        switch (msg)
        {
            case StatusMessage status:
                AnsiConsole.MarkupLine($"  [{Fmt(AgentTheme.Accent)}]●[/] {Esc(status.Text)}");
                break;

            case ModelPhaseStartedMessage phase:
                _phaseTimer.Restart();
                RenderModelPanel(phase);
                break;

            case ModelPhaseCompletedMessage completed:
                FinishActiveTestInline();
                var ok = completed.Success
                    ? $"[{Fmt(AgentTheme.Success)}]✓[/]"
                    : $"[{Fmt(AgentTheme.Error)}]✗[/]";
                AnsiConsole.MarkupLine(
                    $"  {ok} [{Fmt(AgentTheme.Label)}]{Esc(completed.ConfigKey)}[/] completed in {completed.Elapsed:mm\\:ss}");
                AnsiConsole.WriteLine();
                break;

            case TestStartedMessage test:
                FinishActiveTestInline();
                _activePromptName = test.PromptName;
                _activePromptDescription = test.Description;
                _activePromptCategory = test.Category;
                _activeModelId = test.ModelId;
                _activeTokens.Clear();
                _isThinkingMode = false;

                // Render test header rule
                var header = $"{Esc(test.PromptName)} \\[{Esc(test.Category)} L{test.DifficultyLevel}\\]";
                AnsiConsole.Write(new Rule($"[{Fmt(AgentTheme.Label)}]{header}[/]")
                    .RuleStyle(AgentTheme.Dim)
                    .LeftJustified());

                if (!string.IsNullOrEmpty(test.Description))
                {
                    AnsiConsole.MarkupLine($"  [dim]{Esc(test.Description)}[/]");
                }

                AnsiConsole.WriteLine();
                break;

            case ThinkingTokenMessage thinking:
                if (!_isThinkingMode)
                {
                    _isThinkingMode = true;
                    AnsiConsole.MarkupLine($"  [{Fmt(new Style(AgentTheme.Orange, decoration: Decoration.Dim))}]▸ thinking[/]");
                }

                _activeTokens.Append(thinking.Token);
                // Stream thinking tokens in orange
                AnsiConsole.Markup($"[{Fmt(new Style(AgentTheme.Orange))}]{Esc(thinking.Token)}[/]");
                break;

            case ResponseTokenMessage response:
                if (_isThinkingMode)
                {
                    _isThinkingMode = false;
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [{Fmt(new Style(AgentTheme.Cyan, decoration: Decoration.Dim))}]▸ response[/]");
                }

                _activeTokens.Append(response.Token);
                // Stream response tokens in white
                AnsiConsole.Markup($"{Esc(response.Token)}");
                break;

            case TestCompletedMessage result:
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();
                RenderResultBar(result);
                _activePromptName = null;
                _completedTests++;
                AnsiConsole.WriteLine();
                break;

            case ErrorMessage error:
                FinishActiveTestInline();
                AnsiConsole.Write(new Panel(new Markup(
                    $"[{Fmt(AgentTheme.Error)}]{Esc(error.Source)}[/] — {Esc(error.Message)}"))
                    .Header($"[{Fmt(AgentTheme.Error)}] ✗ ERROR [/]")
                    .BorderColor(AgentTheme.Red)
                    .Border(BoxBorder.Rounded)
                    .Expand());
                AnsiConsole.WriteLine();
                break;

            case JudgePhaseStartedMessage judgePhase:
                FinishActiveTestInline();
                AnsiConsole.Write(new Panel(new Markup(
                    $"Evaluating [{Fmt(AgentTheme.Label)}]{judgePhase.ModelsToJudge}[/] model(s)" +
                    $" with [{Fmt(AgentTheme.Header)}]{Esc(judgePhase.JudgeModelId)}[/]"))
                    .Header($"[{Fmt(AgentTheme.Accent)}] ⚖ Judge [/]")
                    .BorderColor(AgentTheme.Orange)
                    .Border(BoxBorder.Heavy)
                    .Expand());
                AnsiConsole.WriteLine();
                break;

            case JudgeResultMessage judgeResult:
                var scoreColor = judgeResult.Score >= 7 ? AgentTheme.Green
                    : judgeResult.Score >= 5 ? AgentTheme.Orange
                    : AgentTheme.Red;
                AnsiConsole.MarkupLine(
                    $"  [dim]{Esc(judgeResult.ModelId)}[/] · [dim]{Esc(judgeResult.PromptName)}[/] → " +
                    $"[{Fmt(new Style(scoreColor, decoration: Decoration.Bold))}]{judgeResult.Score}/10[/]");
                break;
        }
    }

    // ── Panels ────────────────────────────────────────────────────────

    private void RenderHeader()
    {
        var (version, _) = AgentTheme.GetVersionInfo();

        AnsiConsole.Write(new Panel(
            new Markup($"[{Fmt(AgentTheme.Header)}]{Esc(AgentName)}[/] [dim]v{version}[/]"))
            .BorderColor(AgentTheme.Cyan)
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderModelPanel(ModelPhaseStartedMessage phase)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Fmt(AgentTheme.Header)}]{Esc(phase.ModelId)}[/]"),
        };

        var details = new StringBuilder();
        if (phase.ParamsB.HasValue)
        {
            details.Append($"{phase.ParamsB:F1}B");
        }

        if (!string.IsNullOrEmpty(phase.Architecture))
        {
            if (details.Length > 0) details.Append(" · ");
            details.Append(phase.Architecture);
        }

        if (details.Length > 0)
        {
            rows.Add(new Markup($"[dim]{Esc(details.ToString())}[/]"));
        }

        if (!string.IsNullOrEmpty(phase.ModelSummary))
        {
            rows.Add(new Markup($"[italic dim]{Esc(phase.ModelSummary)}[/]"));
        }

        AnsiConsole.Write(new Panel(new Rows(rows))
            .Header($"[{Fmt(AgentTheme.Accent)}] ♦ {Esc(phase.ConfigKey)} [/]")
            .BorderColor(AgentTheme.Orange)
            .Border(BoxBorder.Heavy)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderResultBar(TestCompletedMessage result)
    {
        var passIcon = result.Passed
            ? $"[{Fmt(AgentTheme.Success)}]✓ PASS[/]"
            : $"[{Fmt(AgentTheme.Error)}]✗ FAIL[/]";

        var accuracyColor = result.AccuracyScore >= 0.8 ? AgentTheme.Green
            : result.AccuracyScore >= 0.6 ? AgentTheme.Orange
            : AgentTheme.Red;

        // Metrics line
        AnsiConsole.MarkupLine(
            $"  [{Fmt(AgentTheme.Label)}]{result.TokensPerSecond:F1} tok/s[/]" +
            $"  [dim]TTFT[/] {result.Ttft.TotalMilliseconds:F0}ms" +
            $"  [dim]Accuracy:[/] [{Fmt(new Style(accuracyColor))}]{result.AccuracyScore:F2}[/]" +
            $"  {passIcon}");

        // Check breakdown
        if (result.Checks.Count > 0)
        {
            var parts = result.Checks.Select(c =>
            {
                var icon = c.Score >= 0.9
                    ? $"[{Fmt(AgentTheme.Success)}]✓[/]"
                    : c.Score >= 0.5
                        ? $"[{Fmt(AgentTheme.Accent)}]{c.Score:F2}[/]"
                        : $"[{Fmt(AgentTheme.Error)}]{c.Score:F2}[/]";
                return $"[dim]{Esc(c.Name)}[/]{icon}";
            });

            AnsiConsole.MarkupLine($"  {string.Join("  ", parts)}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void FinishActiveTestInline()
    {
        if (_activePromptName is null)
        {
            return;
        }

        _activeTokens.Clear();
        _activePromptName = null;
    }

    private static string Fmt(Style style) => AgentTheme.FormatStyle(style);
    private static string Esc(string text) => Markup.Escape(text);
}
