namespace ModelBoss.Benchmarks;

/// <summary>
/// Difficulty level for progressive benchmark testing.
/// Level 1 prompts verify basic capability; Level 2 tests stricter compliance;
/// Level 3 stresses edge cases and full-context performance.
/// </summary>
public enum BenchmarkDifficulty
{
    /// <summary>Basic capability — can the model follow directions at all?</summary>
    Level1 = 1,

    /// <summary>Stricter compliance — fewer guardrails, harder constraints, ambiguous input.</summary>
    Level2 = 2,

    /// <summary>Stress testing — long context, multi-step chains, adversarial constraints.</summary>
    Level3 = 3,
}

/// <summary>
/// Defines a benchmark prompt with expected output criteria for accuracy scoring.
/// Each prompt tests a specific model capability (instruction following, extraction, tool use, etc.).
/// Single-turn prompts use <see cref="UserMessage"/> + <see cref="Expected"/>.
/// Multi-turn prompts (MT-Bench style) use <see cref="Turns"/> instead.
/// </summary>
public sealed record BenchmarkPrompt
{
    /// <summary>Unique name for this benchmark (e.g. "summarize_markdown", "extract_json").</summary>
    public required string Name { get; init; }

    /// <summary>Category for grouping (e.g. "instruction_following", "extraction", "tool_calling").</summary>
    public required string Category { get; init; }

    /// <summary>Human-readable description of what this benchmark tests.</summary>
    public string Description { get; init; } = "";

    /// <summary>Difficulty level for progressive testing. Models failing ≥90% at a level skip harder ones.</summary>
    public BenchmarkDifficulty Difficulty { get; init; } = BenchmarkDifficulty.Level1;

    /// <summary>System prompt to send to the model.</summary>
    public required string SystemMessage { get; init; }

    /// <summary>User prompt to send to the model (single-turn mode). Ignored when <see cref="Turns"/> is populated.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Expected characteristics of a correct response (single-turn mode). Ignored when <see cref="Turns"/> is populated.</summary>
    public required ExpectedOutput Expected { get; init; }

    /// <summary>
    /// Multi-turn conversation sequence (MT-Bench style). When non-empty, the runner sends
    /// each turn sequentially maintaining conversation history, and scores each turn independently.
    /// <see cref="UserMessage"/> and <see cref="Expected"/> are ignored in multi-turn mode.
    /// </summary>
    public IReadOnlyList<ConversationTurn> Turns { get; init; } = [];

    /// <summary>Whether this prompt uses multi-turn conversation.</summary>
    public bool IsMultiTurn => Turns.Count > 0;

    /// <summary>Maximum time allowed for this prompt before marking it as failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// A single turn in a multi-turn conversation benchmark.
/// Each turn sends a user message and evaluates the model's response independently.
/// </summary>
public sealed record ConversationTurn
{
    /// <summary>User message for this turn. May reference prior context implicitly.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Expected output criteria for this turn's response.</summary>
    public required ExpectedOutput Expected { get; init; }
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
    /// Substrings that must NOT appear in the first 100 characters of the response.
    /// Catches preamble filler like "Sure!", "Of course!", "Here's" even when
    /// ForbiddenSubstrings allows them deeper in the output.
    /// </summary>
    public IReadOnlyList<string> ForbiddenPreamble { get; init; } = [];

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
