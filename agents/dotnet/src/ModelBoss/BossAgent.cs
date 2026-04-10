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
            var runner = new BenchmarkRunner(LoggerFactory.CreateLogger<BenchmarkRunner>());
            var prompts = GetPromptsByCategory(category);
            var scorecards = new List<ModelScorecard>();

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

                    var benchResults = await runner.RunSuiteAsync(prompts, runOptions, ct);

                    // Score accuracy
                    var accuracyResults = new Dictionary<string, AccuracyResult>();
                    foreach (var prompt in prompts)
                    {
                        if (benchResults.TryGetValue(prompt.Name, out var runs) && runs.Count > 0)
                        {
                            accuracyResults[prompt.Name] = AccuracyScorer.Score(
                                modelOptions.Model, prompt, runs[^1].RawOutput);
                        }
                    }

                    // Build registry info if available
                    var registryInfo = FindRegistryInfo(registry, modelOptions.Model);

                    var scorecard = ScorecardBuilder.Build(
                        configKey, modelOptions, benchResults, accuracyResults, registryInfo);

                    scorecards.Add(scorecard);

                    benchSw.Stop();
                    await output.ScannerCompletedAsync($"Benchmark: {configKey}", benchSw.Elapsed, success: true);

                    Logger.LogInformation(
                        "Model {ConfigKey}: composite={Composite:F3}, accuracy={Accuracy:F3}, tok/s={TokS:F1}",
                        configKey, scorecard.CompositeScore, scorecard.MeanAccuracyScore, scorecard.MedianTokensPerSecond);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    benchSw.Stop();
                    await output.ScannerCompletedAsync($"Benchmark: {configKey}", benchSw.Elapsed, success: false);
                    Logger.LogError(ex, "Benchmark failed for {ConfigKey}: {Message}", configKey, ex.Message);
                }
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
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: 0,
                FilesProcessed: 0,
                Duration: stopwatch.Elapsed,
                OutputPath: outputDir,
                FullOutputPath: outputDir,
                Success: false), ct);

            Logger.LogError(ex, "ModelBoss run failed");
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

    private static IReadOnlyList<BenchmarkPrompt> GetPromptsByCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "instruction_following" => BenchmarkSuites.InstructionFollowing(),
            "extraction" => BenchmarkSuites.Extraction(),
            "markdown_generation" => BenchmarkSuites.MarkdownGeneration(),
            "reasoning" => BenchmarkSuites.Reasoning(),
            _ => BenchmarkSuites.All(),
        };
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
