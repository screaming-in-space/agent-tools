namespace Agent.SDK.Console;

/// <summary>
/// Base type for all messages flowing through the Channel-based UI pipeline.
/// Each derived type represents a distinct event that the renderer handles.
/// </summary>
public abstract record UIMessage(DateTimeOffset Timestamp);

/// <summary>Thinking/reasoning token from LLM inference (high volume).</summary>
public sealed record ThinkingTokenMessage(
    string Token, string ScannerName, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>Visible response token from LLM inference (high volume).</summary>
public sealed record ResponseTokenMessage(
    string Token, string ScannerName, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>A benchmark test is starting against a model.</summary>
public sealed record TestStartedMessage(
    string PromptName, string Category, string Description,
    int DifficultyLevel, string ModelId, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>A benchmark test has completed with results.</summary>
public sealed record TestCompletedMessage(
    string PromptName, double TokensPerSecond, TimeSpan Ttft,
    double AccuracyScore, bool Passed,
    IReadOnlyList<TestCheckResult> Checks, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>A new model benchmark phase is starting.</summary>
public sealed record ModelPhaseStartedMessage(
    string ConfigKey, string ModelId, string? ModelSummary,
    double? ParamsB, string? Architecture, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>A model benchmark phase has completed.</summary>
public sealed record ModelPhaseCompletedMessage(
    string ConfigKey, TimeSpan Elapsed, bool Success, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>An error occurred during benchmarking.</summary>
public sealed record ErrorMessage(
    string Source, string Message, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>Status bar text update.</summary>
public sealed record StatusMessage(
    string Text, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>LLM-as-judge scored a model's response.</summary>
public sealed record JudgeResultMessage(
    string ModelId, string PromptName, int Score,
    string Reasoning, DateTimeOffset Timestamp) : UIMessage(Timestamp);

/// <summary>The LLM-as-judge phase is starting.</summary>
public sealed record JudgePhaseStartedMessage(
    string JudgeModelId, int ModelsToJudge, DateTimeOffset Timestamp) : UIMessage(Timestamp);
