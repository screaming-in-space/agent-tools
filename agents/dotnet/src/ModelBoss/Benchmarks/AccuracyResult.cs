namespace ModelBoss.Benchmarks;

/// <summary>
/// Accuracy assessment of a single model response against expected output.
/// Produced by <see cref="AccuracyScorer"/> after comparing raw output to expectations.
/// </summary>
public sealed record AccuracyResult
{
    /// <summary>Model identifier.</summary>
    public required string ModelId { get; init; }

    /// <summary>Name of the benchmark prompt that was evaluated.</summary>
    public required string PromptName { get; init; }

    /// <summary>Overall accuracy score from 0.0 (complete miss) to 1.0 (perfect).</summary>
    public required double Score { get; init; }

    /// <summary>Whether the response met the minimum quality threshold.</summary>
    public required bool Passed { get; init; }

    /// <summary>Individual check results that contributed to the score.</summary>
    public required IReadOnlyList<AccuracyCheck> Checks { get; init; }
}

/// <summary>
/// A single accuracy check within an <see cref="AccuracyResult"/>.
/// Each check evaluates one dimension: structure, content, tool usage, etc.
/// </summary>
public sealed record AccuracyCheck
{
    /// <summary>What this check verifies (e.g. "has_headings", "tool_call_order", "content_similarity").</summary>
    public required string Name { get; init; }

    /// <summary>Score for this check: 0.0 to 1.0.</summary>
    public required double Score { get; init; }

    /// <summary>Weight of this check in the overall score.</summary>
    public required double Weight { get; init; }

    /// <summary>Human-readable explanation of the result.</summary>
    public required string Detail { get; init; }
}
