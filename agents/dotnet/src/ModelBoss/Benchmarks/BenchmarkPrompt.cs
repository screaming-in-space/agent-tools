namespace ModelBoss.Benchmarks;

/// <summary>
/// Defines a benchmark prompt with expected output criteria for accuracy scoring.
/// Each prompt tests a specific model capability (instruction following, extraction, tool use, etc.).
/// </summary>
public sealed record BenchmarkPrompt
{
    /// <summary>Unique name for this benchmark (e.g. "summarize_markdown", "extract_json").</summary>
    public required string Name { get; init; }

    /// <summary>Category for grouping (e.g. "instruction_following", "extraction", "tool_calling").</summary>
    public required string Category { get; init; }

    /// <summary>System prompt to send to the model.</summary>
    public required string SystemMessage { get; init; }

    /// <summary>User prompt to send to the model.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Expected characteristics of a correct response.</summary>
    public required ExpectedOutput Expected { get; init; }

    /// <summary>Maximum time allowed for this prompt before marking it as failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Defines what a correct response looks like for accuracy scoring.
/// Multiple dimensions are checked independently and weighted.
/// </summary>
public sealed record ExpectedOutput
{
    /// <summary>Substrings that must appear in the response (case-insensitive).</summary>
    public IReadOnlyList<string> RequiredSubstrings { get; init; } = [];

    /// <summary>Substrings that must NOT appear (chatbot filler, hallucinations).</summary>
    public IReadOnlyList<string> ForbiddenSubstrings { get; init; } = [];

    /// <summary>Markdown structural elements required (e.g. "#", "|", "- ").</summary>
    public IReadOnlyList<string> RequiredStructure { get; init; } = [];

    /// <summary>
    /// Reference output for similarity scoring. Empty means skip similarity check.
    /// This is the "gold standard" output a strong model would produce.
    /// </summary>
    public string ReferenceOutput { get; init; } = "";

    /// <summary>Minimum response length in characters. Responses shorter than this fail.</summary>
    public int MinLength { get; init; }

    /// <summary>Maximum response length in characters. Responses longer than this are penalized.</summary>
    public int MaxLength { get; init; } = int.MaxValue;

    /// <summary>Minimum accuracy score (0.0-1.0) to count as a pass.</summary>
    public double PassThreshold { get; init; } = 0.6;
}
