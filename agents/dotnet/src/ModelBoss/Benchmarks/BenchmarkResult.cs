namespace ModelBoss.Benchmarks;

/// <summary>
/// Raw timing result from a single inference request against one model.
/// Immutable value captured per-iteration — aggregation happens in <see cref="ModelScorecard"/>.
/// </summary>
public sealed record BenchmarkResult
{
    /// <summary>Model identifier as reported by the endpoint.</summary>
    public required string ModelId { get; init; }

    /// <summary>Name of the benchmark prompt that produced this result.</summary>
    public required string PromptName { get; init; }

    /// <summary>Wall-clock duration from request sent to full response received.</summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>Time from request sent to first visible token received.</summary>
    public required TimeSpan TimeToFirstToken { get; init; }

    /// <summary>Time from request sent to first thinking token. Equal to <see cref="TotalDuration"/> when no thinking occurred.</summary>
    public TimeSpan TimeToFirstThinking { get; init; }

    /// <summary>Total visible output tokens generated in the response.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Total thinking tokens generated before/during the response.</summary>
    public int ThinkingTokens { get; init; }

    /// <summary>Total tokens consumed from the prompt.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Visible output tokens per second (output tokens / total duration).</summary>
    public double TokensPerSecond => TotalDuration.TotalSeconds > 0
        ? OutputTokens / TotalDuration.TotalSeconds
        : 0;

    /// <summary>
    /// Generation tokens per second — visible output tokens divided by time spent generating
    /// (total duration minus thinking time). Reflects actual decode speed excluding thinking overhead.
    /// </summary>
    public double GenerationTokensPerSecond
    {
        get
        {
            var genTime = TotalDuration - ThinkingDuration;
            return genTime.TotalSeconds > 0 ? OutputTokens / genTime.TotalSeconds : 0;
        }
    }

    /// <summary>Wall-clock time spent in the thinking phase. Zero when model does not think.</summary>
    public TimeSpan ThinkingDuration { get; init; }

    /// <summary>The raw text response from the model (visible output only, excludes thinking).</summary>
    public required string RawOutput { get; init; }

    /// <summary>Whether the request completed without error.</summary>
    public required bool Success { get; init; }

    /// <summary>Error message if <see cref="Success"/> is <c>false</c>.</summary>
    public string? Error { get; init; }
}
