using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Logging;
using Agent.SDK.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelBoss.Benchmarks;
using ModelBoss.Tools;
using OpenAI;

namespace ModelBoss;

/// <summary>
/// Core agent orchestrator for ModelBoss: resolves CLI options, discovers models,
/// runs benchmarks, scores results, and produces ranked scorecards.
/// </summary>
public sealed record BossAgent(ILoggerFactory LoggerFactory, IConfiguration Configuration)
{
    private static readonly AgentTrace Trace = new("ModelBoss");
    private ILogger<BossAgent> Logger { get; } = LoggerFactory.CreateLogger<BossAgent>();

    /// <summary>
    /// Runs the ModelBoss agent with the parsed CLI values.
    /// Returns <c>0</c> on success, <c>1</c> on failure.
    /// </summary>
    public async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        var output = AgentConsole.Output;
        var stopwatch = Stopwatch.StartNew();

        // ── Resolve CLI options ────────────────────────────────────────
        var repoRoot = parseResult.GetValue(BossCommandSetup.RepoRootOption)
            ?? Agent.SDK.Tools.FileTools.FindRepoRoot(Directory.GetCurrentDirectory());

        if (repoRoot is null)
        {
            Logger.LogError("Cannot detect repo root. Use --repo-root or run from within a git repository");
            return 1;
        }

        var outputDir = parseResult.GetValue(BossCommandSetup.OutputOption)
            ?? Path.Combine(repoRoot, "benchmarks");

        var modelsFilter = parseResult.GetValue(BossCommandSetup.ModelsOption) ?? "";
        var iterations = parseResult.GetValue(BossCommandSetup.IterationsOption);
        if (iterations <= 0)
        {
            iterations = 3;
        }

        var category = parseResult.GetValue(BossCommandSetup.CategoryOption) ?? "all";

        await output.StartAsync("ModelBoss", ct);

        await AgentErrorLog.InitAsync(outputDir);

        using var span = Trace.StartSpan("benchmark-run", ActivityKind.Client);
        span?.WithTag("modelboss.output", outputDir);

