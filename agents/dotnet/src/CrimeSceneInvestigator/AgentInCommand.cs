using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Telemetry;
using Agent.SDK.Tools;
using CrimeSceneInvestigator.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CrimeSceneInvestigator;

/// <summary>
/// Core agent logic: resolves CLI options, builds the M.E.AI pipeline, and
/// runs the crime-scene investigation agent. Called from the System.CommandLine action.
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

        var output = AgentConsole.Output;
        var stopwatch = Stopwatch.StartNew();

        await output.StartAsync("Crime Scene Investigator", ct);

        using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("csi.target", context.TargetPath);

        try
        {
            // ── Plan model assignments ─────────────────────────────────────

            var scanOptions = context.ScanOptions;
            var contextDir = Path.Combine(context.TargetPath, "context");

            var modelPlan = await PlanScannerModelsAsync(context, scanOptions, loadedModels, ct);

            // ── Run scanners sequentially ──────────────────────────────────

            if (scanOptions.ScanMarkdown)
            {
                await RunScannerAsync(context, "Markdown Scanner",
                    SystemPrompt.Build(context.TargetPath, context.OutputPath),
                    $"Investigate the markdown files in: {context.TargetPath}",
                    [
                        AIFunctionFactory.Create(FileTools.ListMarkdownFiles),
                        AIFunctionFactory.Create(FileTools.ReadFileContent),
                        AIFunctionFactory.Create(FileTools.ExtractStructure),
                        AIFunctionFactory.Create(FileTools.WriteOutput),
                    ], ct, expectedOutputPath: context.OutputPath,
                    modelOverride: modelPlan.GetValueOrDefault("markdown"));
            }

            if (scanOptions.ScanCodeComments)
            {
                var rulesPath = Path.Combine(contextDir, "RULES.md");
                await RunScannerAsync(context, "Rules Scanner",
                    RulesPrompt.Build(context.TargetPath, rulesPath),
                    $"Analyze code comments and patterns in: {context.TargetPath}",
                    [
                        AIFunctionFactory.Create(CodeCommentTools.ListSourceFiles),
                        AIFunctionFactory.Create(CodeCommentTools.ExtractComments),
                        AIFunctionFactory.Create(CodeCommentTools.ExtractCodePatterns),
                        AIFunctionFactory.Create(FileTools.ReadFileContent),
                        AIFunctionFactory.Create(FileTools.WriteOutput),
                    ], ct, expectedOutputPath: rulesPath,
                    modelOverride: modelPlan.GetValueOrDefault("rules"));
            }

            if (scanOptions.ScanCodePattern)
            {
                var structurePath = Path.Combine(contextDir, "STRUCTURE.md");
                await RunScannerAsync(context, "Structure Scanner",
                    StructurePrompt.Build(context.TargetPath, structurePath),
                    $"Analyze project structure in: {context.TargetPath}",
                    [
                        AIFunctionFactory.Create(StructureTools.ListProjects),
                        AIFunctionFactory.Create(StructureTools.ReadProjectFile),
                        AIFunctionFactory.Create(StructureTools.MapDependencyGraph),
                        AIFunctionFactory.Create(StructureTools.DetectArchitecturePattern),
                        AIFunctionFactory.Create(FileTools.WriteOutput),
                    ], ct, expectedOutputPath: structurePath,
                    modelOverride: modelPlan.GetValueOrDefault("structure"));

                var qualityPath = Path.Combine(contextDir, "QUALITY.md");
                await RunScannerAsync(context, "Quality Scanner",
                    QualityPrompt.Build(context.TargetPath, qualityPath),
                    $"Analyze code quality in: {context.TargetPath}",
                    [
                        AIFunctionFactory.Create(QualityTools.AnalyzeCSharpFile),
                        AIFunctionFactory.Create(QualityTools.AnalyzeCSharpProject),
                        AIFunctionFactory.Create(QualityTools.AnalyzeSourceFile),
                        AIFunctionFactory.Create(QualityTools.CheckEditorConfig),
                        AIFunctionFactory.Create(StructureTools.ListProjects),
                        AIFunctionFactory.Create(CodeCommentTools.ListSourceFiles),
                        AIFunctionFactory.Create(FileTools.ReadFileContent),
                        AIFunctionFactory.Create(FileTools.WriteOutput),
                    ], ct, expectedOutputPath: qualityPath,
                    modelOverride: modelPlan.GetValueOrDefault("quality"));
            }

            if (scanOptions.ScanGitHistory)
            {
                var journalPath = Path.Combine(contextDir, "JOURNAL.md");
                await RunScannerAsync(context, "Journal Scanner",
                    JournalPrompt.Build(context.TargetPath, Path.Combine(contextDir, "journal")),
                    $"Analyze git history in: {context.TargetPath}",
                    [
                        AIFunctionFactory.Create(GitTools.GetGitLog),
                        AIFunctionFactory.Create(GitTools.GetGitDiff),
                        AIFunctionFactory.Create(GitTools.GetGitStats),
                        AIFunctionFactory.Create(GitTools.CheckJournalExists),
                        AIFunctionFactory.Create(FileTools.WriteOutput),
                    ], ct, expectedOutputPath: journalPath,
                    modelOverride: modelPlan.GetValueOrDefault("journal"));
            }

            // DONE.md always runs last — aggregates all prior scanner results
            var donePath = Path.Combine(contextDir, "DONE.md");
            await RunScannerAsync(context, "Done Scanner",
                DonePrompt.Build(context.TargetPath, donePath),
                $"Produce a completion checklist for: {context.TargetPath}",
                [
                    AIFunctionFactory.Create(StructureTools.ListProjects),
                    AIFunctionFactory.Create(CodeCommentTools.ListSourceFiles),
                    AIFunctionFactory.Create(FileTools.ListMarkdownFiles),
                    AIFunctionFactory.Create(FileTools.ReadFileContent),
                    AIFunctionFactory.Create(FileTools.WriteOutput),
                ], ct, expectedOutputPath: donePath,
                modelOverride: modelPlan.GetValueOrDefault("done"));

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

            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);

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

    // ── Planner ─────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the LLM to assign a model config key to each scanner based on
    /// what's loaded and what each scanner needs. Returns a dictionary of
    /// scanner name → <see cref="AgentModelOptions"/>.
    /// </summary>
    private async Task<Dictionary<string, AgentModelOptions>> PlanScannerModelsAsync(
        AgentContext context,
        AgentScanOptions scanOptions,
        List<string> loadedModels,
        CancellationToken ct)
    {
        var output = AgentConsole.Output;
        var allConfigs = AgentModelOptions.ResolveAll(Configuration);

        // Filter to configs whose model is actually loaded
        var availableConfigs = allConfigs
            .Where(kv => loadedModels.Any(loaded =>
                loaded.Contains(kv.Value.Model, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Model.Contains(loaded, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // If 0 or 1 available models, skip planning — use context default for all
        if (availableConfigs.Count <= 1)
        {
            Logger.LogInformation("Skipping planner: {Count} model(s) available, using default for all scanners",
                availableConfigs.Count);
            return [];
        }

        // Build enabled scanner list
        var enabledScanners = new List<string>();
        if (scanOptions.ScanMarkdown) enabledScanners.Add("markdown");
        if (scanOptions.ScanCodeComments) enabledScanners.Add("rules");
        if (scanOptions.ScanCodePattern) { enabledScanners.Add("structure"); enabledScanners.Add("quality"); }
        if (scanOptions.ScanGitHistory) enabledScanners.Add("journal");
        enabledScanners.Add("done"); // always runs

        output.UpdateStatus($"Planning model assignments ({availableConfigs.Count} models, {enabledScanners.Count} scanners)");
        Logger.LogInformation("Running planner with {ModelCount} available models: {Models}",
            availableConfigs.Count, string.Join(", ", availableConfigs.Keys));

        try
        {
            var systemPrompt = PlannerPrompt.Build(enabledScanners, loadedModels, availableConfigs);

            IChatClient agent = await context.GetAgentClientAsync();
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, "Assign the best model to each scanner. Respond with JSON only."),
            };

            var chatOptions = new ChatOptions();
            if (context.ModelOptions.Temperature.HasValue)
                chatOptions.Temperature = context.ModelOptions.Temperature;
            if (context.ModelOptions.MaxOutputTokens.HasValue)
                chatOptions.MaxOutputTokens = context.ModelOptions.MaxOutputTokens;

            string? responseText = null;
            await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    responseText = (responseText ?? "") + text;
                }
            }

            if (responseText is null or { Length: 0 })
            {
                Logger.LogWarning("Planner returned empty response, using default model for all scanners");
                return [];
            }

            // Parse the JSON from the response (may be wrapped in ```json ... ```)
            var plan = ParsePlannerResponse(responseText, allConfigs);

            foreach (var (scanner, options) in plan)
            {
                Logger.LogInformation("Planner assigned {Scanner} → {Model}", scanner, options.Model);
                output.UpdateStatus($"Plan: {scanner} → {options.Model}");
            }

            return plan;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Planner failed, using default model for all scanners");
            return [];
        }
    }

    /// <summary>
    /// Extracts the JSON object from the planner response and maps config keys to model options.
    /// </summary>
    private static Dictionary<string, AgentModelOptions> ParsePlannerResponse(
        string response, Dictionary<string, AgentModelOptions> allConfigs)
    {
        var result = new Dictionary<string, AgentModelOptions>(StringComparer.OrdinalIgnoreCase);

        // Extract JSON from markdown code fences or raw text
        var jsonMatch = Regex.Match(response, @"\{[^}]+\}", RegexOptions.Singleline);
        if (!jsonMatch.Success) return result;

        try
        {
            var assignments = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonMatch.Value);
            if (assignments is null) return result;

            foreach (var (scanner, configKey) in assignments)
            {
                if (allConfigs.TryGetValue(configKey, out var options))
                {
                    result[scanner] = options;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON from the LLM — fall back to defaults
        }

        return result;
    }

    // ── Scanner runner ──────────────────────────────────────────────────

    /// <summary>
    /// Runs a single scanner: one LLM call with its own system prompt and tool set.
    /// If the model produces text output but doesn't call WriteOutput, the text is
    /// saved to <paramref name="expectedOutputPath"/> as a fallback.
    /// </summary>
    private async Task RunScannerAsync(
        AgentContext context,
        string scannerName,
        string systemPrompt,
        string userMessage,
        IList<AITool> tools,
        CancellationToken ct,
        string? expectedOutputPath = null,
        AgentModelOptions? modelOverride = null)
    {
        var output = AgentConsole.Output;
        var activeModel = modelOverride ?? context.ModelOptions;
        output.UpdateStatus($"Running: {scannerName} [{activeModel.Model}]");
        Logger.LogInformation("Starting scanner: {ScannerName} with model {Model}", scannerName, activeModel.Model);

        IChatClient agent = await context.GetAgentClientAsync(modelOverride);

        var chatOptions = new ChatOptions
        {
            Tools = tools.WithProgress(output),
        };

        if (activeModel.Temperature.HasValue)
        {
            chatOptions.Temperature = activeModel.Temperature;
        }

        if (activeModel.TopP.HasValue)
        {
            chatOptions.TopP = activeModel.TopP;
        }

        if (activeModel.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = activeModel.MaxOutputTokens;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            string? responseText = null;
            await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    responseText = (responseText ?? "") + text;
                }
            }

            if (responseText is { Length: > 0 })
            {
                var preview = responseText.Length > 200 ? responseText[..200] + "..." : responseText;
                Logger.LogInformation("Scanner {ScannerName} response: {Preview}", scannerName, preview);

                // Fallback: if the model produced markdown output as text but didn't
                // call WriteOutput, save it to the expected path automatically.
                if (expectedOutputPath is not null && !File.Exists(expectedOutputPath))
                {
                    var trimmed = responseText.TrimStart();
                    // Accept any substantive markdown content (headers, lists, text)
                    if (trimmed.Length > 50)
                    {
                        var dir = Path.GetDirectoryName(expectedOutputPath);
                        if (dir is not null && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.WriteAllText(expectedOutputPath, trimmed);
                        Logger.LogInformation("Fallback write: saved {ScannerName} output to {Path}",
                            scannerName, expectedOutputPath);
                    }
                }
            }
            else
            {
                Logger.LogWarning("Scanner {ScannerName} produced no text response", scannerName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Scanner {ScannerName} failed: {Message}", scannerName, ex.Message);
            output.UpdateStatus($"Scanner {scannerName} failed: {ex.Message}");
            // Continue to next scanner — don't abort the whole run
        }

        Logger.LogInformation("Completed scanner: {ScannerName}", scannerName);
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

        var configKey = parseResult.GetValue(AgentCommandSetup.ConfigKeyOption);
        var modelOptions = AgentModelOptions.Resolve(Configuration, configKey);

        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(targetPath, "context", "MAP.md");

        // ── Resolve scan options ────────────────────────────────────────
        var scanOverride = parseResult.GetValue(AgentCommandSetup.ScanOption);
        var scanOptions = AgentScanOptions.FromCliOverride(scanOverride)
            ?? AgentScanOptions.Resolve(Configuration);

        var output = AgentConsole.Output;
        var modelDisplay = string.IsNullOrEmpty(modelOptions.Model) ? "(server default)" : modelOptions.Model;

        output.UpdateStatus($"Target: {targetPath}");
        output.UpdateStatus($"Config: Models:{configKey ?? AgentModelOptions.DefaultKey}");
        output.UpdateStatus($"Endpoint: {modelOptions.Endpoint}");
        output.UpdateStatus($"Model: {modelDisplay}");
        output.UpdateStatus($"Output: {outputPath}");

        // ── Validate endpoint + model ───────────────────────────────────

        var health = await EndpointHealthCheck.ValidateAsync(modelOptions, ct: ct);

        if (!health.IsHealthy)
        {
            output.UpdateStatus($"Endpoint health check failed: {health.Error}");
            return (1, null, []);
        }

        output.UpdateStatus($"Endpoint healthy - {health.LoadedModels.Count} model(s) loaded");

        if (!health.IsModelLoaded)
        {
            output.UpdateStatus($"Model not loaded: {health.Error}");
            return (1, null, []);
        }

        // ── Set root directory for file tools ───────────────────────────

        FileTools.RootDirectory = targetPath;

        var ctx = new AgentContext(targetPath, outputPath, modelOptions, [], scanOptions);
        return (0, ctx, health.LoadedModels);
    }
}
