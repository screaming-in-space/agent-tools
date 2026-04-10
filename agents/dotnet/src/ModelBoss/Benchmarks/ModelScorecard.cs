namespace ModelBoss.Benchmarks;

/// <summary>
/// Aggregated scorecard for one model across all benchmark prompts.
/// This is the final output: the thing you look at to decide which model to use.
/// </summary>
public sealed record ModelScorecard
{
    /// <summary>Model identifier as reported by the endpoint.</summary>
    public required string ModelId { get; init; }

    /// <summary>Model config key from appsettings.json.</summary>
    public required string ConfigKey { get; init; }

    /// <summary>Registry metadata (params, architecture, VRAM). Null when model not in registry.</summary>
    public required ModelSummary? RegistryInfo { get; init; }

    // ── Speed metrics (aggregated across all prompts) ──────────────────

    /// <summary>Median tokens per second across all benchmark runs.</summary>
    public required double MedianTokensPerSecond { get; init; }

    /// <summary>P95 tokens per second (5th percentile — worst realistic case).</summary>
    public required double P5TokensPerSecond { get; init; }

    /// <summary>Median time-to-first-token across all runs.</summary>
    public required TimeSpan MedianTimeToFirstToken { get; init; }

    /// <summary>Median total request duration across all runs.</summary>
    public required TimeSpan MedianTotalDuration { get; init; }

    // ── Accuracy metrics ───────────────────────────────────────────────

    /// <summary>Mean accuracy score across all prompts (0.0 to 1.0).</summary>
    public required double MeanAccuracyScore { get; init; }

    /// <summary>Number of prompts that passed the minimum accuracy threshold.</summary>
    public required int PromptsPassedCount { get; init; }

    /// <summary>Total prompts evaluated.</summary>
    public required int TotalPromptsCount { get; init; }

    /// <summary>Pass rate as a fraction (0.0 to 1.0).</summary>
    public double PassRate => TotalPromptsCount > 0
        ? (double)PromptsPassedCount / TotalPromptsCount
        : 0;

    // ── Composite ──────────────────────────────────────────────────────

    /// <summary>
    /// Composite score combining speed and accuracy. Higher is better.
    /// Formula: <c>(accuracy * 0.6) + (normalized_speed * 0.3) + (pass_rate * 0.1)</c>
    /// </summary>
    public required double CompositeScore { get; init; }

    /// <summary>Per-prompt detail for drill-down.</summary>
    public required IReadOnlyList<PromptResult> PromptResults { get; init; }
}

/// <summary>
/// Combined speed + accuracy result for a single prompt against a single model.
/// </summary>
public sealed record PromptResult
{
    public required string PromptName { get; init; }
    public required BenchmarkResult Benchmark { get; init; }
    public required AccuracyResult Accuracy { get; init; }
}

/// <summary>
/// Lightweight model info extracted from the registry for scorecard display.
/// </summary>
public sealed record ModelSummary
{
    public required double ParamsB { get; init; }
    public required double ActiveParamsB { get; init; }
    public required string Architecture { get; init; }
    public required int ContextK { get; init; }
    public required int VramQ4Gb { get; init; }
    public required string ToolCalling { get; init; }
    public required bool Thinking { get; init; }
}