        try
        {
            // ── Load registries ────────────────────────────────────────
            var registry = ModelRegistry.Load(repoRoot);
            await output.UpdateStatusAsync($"Registry: {registry.Models.Count} models, {registry.Gpus.Count} GPUs");

            // ── Resolve which models to benchmark ──────────────────────
            var allConfigs = AgentModelOptions.ResolveAll(Configuration);
            var targetConfigs = ResolveTargetModels(allConfigs, modelsFilter);

            if (targetConfigs.Count == 0)
            {
                Logger.LogError("No models to benchmark. Check --models filter or appsettings.json");
                return 1;
            }

            await output.UpdateStatusAsync($"Benchmarking {targetConfigs.Count} model(s): {string.Join(", ", targetConfigs.Keys)}");

            // ── Validate endpoint ──────────────────────────────────────
            var firstModel = targetConfigs.Values.First();
            var health = await EndpointHealthCheck.ValidateAsync(firstModel, ct: ct);

            if (!health.IsHealthy)
            {
                Logger.LogError("Endpoint unhealthy: {Error}", health.Error);
                return 1;
            }

            await output.UpdateStatusAsync($"Endpoint healthy — {health.LoadedModels.Count} model(s) loaded");

            // ── Run benchmarks for each model ──────────────────────────
            var runner = new BenchmarkRunner(LoggerFactory.CreateLogger<BenchmarkRunner>(), output);
            var prompts = BenchmarkSuites.GetByCategory(category);
            var promptCategories = prompts.ToDictionary(p => p.Name, p => p.Category);

            // Store intermediate results for judge pass
            var allBenchResults = new Dictionary<string, Dictionary<string, IReadOnlyList<BenchmarkResult>>>();
            var allAccuracyResults = new Dictionary<string, Dictionary<string, AccuracyResult>>();
            var allRawOutputs = new Dictionary<string, Dictionary<string, string>>();
            var initialScorecards = new List<ModelScorecard>();

            foreach (var (configKey, modelOptions) in targetConfigs)
            {
                ct.ThrowIfCancellationRequested();

                await output.ScannerStartedAsync($"Benchmark: {configKey}", modelOptions.Model);
                var benchSw = Stopwatch.StartNew();

                try
                {
                    var runOptions = new BenchmarkRunOptions
                    {
                        ModelOptions = modelOptions,
                        WarmupIterations = 1,
                        MeasuredIterations = iterations,
                    };

                    // Run each prompt individually so we can score + report per-prompt in real-time
                    var benchResults = new Dictionary<string, IReadOnlyList<BenchmarkResult>>(prompts.Count);
                    var accuracyResults = new Dictionary<string, AccuracyResult>();
                    var rawOutputs = new Dictionary<string, string>();

                    foreach (var prompt in prompts)
                    {
                        ct.ThrowIfCancellationRequested();

                        await output.ReportTestStartedAsync(
                            prompt.Name, prompt.Category, prompt.Description, (int)prompt.Difficulty, modelOptions.Model);

                        var runs = await runner.RunAsync(prompt, runOptions, ct);
                        benchResults[prompt.Name] = runs;

                        if (runs.Count > 0)
                        {
                            var lastRun = runs[^1];
                            var accuracy = AccuracyScorer.Score(
                                modelOptions.Model, prompt, lastRun.RawOutput);
                            accuracyResults[prompt.Name] = accuracy;
                            rawOutputs[prompt.Name] = lastRun.RawOutput;

                            var checks = accuracy.Checks
                                .Select(c => new TestCheckResult(c.Name, c.Score, c.Detail))
                                .ToList();

                            await output.ReportPromptResultAsync(
                                prompt.Name, lastRun.TokensPerSecond, accuracy.Score);
                            await output.ReportTestCompletedAsync(
                                prompt.Name, lastRun.TokensPerSecond, lastRun.TimeToFirstToken,
                                accuracy.Score, accuracy.Passed, checks);
                        }
                    }

                    allBenchResults[configKey] = benchResults;
                    allAccuracyResults[configKey] = accuracyResults;
                    allRawOutputs[configKey] = rawOutputs;

                    // Build initial scorecard (without judge) to determine best model
                    var registryInfo = FindRegistryInfo(registry, modelOptions.Model);
                    var scorecard = ScorecardBuilder.Build(
                        configKey, modelOptions, benchResults, accuracyResults, registryInfo, promptCategories);
                    initialScorecards.Add(scorecard);

                    benchSw.Stop();

                    await output.ReportModelSummaryAsync(
                        configKey, modelOptions.Model, scorecard.CompositeScore,
                        scorecard.MeanAccuracyScore, scorecard.MedianTokensPerSecond,
                        scorecard.PassRate, scorecard.PromptsPassedCount, scorecard.TotalPromptsCount);

                    await output.ScannerCompletedAsync($"Benchmark: {configKey}", benchSw.Elapsed, success: true);

                    Logger.LogInformation(
                        "Model {ConfigKey}: composite={Composite:F3}, accuracy={Accuracy:F3}, tok/s={TokS:F1}",
                        configKey, scorecard.CompositeScore, scorecard.MeanAccuracyScore, scorecard.MedianTokensPerSecond);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    benchSw.Stop();
                    await output.ScannerCompletedAsync($"Benchmark: {configKey}", benchSw.Elapsed, success: false);
                    await AgentErrorLog.LogAsync($"Benchmark:{configKey}", $"Benchmark failed: {ex.Message}", ex);
                    await output.ReportErrorAsync($"Benchmark:{configKey}", ex.Message);
                    Logger.LogError(ex, "Benchmark failed for {ConfigKey}: {Message}", configKey, ex.Message);
                }
            }

            // ── LLM-as-judge pass ──────────────────────────────────────
            var scorecards = new List<ModelScorecard>();
            var allJudgeResults = new Dictionary<string, Dictionary<string, JudgeResult>>();
            string? judgeConfigKey = null;

            if (initialScorecards.Count >= 2)
            {
                // Best-scoring model becomes the judge
                var bestScorecard = initialScorecards.OrderByDescending(s => s.CompositeScore).First();
                judgeConfigKey = bestScorecard.ConfigKey;
                var judgeModelOptions = targetConfigs[judgeConfigKey];

                await output.UpdateStatusAsync($"Judge: {judgeConfigKey} ({bestScorecard.CompositeScore:F3}) scoring {initialScorecards.Count - 1} other model(s)");

                var judge = new LlmJudge(LoggerFactory.CreateLogger<LlmJudge>());
                using var judgeClient = LlmJudge.BuildJudgeClient(judgeModelOptions);

                foreach (var (configKey, rawOutputs) in allRawOutputs)
                {
                    ct.ThrowIfCancellationRequested();

                    // Judge doesn't score its own responses
                    if (string.Equals(configKey, judgeConfigKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var modelId = targetConfigs[configKey].Model;
                    await output.ScannerStartedAsync($"Judging: {configKey}", modelId);
                    var judgeSw = Stopwatch.StartNew();

                    try
                    {
                        var judgeResults = await judge.JudgeSuiteAsync(
                            judgeClient, judgeModelOptions.Model, modelId, prompts, rawOutputs, ct);
                        allJudgeResults[configKey] = judgeResults;

                        judgeSw.Stop();
                        await output.ScannerCompletedAsync($"Judging: {configKey}", judgeSw.Elapsed, success: true);

                        var meanScore = judgeResults.Values.Where(j => j.Parsed).Select(j => j.Score).DefaultIfEmpty(0).Average();
                        Logger.LogInformation("Judge scored {ConfigKey}: mean={Mean:F1}/10 ({Count} prompts)",
                            configKey, meanScore, judgeResults.Count);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        judgeSw.Stop();
                        await output.ScannerCompletedAsync($"Judging: {configKey}", judgeSw.Elapsed, success: false);
                        await AgentErrorLog.LogAsync($"Judge:{configKey}", $"Judging failed: {ex.Message}", ex);
                        await output.ReportErrorAsync($"Judge:{configKey}", ex.Message);
                        Logger.LogError(ex, "Judging failed for {ConfigKey}: {Message}", configKey, ex.Message);
                    }
                }
            }

            // ── Rebuild scorecards with judge results ──────────────────
            foreach (var (configKey, modelOptions) in targetConfigs)
            {
                if (!allBenchResults.TryGetValue(configKey, out var benchResults) ||
                    !allAccuracyResults.TryGetValue(configKey, out var accuracyResults))
                {
                    continue;
                }

                var registryInfo = FindRegistryInfo(registry, modelOptions.Model);
                var isJudge = string.Equals(configKey, judgeConfigKey, StringComparison.OrdinalIgnoreCase);
                allJudgeResults.TryGetValue(configKey, out var judgeResults);

                var scorecard = ScorecardBuilder.Build(
                    configKey, modelOptions, benchResults, accuracyResults, registryInfo,
                    judgeResults, isJudge, promptCategories);
                scorecards.Add(scorecard);
            }

            // ── Write report ───────────────────────────────────────────
            var report = ReportFormatter.FormatReport(scorecards, registry, health.LoadedModels);

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var reportPath = Path.Combine(outputDir, "BENCHMARK.md");
            await File.WriteAllTextAsync(reportPath, report, ct);

            stopwatch.Stop();
            span?.SetSuccess();

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: scorecards.Count,
                FilesProcessed: scorecards.Count,
                Duration: stopwatch.Elapsed,
                OutputPath: Path.GetRelativePath(repoRoot, reportPath),
                FullOutputPath: reportPath,
                Success: true), ct);

            Logger.LogInformation("Benchmark report written to {Path}", reportPath);
            await AgentErrorLog.CloseAsync();
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);
            await AgentErrorLog.LogAsync("Agent", $"Run failed: {ex.Message}", ex);

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: 0,
                FilesProcessed: 0,
                Duration: stopwatch.Elapsed,
                OutputPath: outputDir,
                FullOutputPath: outputDir,
                Success: false), ct);

            Logger.LogError(ex, "ModelBoss run failed");
            await AgentErrorLog.CloseAsync();
            return 1;
        }
    }

    private static Dictionary<string, AgentModelOptions> ResolveTargetModels(
        Dictionary<string, AgentModelOptions> allConfigs,
        string modelsFilter)
    {
        if (string.IsNullOrWhiteSpace(modelsFilter))
        {
            return allConfigs;
        }

        var keys = modelsFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new Dictionary<string, AgentModelOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            if (allConfigs.TryGetValue(key, out var options))
            {
                result[key] = options;
            }
        }

        return result;
    }

    private static ModelSummary? FindRegistryInfo(ModelRegistry registry, string modelId)
    {
        var entry = registry.Models
            .FirstOrDefault(m =>
                modelId.Contains(m.Key, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Contains(modelId, StringComparison.OrdinalIgnoreCase));

        if (entry.Value is null)
        {
            return null;
        }

        return new ModelSummary
        {
            ParamsB = entry.Value.ParamsB,
            ActiveParamsB = entry.Value.ActiveParamsB,
            Architecture = entry.Value.Architecture,
            ContextK = entry.Value.ContextK,
            VramQ4Gb = entry.Value.VramQ4Gb,
            ToolCalling = entry.Value.ToolCalling,
            Thinking = entry.Value.Thinking,
        };
    }
}
