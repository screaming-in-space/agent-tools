using System.CommandLine;
using System.Diagnostics;
using System.Text;
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

            var scanOptions = context.ScanOptions;

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
                    modelOverride: modelPlan.GetValueOrDefault("markdown"),
                    timeout: TimeSpan.FromMinutes(2));
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
                    modelOverride: modelPlan.GetValueOrDefault("rules"),
                    timeout: TimeSpan.FromMinutes(4));
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
                    modelOverride: modelPlan.GetValueOrDefault("structure"),
                    timeout: TimeSpan.FromMinutes(3));

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
                    modelOverride: modelPlan.GetValueOrDefault("quality"),
                    timeout: TimeSpan.FromMinutes(4));
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
                    modelOverride: modelPlan.GetValueOrDefault("journal"),
                    timeout: TimeSpan.FromMinutes(3));
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
                modelOverride: modelPlan.GetValueOrDefault("done"),
                timeout: TimeSpan.FromMinutes(3));

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

        await output.ScannerStartedAsync("Planner", context.ModelOptions.Model);
        Logger.LogInformation("Running planner with {ModelCount} available models: {Models}",
            availableConfigs.Count, string.Join(", ", availableConfigs.Keys));
        var plannerSw = Stopwatch.StartNew();

        try
        {
            var systemPrompt = PlannerPrompt.Build(enabledScanners, loadedModels, availableConfigs);

            var agent = context.GetAgentClient();
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

            var plannerBuf = new StringBuilder();
            await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    plannerBuf.Append(text);
                }
            }

            var responseText = plannerBuf.ToString();
            if (responseText.Length == 0)
            {
                Logger.LogWarning("Planner returned empty response, using default model for all scanners");
                return [];
            }

            // Parse the JSON from the response (may be wrapped in ```json ... ```)
            var plan = ParsePlannerResponse(responseText, allConfigs);

            plannerSw.Stop();
            await output.ScannerCompletedAsync("Planner", plannerSw.Elapsed, success: true);

            foreach (var (scanner, options) in plan)
            {
                Logger.LogInformation("Planner assigned {Scanner} → {Model}", scanner, options.Model);
            }

            return plan;
        }
        catch (Exception ex)
        {
            plannerSw.Stop();
            await output.ScannerCompletedAsync("Planner", plannerSw.Elapsed, success: false);
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

        var cleaned = Regex.Replace(response, @"```(?:json)?", "").Trim();
        var start = cleaned.IndexOf('{');
        if (start < 0) { return result; }

        var depth = 0;
        var end = -1;
        for (var i = start; i < cleaned.Length; i++)
        {
            if (cleaned[i] == '{') { depth++; }
            else if (cleaned[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
        }

        if (end < 0) { return result; }
        var jsonMatch = cleaned.AsSpan(start, end - start + 1);

        try
        {
            var assignments = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonMatch);
            if (assignments is null) return result;

            foreach (var (scanner, configKey) in assignments)
            {
                if (string.Equals(configKey, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    result[scanner] = AgentModelOptions.Skipped;
                }
                else if (allConfigs.TryGetValue(configKey, out var options))
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
    /// Runs a single scanner with timeout and one retry. If the model produces text
    /// output but doesn't call WriteOutput, substantive markdown is saved as a fallback.
    /// </summary>
    private async Task RunScannerAsync(
        AgentContext context,
        string scannerName,
        string systemPrompt,
        string userMessage,
        IList<AITool> tools,
        CancellationToken ct,
        string? expectedOutputPath = null,
        AgentModelOptions? modelOverride = null,
        TimeSpan? timeout = null)
    {
        var output = AgentConsole.Output;

        if (modelOverride?.IsSkipped == true)
        {
            var reason = "No loaded model meets capability requirements for this scanner";
            await output.ScannerSkippedAsync(scannerName, reason);
            Logger.LogWarning("Skipping scanner {ScannerName}: {Reason}", scannerName, reason);
            await AgentErrorLog.LogAsync(scannerName, $"Skipped: {reason}");
            return;
        }

        var activeModel = modelOverride ?? context.ModelOptions;
        var sw = Stopwatch.StartNew();

        await output.ScannerStartedAsync(scannerName, activeModel.Model);
        Logger.LogInformation("Starting scanner: {ScannerName} with model {Model}", scannerName, activeModel.Model);

        var agent = context.GetAgentClient(modelOverride);

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

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, attempt > 1
                    ? $"{userMessage}\n\nPrevious attempt failed to produce output. Focus on calling WriteOutput with valid markdown content."
                    : userMessage),
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);
            var scannerCt = timeoutCts.Token;

            try
            {
                var responseBuf = new StringBuilder();
                await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, scannerCt))
                {
                    if (update.Text is { Length: > 0 } text)
                    {
                        responseBuf.Append(text);
                    }
                }

                var responseText = responseBuf.ToString();
                if (responseText.Length > 0)
                {
                    var preview = responseText.Length > 200 ? responseText[..200] + "..." : responseText;
                    Logger.LogInformation("Scanner {ScannerName} response: {Preview}", scannerName, preview);

                    // Fallback: if the model produced markdown as text but didn't call WriteOutput.
                    if (expectedOutputPath is not null && !File.Exists(expectedOutputPath))
                    {
                        var trimmed = responseText.TrimStart();
                        if (IsSubstantiveMarkdown(trimmed))
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
                        else
                        {
                            var rejected = trimmed.Length > 100 ? trimmed[..100] + "..." : trimmed;
                            Logger.LogWarning("Scanner {ScannerName} fallback rejected — not markdown: {Preview}",
                                scannerName, rejected);
                            await AgentErrorLog.LogAsync(scannerName, $"Fallback rejected — model produced chatbot filler: {rejected}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("Scanner {ScannerName} produced no text response", scannerName);
                }

                // Retry if output file still missing and we have attempts left
                if (expectedOutputPath is not null && !File.Exists(expectedOutputPath) && attempt < maxAttempts)
                {
                    Logger.LogWarning("Scanner {ScannerName} produced no output — retrying (attempt {Attempt}/{Max})",
                        scannerName, attempt, maxAttempts);
                    continue;
                }

                if (expectedOutputPath is not null && !File.Exists(expectedOutputPath))
                {
                    Logger.LogError("Scanner {ScannerName} produced no output file at {Path}",
                        scannerName, expectedOutputPath);
                    await AgentErrorLog.LogAsync(scannerName, $"No output file produced at {expectedOutputPath}");
                }

                break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Logger.LogWarning("Scanner {ScannerName} timed out after {Timeout}", scannerName, effectiveTimeout);
                await AgentErrorLog.LogAsync(scannerName, $"Timed out after {effectiveTimeout}");
                if (attempt < maxAttempts)
                {
                    Logger.LogInformation("Retrying scanner {ScannerName} (attempt {Attempt}/{Max})",
                        scannerName, attempt + 1, maxAttempts);
                    continue;
                }

                sw.Stop();
                await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: false);
                return;
            }
            catch (Exception ex)
            {
                sw.Stop();
                await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: false);
                Logger.LogError(ex, "Scanner {ScannerName} failed: {Message}", scannerName, ex.Message);
                await AgentErrorLog.LogAsync(scannerName, $"Failed: {ex.Message}", ex);
                return;
            }
        }

        sw.Stop();
        await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: true);
        Logger.LogInformation("Completed scanner: {ScannerName} in {Elapsed:F1}s", scannerName, sw.Elapsed.TotalSeconds);
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
        var modelOverride = parseResult.GetValue(AgentCommandSetup.ModelOverrideOption);
        var modelOptions = AgentModelOptions.Resolve(Configuration, configKey);

        // --model overrides the model name from config, keeping the same endpoint/key
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            modelOptions = modelOptions with { Model = modelOverride };
        }

        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(targetPath, "context", "MAP.md");

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

        FileTools.RootDirectory = targetPath;

        var ctx = new AgentContext(targetPath, outputPath, modelOptions, [], scanOptions);
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
