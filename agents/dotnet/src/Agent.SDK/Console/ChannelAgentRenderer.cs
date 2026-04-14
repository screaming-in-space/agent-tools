using System.Text;
using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

/// <summary>
/// Consumes <see cref="UIMessage"/> records from a channel and renders vertically-extending
/// Spectre.Console panels. Each test accumulates tokens silently, then renders a complete
/// panel when the test finishes — description, output preview, metrics, and checks.
/// </summary>
public sealed class ChannelAgentRenderer(ChannelReader<UIMessage> reader)
{
    private readonly StringBuilder _thinkingTokens = new();
    private readonly StringBuilder _responseTokens = new();

    private string? _activePromptName;
    private string? _activePromptDescription;
    private string? _activePromptCategory;
    private int _activeDifficulty;

    public string AgentName { get; set; } = "ModelBoss";

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

        var table = new Table().Border(TableBorder.None).HideHeaders().AddColumn("k").AddColumn("v");
        table.AddRow(
            new Markup("[dim]Status[/]"),
            new Markup(summary.Success
                ? $"[{Fmt(AgentTheme.Success)}]✓ Completed[/]"
                : $"[{Fmt(AgentTheme.Error)}]✗ Failed[/]"));
        table.AddRow(new Markup("[dim]Duration[/]"), new Markup($"{summary.Duration:mm\\:ss}"));
        table.AddRow(new Markup("[dim]Models[/]"), new Markup($"{summary.FilesProcessed}"));
        table.AddRow(new Markup("[dim]Output[/]"), new Markup($"[{Fmt(AgentTheme.Label)}]{Esc(summary.OutputPath)}[/]"));

