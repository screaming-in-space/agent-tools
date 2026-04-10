using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Telemetry;
using Agent.SDK.Tools;
using CrimeSceneInvestigator.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrimeSceneInvestigator;

/// <summary>
/// Core agent orchestrator: resolves CLI options, plans model assignments,
/// runs scanners sequentially, and produces the context directory.
/// </summary>
public record AgentInCommand(ILogger<AgentInCommand> Logger, IConfiguration Configuration)
{
    /// <summary>
    /// Runs the crime scene investigator agent with the parsed CLI values.
    /// Returns <c>0</c> on success, <c>1</c> on failure.
    /// </summary>
    public async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        var (exitCode, context, loadedModels) = await SetupAsync(parseResult, ct);
        if (context is null)
        {
            return exitCode;
        }

        using var _ = context;

        var output = AgentConsole.Output;
        var contextDir = Path.Combine(context.TargetPath, "context");
        await AgentErrorLog.InitAsync(contextDir);
        await AgentDebugLog.InitAsync(contextDir);
        var stopwatch = Stopwatch.StartNew();

        await output.StartAsync("Crime Scene Investigator", ct);

        using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("csi.target", context.TargetPath);

        try
        {
            // ── Plan model assignments ─────────────────────────────────────

            var planner = new ScannerPlanner(Logger, Configuration);
            var modelPlan = await planner.PlanAsync(context, context.ScanOptions, loadedModels, ct);

            // ── Run scanners sequentially ──────────────────────────────────

            var runner = new ScannerRunner(Logger);
            await RunAllScannersAsync(context, runner, modelPlan, contextDir, ct);

            stopwatch.Stop();
            CsiMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: output.ToolCallCount,
                FilesProcessed: output.ToolCallCount,
                Duration: stopwatch.Elapsed,
                OutputPath: Path.GetRelativePath(context.TargetPath, contextDir),
                FullOutputPath: contextDir,
                Success: true), ct);

            await AgentDebugLog.CloseAsync();
            await AgentErrorLog.CloseAsync();
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);
            await AgentErrorLog.LogAsync("Agent", $"Run failed: {ex.Message}", ex);
            await AgentDebugLog.CloseAsync();
            await AgentErrorLog.CloseAsync();

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: output.ToolCallCount,
                FilesProcessed: output.ToolCallCount,
                Duration: stopwatch.Elapsed,
                OutputPath: Path.GetRelativePath(context.TargetPath, context.OutputPath),
                FullOutputPath: context.OutputPath,
                Success: false), ct);

            Logger.LogError(ex, "Agent run failed");
            return 1;
        }
    }

    // ── Scanner orchestration ──────────────────────────────────────────

    private static async Task RunAllScannersAsync(
        AgentContext context,
        ScannerRunner runner,
        Dictionary<string, AgentModelOptions> modelPlan,
        string contextDir,
        CancellationToken ct)
    {
        var scanOptions = context.ScanOptions;
        var fileTools = new FileTools(context.TargetPath, contextDir);
        var codeCommentTools = new CodeCommentTools(fileTools);
        var gitTools = new GitTools(fileTools);
        var structureTools = new StructureTools(fileTools);
        var qualityTools = new QualityTools(fileTools);

        if (scanOptions.ScanMarkdown)
        {
            fileTools.ResetReadTracking();
            await runner.RunAsync(context, "Markdown Scanner",
                SystemPrompt.Build(context.TargetPath, context.OutputPath),
                $"Investigate the markdown files in: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(fileTools.ListMarkdownFiles),
                    AIFunctionFactory.Create(fileTools.ReadFileContent),
                    AIFunctionFactory.Create(FileTools.ExtractStructure),
                    AIFunctionFactory.Create(fileTools.WriteOutput),
                ], ct, expectedOutputPath: context.OutputPath,
                modelOverride: modelPlan.GetValueOrDefault("markdown"),
                timeout: TimeSpan.FromMinutes(2));
        }

        if (scanOptions.ScanCodeComments)
        {
            fileTools.ResetReadTracking();
            var rulesPath = Path.Combine(contextDir, "RULES.md");
            await runner.RunAsync(context, "Rules Scanner",
                RulesPrompt.Build(context.TargetPath, rulesPath),
                $"Analyze code comments and patterns in: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(codeCommentTools.ListSourceFiles),
                    AIFunctionFactory.Create(codeCommentTools.ExtractComments),
                    AIFunctionFactory.Create(codeCommentTools.ExtractCodePatterns),
                    AIFunctionFactory.Create(fileTools.ReadFileContent),
                    AIFunctionFactory.Create(fileTools.WriteOutput),
                ], ct, expectedOutputPath: rulesPath,
                modelOverride: modelPlan.GetValueOrDefault("rules"),
                timeout: TimeSpan.FromMinutes(4));
        }

        if (scanOptions.ScanCodePattern)
        {
            fileTools.ResetReadTracking();
            var structurePath = Path.Combine(contextDir, "STRUCTURE.md");
            await runner.RunAsync(context, "Structure Scanner",
                StructurePrompt.Build(context.TargetPath, structurePath),
                $"Analyze project structure in: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(structureTools.ListProjects),
                    AIFunctionFactory.Create(structureTools.ReadProjectFile),
                    AIFunctionFactory.Create(structureTools.MapDependencyGraph),
                    AIFunctionFactory.Create(structureTools.DetectArchitecturePattern),
                    AIFunctionFactory.Create(fileTools.WriteOutput),
                ], ct, expectedOutputPath: structurePath,
                modelOverride: modelPlan.GetValueOrDefault("structure"),
                timeout: TimeSpan.FromMinutes(3));

            fileTools.ResetReadTracking();
            var qualityPath = Path.Combine(contextDir, "QUALITY.md");
            await runner.RunAsync(context, "Quality Scanner",
                QualityPrompt.Build(context.TargetPath, qualityPath),
                $"Analyze code quality in: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(qualityTools.AnalyzeCSharpFile),
                    AIFunctionFactory.Create(qualityTools.AnalyzeCSharpProject),
                    AIFunctionFactory.Create(qualityTools.AnalyzeSourceFile),
                    AIFunctionFactory.Create(qualityTools.CheckEditorConfig),
                    AIFunctionFactory.Create(structureTools.ListProjects),
                    AIFunctionFactory.Create(codeCommentTools.ListSourceFiles),
                    AIFunctionFactory.Create(fileTools.ReadFileContent),
                    AIFunctionFactory.Create(fileTools.WriteOutput),
                ], ct, expectedOutputPath: qualityPath,
                modelOverride: modelPlan.GetValueOrDefault("quality"),
                timeout: TimeSpan.FromMinutes(4));
        }

        if (scanOptions.ScanGitHistory)
        {
            fileTools.ResetReadTracking();
            var journalPath = Path.Combine(contextDir, "JOURNAL.md");
            await runner.RunAsync(context, "Journal Scanner",
                JournalPrompt.Build(context.TargetPath, Path.Combine(contextDir, "journal")),
                $"Analyze git history in: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(gitTools.GetGitLog),
                    AIFunctionFactory.Create(gitTools.GetGitDiff),
                    AIFunctionFactory.Create(gitTools.GetGitStats),
                    AIFunctionFactory.Create(gitTools.CheckJournalExists),
                    AIFunctionFactory.Create(fileTools.WriteOutput),
                ], ct, expectedOutputPath: journalPath,
                modelOverride: modelPlan.GetValueOrDefault("journal"),
                timeout: TimeSpan.FromMinutes(3));
        }

        // DONE.md always runs last — aggregates all prior scanner results
        fileTools.ResetReadTracking();
        var donePath = Path.Combine(contextDir, "DONE.md");
        await runner.RunAsync(context, "Done Scanner",
            DonePrompt.Build(context.TargetPath, donePath),
            $"Produce a completion checklist for: {context.TargetPath}",
            [
                AIFunctionFactory.Create(structureTools.ListProjects),
                AIFunctionFactory.Create(codeCommentTools.ListSourceFiles),
                AIFunctionFactory.Create(fileTools.ListMarkdownFiles),
                AIFunctionFactory.Create(fileTools.ReadFileContent),
                AIFunctionFactory.Create(fileTools.WriteOutput),
            ], ct, expectedOutputPath: donePath,
            modelOverride: modelPlan.GetValueOrDefault("done"),
            timeout: TimeSpan.FromMinutes(3));
    }

    // ── Setup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves CLI options, binds model config, validates the endpoint,
    /// registers tools, and returns the context needed to run the agent.
    /// Also returns the list of loaded models for the planner.
    /// Returns exit code <c>1</c> with a <c>null</c> context on failure.
    /// </summary>
    private async Task<(int ExitCode, AgentContext? Context, List<string> LoadedModels)> SetupAsync(
        ParseResult parseResult, CancellationToken ct)
    {
        var directory = parseResult.GetValue(AgentCommandSetup.DirectoryArg)!;
        var targetPath = directory.FullName;

        if (!directory.Exists)
        {
            Logger.LogError("Directory '{TargetPath}' does not exist", targetPath);
            return (1, null, []);
        }

        // ── Validate git repo ───────────────────────────────────────────
        var repoRoot = FileTools.FindRepoRoot(targetPath);
        if (repoRoot is null)
        {
            Logger.LogError("Directory '{TargetPath}' is not inside a git repository. CSI requires a .git root", targetPath);
            return (1, null, []);
        }

        var configKey = parseResult.GetValue(AgentCommandSetup.ConfigKeyOption);
        var modelOverride = parseResult.GetValue(AgentCommandSetup.ModelOverrideOption);
        var modelOptions = AgentModelOptions.Resolve(Configuration, configKey);

        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            modelOptions = modelOptions with { Model = modelOverride };
        }

        var contextDir = Path.Combine(targetPath, "context");
        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(contextDir, "MAP.md");

        // ── Resolve scan options ────────────────────────────────────────
        var scanOverride = parseResult.GetValue(AgentCommandSetup.ScanOption);
        var scanOptions = AgentScanOptions.FromCliOverride(scanOverride)
            ?? AgentScanOptions.Resolve(Configuration);

        var output = AgentConsole.Output;
        var modelDisplay = string.IsNullOrEmpty(modelOptions.Model) ? "(server default)" : modelOptions.Model;

        await output.UpdateStatusAsync($"Target: {targetPath}");
        await output.UpdateStatusAsync($"Config: Models:{configKey ?? AgentModelOptions.DefaultKey}");
        await output.UpdateStatusAsync($"Endpoint: {modelOptions.Endpoint}");
        await output.UpdateStatusAsync($"Model: {modelDisplay}");
        await output.UpdateStatusAsync($"Output: {outputPath}");

        // ── Validate endpoint + model ───────────────────────────────────

        var health = await EndpointHealthCheck.ValidateAsync(modelOptions, ct: ct);

        if (!health.IsHealthy)
        {
            await output.UpdateStatusAsync($"Endpoint health check failed: {health.Error}");
            return (1, null, []);
        }

        await output.UpdateStatusAsync($"Endpoint healthy - {health.LoadedModels.Count} model(s) loaded");

        if (!health.IsModelLoaded)
        {
            await output.UpdateStatusAsync($"Model not loaded: {health.Error}");
            return (1, null, []);
        }

        // ── Set root directory for file tools ───────────────────────────

        var ctx = new AgentContext(targetPath, repoRoot, outputPath, modelOptions, [], scanOptions);
        return (0, ctx, health.LoadedModels);
    }

    // ── Fallback validation ────────────────────────────────────────────

    private static readonly string[] ChatbotPreambles =
        ["I'm", "I am", "Sure", "Here's", "Let me", "Hello", "I can", "I'd be", "How can", "Of course"];

    /// <summary>
    /// Returns <c>true</c> when the content looks like real scanner output
    /// (has markdown structure) rather than a generic chatbot response.
    /// </summary>
    public static bool IsSubstantiveMarkdown(string content)
    {
        if (content.Length < 50)
        {
            return false;
        }

        var hasMarkdownStructure = content.Contains('#')
            || content.Contains("- ")
            || content.Contains("| ");

        if (!hasMarkdownStructure)
        {
            return false;
        }

        var firstLine = content.AsSpan();
        var newline = firstLine.IndexOfAny('\r', '\n');
        if (newline > 0)
        {
            firstLine = firstLine[..newline];
        }

        foreach (var preamble in ChatbotPreambles)
        {
            if (firstLine.StartsWith(preamble, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
