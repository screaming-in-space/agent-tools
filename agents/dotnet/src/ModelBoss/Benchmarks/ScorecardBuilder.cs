namespace ModelBoss.Benchmarks;

/// <summary>
/// Aggregates raw <see cref="BenchmarkResult"/> and <see cref="AccuracyResult"/> data
/// into a final <see cref="ModelScorecard"/>. Pure computation, no I/O.
/// </summary>
public static class ScorecardBuilder
{
    /// <summary>
    /// Builds a scorecard for one model from its benchmark and accuracy results.
    /// </summary>
    public static ModelScorecard Build(
        string configKey,
        Agent.SDK.Configuration.AgentModelOptions modelOptions,
        Dictionary<string, IReadOnlyList<BenchmarkResult>> benchmarkResults,
        Dictionary<string, AccuracyResult> accuracyResults,
        ModelSummary? registryInfo)
    {
        ArgumentNullException.ThrowIfNull(benchmarkResults);
        ArgumentNullException.ThrowIfNull(accuracyResults);

        var allBenchmarks = benchmarkResults.Values.SelectMany(r => r).Where(r => r.Success).ToList();
        var allAccuracy = accuracyResults.Values.ToList();

        // ── Speed metrics ──────────────────────────────────────────────
        var tokensPerSec = allBenchmarks.Select(b => b.TokensPerSecond).OrderBy(x => x).ToList();
        var ttfts = allBenchmarks.Select(b => b.TimeToFirstToken).OrderBy(x => x).ToList();
        var durations = allBenchmarks.Select(b => b.TotalDuration).OrderBy(x => x).ToList();

        var medianTps = Percentile(tokensPerSec, 0.5);
        var p5Tps = Percentile(tokensPerSec, 0.05);
        var medianTtft = ttfts.Count > 0 ? PercentileTimeSpan(ttfts, 0.5) : TimeSpan.Zero;
        var medianDuration = durations.Count > 0 ? PercentileTimeSpan(durations, 0.5) : TimeSpan.Zero;

        // ── Accuracy metrics ───────────────────────────────────────────
        var meanAccuracy = allAccuracy.Count > 0
            ? allAccuracy.Average(a => a.Score)
            : 0;
        var passedCount = allAccuracy.Count(a => a.Passed);

        // ── Composite score ────────────────────────────────────────────
        // Normalize speed: 50 tok/s = 1.0, linear scale
        var normalizedSpeed = Math.Min(1.0, medianTps / 50.0);
        var passRate = allAccuracy.Count > 0 ? (double)passedCount / allAccuracy.Count : 0;
        var compositeScore = (meanAccuracy * 0.6) + (normalizedSpeed * 0.3) + (passRate * 0.1);

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
                promptResults.Add(new PromptResult
                {
                    PromptName = promptName,
                    Benchmark = benchmark,
                    Accuracy = accuracy,
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
            MedianTimeToFirstToken = medianTtft,
            MedianTotalDuration = medianDuration,
            MeanAccuracyScore = meanAccuracy,
            PromptsPassedCount = passedCount,
            TotalPromptsCount = allAccuracy.Count,
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
