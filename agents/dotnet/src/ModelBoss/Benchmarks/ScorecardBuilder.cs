namespace ModelBoss.Benchmarks;

/// <summary>
/// Aggregates raw <see cref="BenchmarkResult"/> and <see cref="AccuracyResult"/> data
/// into a final <see cref="ModelScorecard"/>. Pure computation, no I/O.
/// </summary>
public static class ScorecardBuilder
{
    /// <summary>
    /// Builds a scorecard for one model from its benchmark and accuracy results.
    /// No LLM-as-judge scores — uses deterministic-only composite formula.
    /// </summary>
    public static ModelScorecard Build(
        string configKey,
        Agent.SDK.Configuration.AgentModelOptions modelOptions,
        Dictionary<string, IReadOnlyList<BenchmarkResult>> benchmarkResults,
        Dictionary<string, AccuracyResult> accuracyResults,
        ModelSummary? registryInfo,
        Dictionary<string, string>? promptCategories = null)
    {
        return Build(configKey, modelOptions, benchmarkResults, accuracyResults, registryInfo,
            judgeResults: null, isJudgeModel: false, promptCategories: promptCategories);
    }

    /// <summary>
    /// Builds a scorecard incorporating LLM-as-judge scores into the composite.
    /// Pass <paramref name="judgeResults"/> as <c>null</c> for the judge model itself
    /// and set <paramref name="isJudgeModel"/> to <c>true</c>.
    /// </summary>
    public static ModelScorecard Build(
        string configKey,
        Agent.SDK.Configuration.AgentModelOptions modelOptions,
        Dictionary<string, IReadOnlyList<BenchmarkResult>> benchmarkResults,
        Dictionary<string, AccuracyResult> accuracyResults,
        ModelSummary? registryInfo,
        Dictionary<string, JudgeResult>? judgeResults,
        bool isJudgeModel,
        Dictionary<string, string>? promptCategories = null)
    {
        ArgumentNullException.ThrowIfNull(benchmarkResults);
        ArgumentNullException.ThrowIfNull(accuracyResults);

        var allBenchmarks = benchmarkResults.Values.SelectMany(r => r).Where(r => r.Success).ToList();
        var allAccuracy = accuracyResults.Values.ToList();

        // ── Speed metrics ──────────────────────────────────────────────
        var tokensPerSec = allBenchmarks.Select(b => b.TokensPerSecond).OrderBy(x => x).ToList();
        var genToksPerSec = allBenchmarks.Select(b => b.GenerationTokensPerSecond).OrderBy(x => x).ToList();
        var ttfts = allBenchmarks.Select(b => b.TimeToFirstToken).OrderBy(x => x).ToList();
        var durations = allBenchmarks.Select(b => b.TotalDuration).OrderBy(x => x).ToList();

        var medianTps = Percentile(tokensPerSec, 0.5);
        var p5Tps = Percentile(tokensPerSec, 0.05);
        var medianGenTps = Percentile(genToksPerSec, 0.5);
        var medianTtft = ttfts.Count > 0 ? PercentileTimeSpan(ttfts, 0.5) : TimeSpan.Zero;
        var medianDuration = durations.Count > 0 ? PercentileTimeSpan(durations, 0.5) : TimeSpan.Zero;

        // ── Thinking metrics ───────────────────────────────────────────
        var totalThinkingTokens = allBenchmarks.Sum(b => b.ThinkingTokens);
        var thinkingDurations = allBenchmarks
            .Where(b => b.ThinkingTokens > 0)
            .Select(b => b.ThinkingDuration)
            .OrderBy(x => x)
            .ToList();
        var medianThinkingDuration = thinkingDurations.Count > 0
            ? PercentileTimeSpan(thinkingDurations, 0.5)
            : TimeSpan.Zero;

        // ── Accuracy metrics ───────────────────────────────────────────
        var meanAccuracy = allAccuracy.Count > 0
            ? allAccuracy.Average(a => a.Score)
            : 0;
        var passedCount = allAccuracy.Count(a => a.Passed);

        // ── Composite score ────────────────────────────────────────────
        // Normalize speed: 50 tok/s = 1.0, linear scale
        var normalizedSpeed = Math.Min(1.0, medianTps / 50.0);
        var passRate = allAccuracy.Count > 0 ? (double)passedCount / allAccuracy.Count : 0;

        // ── Judge metrics ──────────────────────────────────────────────
        var parsedJudge = judgeResults?.Values.Where(j => j.Parsed).ToList();
        var meanJudge = parsedJudge is { Count: > 0 }
            ? parsedJudge.Average(j => (double)j.Score)
            : (double?)null;
        var meanJudgeNorm = parsedJudge is { Count: > 0 }
            ? parsedJudge.Average(j => j.NormalizedScore)
            : (double?)null;
        var judgedCount = parsedJudge?.Count ?? 0;

        // With judge: (accuracy × 0.35) + (judge × 0.30) + (speed × 0.25) + (pass_rate × 0.10)
        // Without:    (accuracy × 0.60) + (speed × 0.30) + (pass_rate × 0.10)
        var compositeScore = meanJudgeNorm.HasValue
            ? (meanAccuracy * 0.35) + (meanJudgeNorm.Value * 0.30) + (normalizedSpeed * 0.25) + (passRate * 0.10)
            : (meanAccuracy * 0.60) + (normalizedSpeed * 0.30) + (passRate * 0.10);

        // ── Per-prompt detail ──────────────────────────────────────────
        var promptResults = new List<PromptResult>();
        foreach (var (promptName, benchmarks) in benchmarkResults)
        {
            // Use the last measured iteration as representative
            var benchmark = benchmarks.Count > 0 ? benchmarks[^1] : null;
            if (benchmark is null)
            {
                continue;
            }

            if (accuracyResults.TryGetValue(promptName, out var accuracy))
            {
                JudgeResult? judge = null;
                judgeResults?.TryGetValue(promptName, out judge);

                var category = promptCategories is not null && promptCategories.TryGetValue(promptName, out var cat)
                    ? cat : "unknown";

                promptResults.Add(new PromptResult
                {
                    PromptName = promptName,
                    Category = category,
                    Benchmark = benchmark,
                    Accuracy = accuracy,
                    Judge = judge,
                });
            }
        }

        return new ModelScorecard
        {
            ModelId = modelOptions.Model,
            ConfigKey = configKey,
            RegistryInfo = registryInfo,
            MedianTokensPerSecond = medianTps,
            P5TokensPerSecond = p5Tps,
            MedianGenerationTokensPerSecond = medianGenTps,
            MedianTimeToFirstToken = medianTtft,
            MedianTotalDuration = medianDuration,
            TotalThinkingTokens = totalThinkingTokens,
            MedianThinkingDuration = medianThinkingDuration,
            MeanAccuracyScore = meanAccuracy,
            PromptsPassedCount = passedCount,
            TotalPromptsCount = allAccuracy.Count,
            MeanJudgeScore = meanJudge,
            MeanJudgeNormalized = meanJudgeNorm,
            JudgedPromptCount = judgedCount,
            IsJudgeModel = isJudgeModel,
            CompositeScore = compositeScore,
            PromptResults = promptResults,
        };
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = index - lower;
        return sorted[lower] + (fraction * (sorted[upper] - sorted[lower]));
    }

    private static TimeSpan PercentileTimeSpan(List<TimeSpan> sorted, double p)
    {
        var ticks = sorted.Select(t => (double)t.Ticks).ToList();
        return TimeSpan.FromTicks((long)Percentile(ticks, p));
    }
}
