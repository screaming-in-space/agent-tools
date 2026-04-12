using System.ComponentModel;
using System.Text;
using Agent.SDK.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelBoss.Benchmarks;

namespace ModelBoss.Tools;

/// <summary>
/// Agent-callable tools that wrap <see cref="BenchmarkRunner"/> and <see cref="AccuracyScorer"/>.
/// These are the tools the ModelBoss LLM agent uses to run benchmarks and score models.
/// </summary>
public sealed class BenchmarkTools(
    BenchmarkRunner runner,
    IConfiguration configuration,
    ILogger<BenchmarkTools> logger)
{
    [Description("Runs speed benchmarks for a specific model config key. Returns timing data: tokens/s, TTFT, total duration.")]
    public async Task<string> RunSpeedBenchmarkAsync(
        string configKey,
        string category,
        int iterations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return "Error: configKey is required (e.g. 'default', 'qwen', 'gemma').";
        }

        var modelOptions = AgentModelOptions.Resolve(configuration, configKey);
        var prompts = BenchmarkSuites.GetByCategory(category);

        if (prompts.Count == 0)
        {
            return $"No prompts found for category '{category}'. Available: instruction_following, extraction, markdown_generation, reasoning, multi_turn, context_window, all";
        }

        var options = new BenchmarkRunOptions
        {
            ModelOptions = modelOptions,
            WarmupIterations = 1,
            MeasuredIterations = Math.Clamp(iterations, 1, 10),
        };

        logger.LogInformation("Running speed benchmark for {ConfigKey} ({Model}), {Count} prompts, {Iterations} iterations",
            configKey, modelOptions.Model, prompts.Count, options.MeasuredIterations);

        var results = await runner.RunSuiteAsync(prompts, options, ct);
        return FormatSpeedResults(configKey, modelOptions.Model, results);
    }

    [Description("Runs accuracy benchmarks for a specific model config key. Scores output against expected criteria.")]
    public async Task<string> RunAccuracyBenchmarkAsync(
        string configKey,
        string category,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return "Error: configKey is required.";
        }

        var modelOptions = AgentModelOptions.Resolve(configuration, configKey);
        var prompts = BenchmarkSuites.GetByCategory(category);

        if (prompts.Count == 0)
        {
            return $"No prompts found for category '{category}'.";
        }

        var options = new BenchmarkRunOptions
        {
            ModelOptions = modelOptions,
            WarmupIterations = 0,
            MeasuredIterations = 1,
        };

        logger.LogInformation("Running accuracy benchmark for {ConfigKey} ({Model}), {Count} prompts",
            configKey, modelOptions.Model, prompts.Count);

        var benchResults = await runner.RunSuiteAsync(prompts, options, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## Accuracy Results: {modelOptions.Model} ({configKey})");
        sb.AppendLine();
        sb.AppendLine("| Prompt | Score | Pass | Details |");
        sb.AppendLine("|--------|-------|------|---------|");

        foreach (var prompt in prompts)
        {
            if (!benchResults.TryGetValue(prompt.Name, out var runs) || runs.Count == 0)
            {
                sb.AppendLine($"| {prompt.Name} | N/A | ✗ | No results |");
                continue;
            }

            var lastRun = runs[^1];
            var accuracy = AccuracyScorer.Score(modelOptions.Model, prompt, lastRun.RawOutput);

            var topIssues = accuracy.Checks
                .Where(c => c.Score < 1.0)
                .OrderBy(c => c.Score)
                .Take(2)
                .Select(c => $"{c.Name}={c.Score:F2}");

            var issues = string.Join("; ", topIssues);
            if (string.IsNullOrEmpty(issues))
            {
                issues = "All checks passed";
            }

            sb.AppendLine($"| {prompt.Name} | {accuracy.Score:F2} | {(accuracy.Passed ? "✓" : "✗")} | {issues} |");
        }

        return sb.ToString();
    }

    [Description("Runs a full benchmark suite (speed + accuracy) for a model config key and produces a scorecard.")]
    public async Task<string> RunFullSuiteAsync(
        string configKey,
        int iterations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return "Error: configKey is required.";
        }

        var modelOptions = AgentModelOptions.Resolve(configuration, configKey);
        var prompts = BenchmarkSuites.All();

        var options = new BenchmarkRunOptions
        {
            ModelOptions = modelOptions,
            WarmupIterations = 1,
            MeasuredIterations = Math.Clamp(iterations, 1, 10),
        };

        logger.LogInformation("Running full suite for {ConfigKey} ({Model}), {Count} prompts, {Iterations} iterations",
            configKey, modelOptions.Model, prompts.Count, options.MeasuredIterations);

        var benchResults = await runner.RunSuiteAsync(prompts, options, ct);

        // Score accuracy on last iteration of each prompt
        var accuracyResults = new Dictionary<string, AccuracyResult>();
        foreach (var prompt in prompts)
        {
            if (benchResults.TryGetValue(prompt.Name, out var runs) && runs.Count > 0)
            {
                accuracyResults[prompt.Name] = AccuracyScorer.Score(modelOptions.Model, prompt, runs[^1].RawOutput);
            }
        }

        var promptCategories = prompts.ToDictionary(p => p.Name, p => p.Category);
        var scorecard = ScorecardBuilder.Build(
            configKey, modelOptions, benchResults, accuracyResults, registryInfo: null, promptCategories);

        return FormatScorecard(scorecard);
    }

    // ── Formatting helpers ─────────────────────────────────────────────

    private static string FormatSpeedResults(
        string configKey,
        string model,
        Dictionary<string, IReadOnlyList<BenchmarkResult>> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Speed Results: {model} ({configKey})");
        sb.AppendLine();
        sb.AppendLine("| Prompt | Tok/s | TTFT (ms) | Total (s) | Tokens | Success |");
        sb.AppendLine("|--------|-------|-----------|-----------|--------|---------|");

        foreach (var (prompt, runs) in results)
        {
            var successful = runs.Where(r => r.Success).ToList();
            if (successful.Count == 0)
            {
                var error = runs.Count > 0 ? runs[0].Error ?? "unknown" : "unknown";
                sb.AppendLine($"| {prompt} | - | - | - | - | ✗ {error} |");
                continue;
            }

            var medianTps = successful.Select(r => r.TokensPerSecond).OrderBy(x => x).ToList();
            var median = medianTps[medianTps.Count / 2];
            var medianTtft = successful.Select(r => r.TimeToFirstToken.TotalMilliseconds).OrderBy(x => x).ToList();
            var ttft = medianTtft[medianTtft.Count / 2];
            var medianDur = successful.Select(r => r.TotalDuration.TotalSeconds).OrderBy(x => x).ToList();
            var dur = medianDur[medianDur.Count / 2];
            var tokens = successful.Select(r => r.OutputTokens).OrderBy(x => x).ToList();
            var tokCount = tokens[tokens.Count / 2];

            sb.AppendLine($"| {prompt} | {median:F1} | {ttft:F0} | {dur:F1} | {tokCount} | ✓ ({successful.Count}/{runs.Count}) |");
        }

        return sb.ToString();
    }

    private static string FormatScorecard(ModelScorecard card)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Scorecard: {card.ModelId}");
        sb.AppendLine();
        sb.AppendLine($"**Config Key:** {card.ConfigKey}");
        sb.AppendLine($"**Composite Score:** {card.CompositeScore:F3}");
        sb.AppendLine();
        sb.AppendLine("## Speed");
        sb.AppendLine($"- Median tokens/s: {card.MedianTokensPerSecond:F1}");
        sb.AppendLine($"- P5 tokens/s: {card.P5TokensPerSecond:F1}");
        sb.AppendLine($"- Median TTFT: {card.MedianTimeToFirstToken.TotalMilliseconds:F0}ms");
        sb.AppendLine($"- Median duration: {card.MedianTotalDuration.TotalSeconds:F1}s");
        sb.AppendLine();
        sb.AppendLine("## Accuracy");
        sb.AppendLine($"- Mean score: {card.MeanAccuracyScore:F3}");
        sb.AppendLine($"- Pass rate: {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0})");
        sb.AppendLine();
        sb.AppendLine("## Per-Prompt Detail");
        sb.AppendLine();
        sb.AppendLine("| Prompt | Tok/s | Accuracy | Pass |");
        sb.AppendLine("|--------|-------|----------|------|");

        foreach (var pr in card.PromptResults)
        {
            sb.AppendLine(
                $"| {pr.PromptName} | {pr.Benchmark.TokensPerSecond:F1} | {pr.Accuracy.Score:F2} | {(pr.Accuracy.Passed ? "✓" : "✗")} |");
        }

        return sb.ToString();
    }
}
