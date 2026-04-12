using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

/// <summary>
/// Consumes <see cref="UIMessage"/> records from a channel and renders vertically-extending
/// Spectre.Console panels. Each test gets its own panel — no truncation, no fixed layout.
/// Finalized panels are written once; the active panel updates in-place via Live rendering.
/// </summary>
public sealed class ChannelAgentRenderer(ChannelReader<UIMessage> reader)
{
    private readonly StringBuilder _activeTokens = new();
    private readonly Stopwatch _phaseTimer = new();
    private readonly List<TestCheckResult> _pendingChecks = [];
    private string? _activePromptName;
    private string? _activePromptDescription;
    private string? _activePromptCategory;
    private string? _activeModelId;
    private string? _activeConfigKey;
    private bool _isThinkingMode;
    private int _completedTests;
    private int _totalTests;

    public string AgentName { get; set; } = "ModelBoss";

    /// <summary>
    /// Main render loop. Drains the channel and renders panels as events arrive.
    /// Runs until the channel completes or cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        RenderHeader();

        try
        {
            await foreach (var msg in reader.ReadAllAsync(ct))
            {
                switch (msg)
                {
                    case StatusMessage status:
                        RenderStatus(status.Text);
                        break;

                    case ModelPhaseStartedMessage phase:
                        FinalizeActiveTest();
                        _activeConfigKey = phase.ConfigKey;
                        _activeModelId = phase.ModelId;
                        _phaseTimer.Restart();
                        RenderModelHeader(phase);
                        break;

                    case ModelPhaseCompletedMessage completed:
                        FinalizeActiveTest();
                        RenderModelCompleted(completed);
                        break;

                    case TestStartedMessage test:
                        FinalizeActiveTest();
                        _activePromptName = test.PromptName;
                        _activePromptDescription = test.Description;
                        _activePromptCategory = test.Category;
                        _activeModelId = test.ModelId;
                        _activeTokens.Clear();
                        _isThinkingMode = false;
                        _totalTests++;
                        RenderTestHeader(test);
                        break;

                    case ThinkingTokenMessage thinking:
                        if (!_isThinkingMode)
                        {
                            _isThinkingMode = true;
                            RenderModeMarker("thinking", AgentTheme.Orange);
                        }

                        _activeTokens.Append(thinking.Token);
                        RenderTokenInline(thinking.Token, AgentTheme.Orange);
                        break;

                    case ResponseTokenMessage response:
                        if (_isThinkingMode)
                        {
                            _isThinkingMode = false;
                            AnsiConsole.WriteLine();
                            RenderModeMarker("response", AgentTheme.Cyan);
                        }

                        _activeTokens.Append(response.Token);
                        RenderTokenInline(response.Token, Color.White);
                        break;

                    case TestCompletedMessage result:
                        AnsiConsole.WriteLine();
                        RenderTestResult(result);
                        _activePromptName = null;
                        _completedTests++;
                        break;

                    case ErrorMessage error:
                        FinalizeActiveTest();
                        RenderError(error);
                        break;

                    case JudgePhaseStartedMessage judgePhase:
                        FinalizeActiveTest();
                        RenderJudgeHeader(judgePhase);
                        break;

                    case JudgeResultMessage judgeResult:
                        RenderJudgeResult(judgeResult);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        FinalizeActiveTest();
    }

    public static void RenderSummary(AgentRunSummary summary)
    {
        var content = new Markup(
            $"[{AgentTheme.FormatStyle(summary.Success ? AgentTheme.Success : AgentTheme.Error)}]" +
            $"{(summary.Success ? "Completed" : "Failed")}[/]" +
            $"  [dim]Duration:[/] {summary.Duration:mm\\:ss}" +
            $"  [dim]Models:[/] {summary.FilesProcessed}" +
            $"  [dim]Tools:[/] {summary.ToolCallCount}" +
            $"  [dim]Output:[/] {Markup.Escape(summary.OutputPath)}");

        AnsiConsole.Write(new Panel(content)
            .Header($"[{AgentTheme.FormatStyle(AgentTheme.Header)}] Summary [/]")
            .BorderColor(summary.Success ? AgentTheme.Green : AgentTheme.Red)
            .Border(BoxBorder.Double)
            .Expand());
        AnsiConsole.WriteLine();
    }

    // ── Panel renderers ───────────────────────────────────────────────

    private void RenderHeader()
    {
        var (version, _) = AgentTheme.GetVersionInfo();

        var content = new Markup(
            $"[{AgentTheme.FormatStyle(AgentTheme.Header)}]{Markup.Escape(AgentName)}[/]" +
            $" [dim]v{version}[/]");

        AnsiConsole.Write(new Panel(content)
            .BorderColor(AgentTheme.Cyan)
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderStatus(string text)
    {
        AnsiConsole.MarkupLine($"  [{AgentTheme.FormatStyle(AgentTheme.Accent)}]{Markup.Escape(text)}[/]");
    }

    private static void RenderModelHeader(ModelPhaseStartedMessage phase)
    {
        var details = new StringBuilder();
        details.Append($"[{AgentTheme.FormatStyle(AgentTheme.Header)}]{Markup.Escape(phase.ModelId)}[/]");

        if (phase.ParamsB.HasValue)
        {
            details.Append($"  [dim]{phase.ParamsB:F1}B[/]");
        }

        if (!string.IsNullOrEmpty(phase.Architecture))
        {
            details.Append($"  [dim]{Markup.Escape(phase.Architecture)}[/]");
        }

        if (!string.IsNullOrEmpty(phase.ModelSummary))
        {
            details.AppendLine();
            details.Append($"  [italic dim]{Markup.Escape(phase.ModelSummary)}[/]");
        }

        AnsiConsole.Write(new Panel(new Markup(details.ToString()))
            .Header($"[{AgentTheme.FormatStyle(AgentTheme.Accent)}] {Markup.Escape(phase.ConfigKey)} [/]")
            .BorderColor(AgentTheme.Orange)
            .Border(BoxBorder.Heavy)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderModelCompleted(ModelPhaseCompletedMessage completed)
    {
        var icon = completed.Success ? "[green]OK[/]" : "[red]FAILED[/]";
        AnsiConsole.MarkupLine($"  {icon}  [dim]{Markup.Escape(completed.ConfigKey)}[/] completed in {completed.Elapsed:mm\\:ss}");
        AnsiConsole.WriteLine();
    }

    private static void RenderTestHeader(TestStartedMessage test)
    {
        var header = $"{Markup.Escape(test.PromptName)} [{Markup.Escape(test.Category)} L{test.DifficultyLevel}]";

        AnsiConsole.Write(new Rule($"[{AgentTheme.FormatStyle(AgentTheme.Label)}]{header}[/]")
            .RuleStyle(AgentTheme.Dim)
            .LeftJustified());

        if (!string.IsNullOrEmpty(test.Description))
        {
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(test.Description)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    private static void RenderModeMarker(string mode, Color color)
    {
        AnsiConsole.MarkupLine($"  [{AgentTheme.FormatStyle(new Style(color, decoration: Decoration.Dim))}][{mode}][/]");
    }

    private static void RenderTokenInline(string token, Color color)
    {
        AnsiConsole.Markup($"[{AgentTheme.FormatStyle(new Style(color))}]{Markup.Escape(token)}[/]");
    }

    private static void RenderTestResult(TestCompletedMessage result)
    {
        var passIcon = result.Passed
            ? $"[{AgentTheme.FormatStyle(AgentTheme.Success)}]PASS[/]"
            : $"[{AgentTheme.FormatStyle(AgentTheme.Error)}]FAIL[/]";

        var accuracyColor = result.AccuracyScore >= 0.8 ? AgentTheme.Green
            : result.AccuracyScore >= 0.6 ? AgentTheme.Orange
            : AgentTheme.Red;

        AnsiConsole.MarkupLine(
            $"  [{AgentTheme.FormatStyle(AgentTheme.Label)}]{result.TokensPerSecond:F1} tok/s[/]" +
            $"  [dim]TTFT[/] {result.Ttft.TotalMilliseconds:F0}ms" +
            $"  [dim]Accuracy:[/] [{AgentTheme.FormatStyle(new Style(accuracyColor))}]{result.AccuracyScore:F2}[/]" +
            $"  {passIcon}");

        if (result.Checks.Count > 0)
        {
            var checksSummary = string.Join("  ", result.Checks.Select(c =>
            {
                var icon = c.Score >= 0.9 ? "[green]OK[/]" : c.Score >= 0.5 ? $"[yellow]{c.Score:F2}[/]" : $"[red]{c.Score:F2}[/]";
                return $"[dim]{Markup.Escape(c.Name)}[/] {icon}";
            }));

            AnsiConsole.MarkupLine($"  {checksSummary}");
        }

        AnsiConsole.WriteLine();
    }

    private static void RenderError(ErrorMessage error)
    {
        var content = new Markup(
            $"[{AgentTheme.FormatStyle(AgentTheme.Error)}]{Markup.Escape(error.Source)}[/]" +
            $" — {Markup.Escape(error.Message)}");

        AnsiConsole.Write(new Panel(content)
            .Header($"[{AgentTheme.FormatStyle(AgentTheme.Error)}] ERROR [/]")
            .BorderColor(AgentTheme.Red)
            .Border(BoxBorder.Rounded)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderJudgeHeader(JudgePhaseStartedMessage phase)
    {
        AnsiConsole.Write(new Rule(
            $"[{AgentTheme.FormatStyle(AgentTheme.Accent)}]Judge: {Markup.Escape(phase.JudgeModelId)} scoring {phase.ModelsToJudge} model(s)[/]")
            .RuleStyle(AgentTheme.Orange)
            .LeftJustified());
        AnsiConsole.WriteLine();
    }

    private static void RenderJudgeResult(JudgeResultMessage result)
    {
        var scoreColor = result.Score >= 7 ? AgentTheme.Green
            : result.Score >= 5 ? AgentTheme.Orange
            : AgentTheme.Red;

        AnsiConsole.MarkupLine(
            $"  [dim]{Markup.Escape(result.ModelId)}[/] on [dim]{Markup.Escape(result.PromptName)}[/]: " +
            $"[{AgentTheme.FormatStyle(new Style(scoreColor, decoration: Decoration.Bold))}]{result.Score}/10[/]");
    }

    private void FinalizeActiveTest()
    {
        if (_activePromptName is null)
        {
            return;
        }

        _activeTokens.Clear();
        _activePromptName = null;
    }
}