        AnsiConsole.Write(new Panel(table)
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
                RenderModelPanel(phase);
                break;

            case ModelPhaseCompletedMessage completed:
                var icon = completed.Success ? $"[{Fmt(AgentTheme.Success)}]✓[/]" : $"[{Fmt(AgentTheme.Error)}]✗[/]";
                AnsiConsole.MarkupLine($"  {icon} [{Fmt(AgentTheme.Label)}]{Esc(completed.ConfigKey)}[/] completed in {completed.Elapsed:mm\\:ss}");
                AnsiConsole.WriteLine();
                break;

            case TestStartedMessage test:
                _activePromptName = test.PromptName;
                _activePromptDescription = test.Description;
                _activePromptCategory = test.Category;
                _activeDifficulty = test.DifficultyLevel;
                _thinkingTokens.Clear();
                _responseTokens.Clear();
                break;

            case ThinkingTokenMessage thinking:
                _thinkingTokens.Append(thinking.Token);
                break;

            case ResponseTokenMessage response:
                _responseTokens.Append(response.Token);
                break;

            case TestCompletedMessage result:
                RenderTestPanel(result);
                _activePromptName = null;
                break;

            case ModelSummaryMessage summary:
                RenderModelSummary(summary);
                break;

            case ErrorMessage error:
                RenderErrorPanel(error);
                break;

            case JudgePhaseStartedMessage judgePhase:
                RenderJudgePanel(judgePhase);
                break;

            case JudgeResultMessage judgeResult:
                RenderJudgeScore(judgeResult);
                break;
        }
    }

    // ── Test panel (the core visual unit) ─────────────────────────────

    private void RenderTestPanel(TestCompletedMessage result)
    {
        var rows = new List<IRenderable>();

        // ── Description ───────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_activePromptDescription))
        {
            rows.Add(new Markup($"[dim]{Esc(_activePromptDescription)}[/]"));
        }

        rows.Add(Text.Empty);

        // ── Thinking (compact preview in dim orange) ──────────────────
        var thinking = _thinkingTokens.ToString().Trim();
        if (thinking.Length > 0)
        {
            var preview = thinking.Length > 200
                ? thinking[..200].Replace("\n", " ") + " …"
                : thinking.Replace("\n", " ");

            rows.Add(new Panel(new Markup($"[{Fmt(new Style(AgentTheme.Orange))}]{Esc(preview)}[/]"))
                .Header($"[{Fmt(new Style(AgentTheme.Orange, decoration: Decoration.Dim))}]thinking[/]")
                .BorderColor(AgentTheme.Dim)
                .Border(BoxBorder.Ascii)
                .Expand());
        }

        rows.Add(Text.Empty);

        // ── Response output ───────────────────────────────────────────
        var response = _responseTokens.ToString().Trim();
        if (response.Length > 0)
        {
            var lines = response.Split('\n');
            var displayLines = lines.Length > 8
                ? [.. lines.Take(8), $"[dim]… ({lines.Length - 8} more lines)[/]"]
                : lines.Select(Esc).ToArray();

            var outputText = string.Join('\n', displayLines);

            rows.Add(new Panel(new Markup(outputText))
                .Header("[dim]response[/]")
                .BorderColor(AgentTheme.Sky)
                .Border(BoxBorder.Rounded)
                .Expand());
        }

        rows.Add(Text.Empty);

        // ── Metrics line ──────────────────────────────────────────────
        var passMarkup = result.Passed
            ? $"[{Fmt(AgentTheme.Success)}]✓ PASS[/]"
            : $"[{Fmt(AgentTheme.Error)}]✗ FAIL[/]";

        var accColor = result.AccuracyScore >= 0.8 ? AgentTheme.Green
            : result.AccuracyScore >= 0.6 ? AgentTheme.Orange
            : AgentTheme.Red;

        rows.Add(new Markup(
            $"{passMarkup}" +
            $"   [{Fmt(AgentTheme.Label)}]{result.TokensPerSecond:F1} tok/s[/]" +
            $"   [dim]TTFT[/] {result.Ttft.TotalMilliseconds:F0}ms" +
            $"   [dim]Accuracy[/] [{Fmt(new Style(accColor, decoration: Decoration.Bold))}]{result.AccuracyScore:F2}[/]"));

        // ── Check breakdown ───────────────────────────────────────────
        if (result.Checks.Count > 0)
        {
            var parts = result.Checks.Select(c =>
            {
                var ci = c.Score >= 0.9
                    ? $"[{Fmt(AgentTheme.Success)}]✓[/]"
                    : c.Score >= 0.5
                        ? $"[{Fmt(AgentTheme.Accent)}]{c.Score:F2}[/]"
                        : $"[{Fmt(AgentTheme.Error)}]{c.Score:F2}[/]";
                return $"[dim]{Esc(c.Name)}[/] {ci}";
            });
            rows.Add(new Markup(string.Join("   ", parts)));
        }

        // ── Assemble panel ────────────────────────────────────────────
        var borderColor = result.Passed ? AgentTheme.Green : AgentTheme.Red;
        var cat = Esc(_activePromptCategory ?? "");
        var name = Esc(_activePromptName ?? result.PromptName);
        var header = $"[{Fmt(AgentTheme.Label)}] {name} [/][dim][[{cat} L{_activeDifficulty}]][/]";

        AnsiConsole.Write(new Panel(new Rows(rows))
            .Header(header)
            .BorderColor(borderColor)
            .Border(BoxBorder.Rounded)
            .Expand());
    }

    // ── Model phase panel ─────────────────────────────────────────────

    private static void RenderModelPanel(ModelPhaseStartedMessage phase)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Fmt(AgentTheme.Header)}]{Esc(phase.ModelId)}[/]"),
        };

        var details = new StringBuilder();
        if (phase.ParamsB.HasValue) { details.Append($"{phase.ParamsB:F1}B"); }
        if (!string.IsNullOrEmpty(phase.Architecture))
        {
            if (details.Length > 0) { details.Append(" · "); }
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

    // ── Model summary panel ─────────────────────────────────────────────

    private static void RenderModelSummary(ModelSummaryMessage s)
    {
        var compositeColor = s.CompositeScore >= 0.8 ? AgentTheme.Green
            : s.CompositeScore >= 0.6 ? AgentTheme.Orange
            : AgentTheme.Red;

        var accColor = s.MeanAccuracy >= 0.8 ? AgentTheme.Green
            : s.MeanAccuracy >= 0.6 ? AgentTheme.Orange
            : AgentTheme.Red;

        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn("k", c => c.PadRight(2)).AddColumn("v");

        table.AddRow(
            new Markup("[dim]Composite[/]"),
            new Markup($"[{Fmt(new Style(compositeColor, decoration: Decoration.Bold))}]{s.CompositeScore:F3}[/]"));
        table.AddRow(
            new Markup("[dim]Accuracy[/]"),
            new Markup($"[{Fmt(new Style(accColor))}]{s.MeanAccuracy:F3}[/]"));
        table.AddRow(
            new Markup("[dim]Speed[/]"),
            new Markup($"[{Fmt(AgentTheme.Label)}]{s.MedianTokS:F1} tok/s[/]"));
        table.AddRow(
            new Markup("[dim]Pass rate[/]"),
            new Markup($"{s.Passed}/{s.Total} ({s.PassRate:P0})"));

        AnsiConsole.Write(new Panel(table)
            .Header($"[{Fmt(AgentTheme.Header)}] {Esc(s.ModelId)} [/]")
            .BorderColor(compositeColor)
            .Border(BoxBorder.Double)
            .Expand());
        AnsiConsole.WriteLine();
    }

    // ── Error panel ───────────────────────────────────────────────────

    private static void RenderErrorPanel(ErrorMessage error)
    {
        AnsiConsole.Write(new Panel(new Markup(
            $"[{Fmt(AgentTheme.Error)}]{Esc(error.Source)}[/] — {Esc(error.Message)}"))
            .Header($"[{Fmt(AgentTheme.Error)}] ✗ ERROR [/]")
            .BorderColor(AgentTheme.Red)
            .Border(BoxBorder.Rounded)
            .Expand());
    }

    // ── Judge panels ──────────────────────────────────────────────────

    private static void RenderJudgePanel(JudgePhaseStartedMessage phase)
    {
        AnsiConsole.Write(new Panel(new Markup(
            $"Evaluating [{Fmt(AgentTheme.Label)}]{phase.ModelsToJudge}[/] model(s)" +
            $" with [{Fmt(AgentTheme.Header)}]{Esc(phase.JudgeModelId)}[/]"))
            .Header($"[{Fmt(AgentTheme.Accent)}] ⚖ Judge [/]")
            .BorderColor(AgentTheme.Orange)
            .Border(BoxBorder.Heavy)
            .Expand());
        AnsiConsole.WriteLine();
    }

    private static void RenderJudgeScore(JudgeResultMessage result)
    {
        var scoreColor = result.Score >= 7 ? AgentTheme.Green
            : result.Score >= 5 ? AgentTheme.Orange
            : AgentTheme.Red;
        AnsiConsole.MarkupLine(
            $"  [dim]{Esc(result.ModelId)}[/] · [dim]{Esc(result.PromptName)}[/] → " +
            $"[{Fmt(new Style(scoreColor, decoration: Decoration.Bold))}]{result.Score}/10[/]");
    }

    // ── Header ────────────────────────────────────────────────────────

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

    // ── Helpers ────────────────────────────────────────────────────────

    private static string Fmt(Style style) => AgentTheme.FormatStyle(style);
    private static string Esc(string text) => Markup.Escape(text);
}
