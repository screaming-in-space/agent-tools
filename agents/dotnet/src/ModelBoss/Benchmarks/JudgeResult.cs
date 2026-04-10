namespace ModelBoss.Benchmarks;

/// <summary>
/// Result of an LLM-as-judge evaluation for a single model response.
/// The judge model scores on a 1-10 scale across multiple dimensions,
/// similar to MT-Bench's evaluation protocol.
/// </summary>
public sealed record JudgeResult
{
    /// <summary>Model that was judged.</summary>
    public required string ModelId { get; init; }

    /// <summary>Model that served as judge.</summary>
    public required string JudgeModelId { get; init; }

    /// <summary>Name of the prompt that was evaluated.</summary>
    public required string PromptName { get; init; }

    /// <summary>Overall judge score from 1 (worst) to 10 (best).</summary>
    public required int Score { get; init; }

    /// <summary>Normalized score (0.0 to 1.0) for composite integration.</summary>
    public double NormalizedScore => (Score - 1) / 9.0;

    /// <summary>Raw reasoning/explanation from the judge model.</summary>
    public required string Reasoning { get; init; }

    /// <summary>Whether the judge response was successfully parsed.</summary>
    public required bool Parsed { get; init; }
}
