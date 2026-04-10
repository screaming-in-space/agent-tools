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

    /// <summary>Time from request sent to first token received (time-to-first-token).</summary>
    public required TimeSpan TimeToFirstToken { get; init; }

    /// <summary>Total tokens generated in the response.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Total tokens consumed from the prompt.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Tokens per second for the generation phase (output tokens / generation time).</summary>
    public double TokensPerSecond => TotalDuration.TotalSeconds > 0
        ? OutputTokens / TotalDuration.TotalSeconds
        : 0;

    /// <summary>The raw text response from the model.</summary>
    public required string RawOutput { get; init; }

    /// <summary>Whether the request completed without error.</summary>
    public required bool Success { get; init; }

    /// <summary>Error message if <see cref="Success"/> is <c>false</c>.</summary>
    public string? Error { get; init; }
}
