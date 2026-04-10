using ModelBoss.Benchmarks;

namespace ModelBoss.Tests;

public class AccuracyScorerTests
{
    // ── Reference similarity (exercises BigramSimilarity via public Score) ──

    [Fact]
    public void Score_IdenticalToReference_HighSimilarityScore()
    {
        var reference = """{"name": "AgentTools", "version": "0.1.0"}""";
        var prompt = MakePrompt(referenceOutput: reference, minLength: 10);

        var result = AccuracyScorer.Score("test-model", prompt, reference);

        var check = result.Checks.Single(c => c.Name == "reference_similarity");
        Assert.Equal(1.0, check.Score);
    }

    [Fact]
    public void Score_CompletelyDifferentFromReference_LowSimilarityScore()
    {
        var prompt = MakePrompt(
            referenceOutput: """{"name": "AgentTools", "version": "0.1.0"}""",
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "zzzzzzzzzzzzzzzzzzzzz");

        var check = result.Checks.Single(c => c.Name == "reference_similarity");
        Assert.True(check.Score < 0.3);
    }

    [Fact]
    public void Score_NoReference_SkipsSimilarityCheck()
    {
        var prompt = MakePrompt(referenceOutput: "", minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "Some response");

        Assert.DoesNotContain(result.Checks, c => c.Name == "reference_similarity");
    }

    // ── Score: Required substrings ─────────────────────────────────────

    [Fact]
    public void Score_AllRequiredSubstringsPresent_PassesCheck()
    {
        var prompt = MakePrompt(
            requiredSubstrings: ["AgentTools", "0.1.0", "C#"],
            minLength: 10);

        var result = AccuracyScorer.Score("test-model", prompt,
            """{"name": "AgentTools", "version": "0.1.0", "language": "C#"}""");

        var check = result.Checks.Single(c => c.Name == "required_substrings");
        Assert.Equal(1.0, check.Score);
    }

    [Fact]
    public void Score_MissingRequiredSubstrings_LowersScore()
    {
        var prompt = MakePrompt(
            requiredSubstrings: ["AgentTools", "0.1.0", "C#"],
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt,
            """{"name": "AgentTools"}""");

        var check = result.Checks.Single(c => c.Name == "required_substrings");
        Assert.True(check.Score < 1.0);
        Assert.Contains("Missing", check.Detail);
    }

    // ── Score: Forbidden substrings ────────────────────────────────────

    [Fact]
    public void Score_NoForbiddenSubstrings_FullScore()
    {
        var prompt = MakePrompt(
            forbiddenSubstrings: ["Sure", "Here's"],
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "Clean response.");

        var check = result.Checks.Single(c => c.Name == "forbidden_substrings");
        Assert.Equal(1.0, check.Score);
    }

    [Fact]
    public void Score_ContainsForbiddenSubstring_Penalized()
    {
        var prompt = MakePrompt(
            forbiddenSubstrings: ["Sure", "Here's"],
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "Sure, here is the data.");

        var check = result.Checks.Single(c => c.Name == "forbidden_substrings");
        Assert.True(check.Score < 1.0);
    }

    // ── Score: Length ───────────────────────────────────────────────────

    [Fact]
    public void Score_ResponseWithinLength_FullLengthScore()
    {
        var prompt = MakePrompt(minLength: 10, maxLength: 200);

        var result = AccuracyScorer.Score("test-model", prompt, new string('x', 50));

        var check = result.Checks.Single(c => c.Name == "length");
        Assert.Equal(1.0, check.Score);
    }

    [Fact]
    public void Score_ResponseTooShort_ReducedLengthScore()
    {
        var prompt = MakePrompt(minLength: 100, maxLength: 200);

        var result = AccuracyScorer.Score("test-model", prompt, "Short.");

        var check = result.Checks.Single(c => c.Name == "length");
        Assert.True(check.Score < 1.0);
    }

    [Fact]
    public void Score_ResponseTooLong_ReducedLengthScore()
    {
        var prompt = MakePrompt(minLength: 10, maxLength: 50);

        var result = AccuracyScorer.Score("test-model", prompt, new string('x', 200));

        var check = result.Checks.Single(c => c.Name == "length");
        Assert.True(check.Score < 1.0);
    }

    // ── Score: Required structure ──────────────────────────────────────

    [Fact]
    public void Score_AllStructuralElementsPresent_FullScore()
    {
        var prompt = MakePrompt(
            requiredStructure: ["|", "---"],
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "| Header |\n|---|\n| Data |");

        var check = result.Checks.Single(c => c.Name == "required_structure");
        Assert.Equal(1.0, check.Score);
    }

    [Fact]
    public void Score_MissingStructuralElement_ReducedScore()
    {
        var prompt = MakePrompt(
            requiredStructure: ["|", "---", "# Heading"],
            minLength: 5);

        var result = AccuracyScorer.Score("test-model", prompt, "| Header |\n|---|\n| Data |");

        var check = result.Checks.Single(c => c.Name == "required_structure");
        Assert.True(check.Score < 1.0);
    }

    // ── Score: Reference similarity ────────────────────────────────────

    [Fact]
    public void Score_MatchesReference_HighSimilarity()
    {
        var prompt = MakePrompt(
            referenceOutput: """{"name": "AgentTools", "version": "0.1.0"}""",
            minLength: 10);

        var result = AccuracyScorer.Score("test-model", prompt,
            """{"name": "AgentTools", "version": "0.1.0"}""");

        var check = result.Checks.Single(c => c.Name == "reference_similarity");
        Assert.Equal(1.0, check.Score);
    }

    // ── Score: Composite / Passed ──────────────────────────────────────

    [Fact]
    public void Score_HighQualityResponse_MarkedAsPassed()
    {
        var prompt = MakePrompt(
            requiredSubstrings: ["AgentTools"],
            minLength: 10,
            maxLength: 200,
            passThreshold: 0.6);

        var result = AccuracyScorer.Score("test-model", prompt,
            "The project AgentTools is a .NET 10 application.");

        Assert.True(result.Passed);
        Assert.True(result.Score >= 0.6);
    }

    [Fact]
    public void Score_EmptyResponse_MarkedAsFailed()
    {
        var prompt = MakePrompt(
            requiredSubstrings: ["AgentTools"],
            minLength: 20,
            passThreshold: 0.6);

        var result = AccuracyScorer.Score("test-model", prompt, "");

        Assert.False(result.Passed);
    }

    [Fact]
    public void Score_NullOutput_TreatedAsEmpty()
    {
        var prompt = MakePrompt(minLength: 10);

        var result = AccuracyScorer.Score("test-model", prompt, null!);

        Assert.False(result.Passed);
        Assert.Equal("test-model", result.ModelId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static BenchmarkPrompt MakePrompt(
        IReadOnlyList<string>? requiredSubstrings = null,
        IReadOnlyList<string>? forbiddenSubstrings = null,
        IReadOnlyList<string>? requiredStructure = null,
        string? referenceOutput = null,
        int minLength = 0,
        int maxLength = int.MaxValue,
        double passThreshold = 0.6)
    {
        return new BenchmarkPrompt
        {
            Name = "test_prompt",
            Category = "test",
            SystemMessage = "Test system message.",
            UserMessage = "Test user message.",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = requiredSubstrings ?? [],
                ForbiddenSubstrings = forbiddenSubstrings ?? [],
                RequiredStructure = requiredStructure ?? [],
                ReferenceOutput = referenceOutput ?? "",
                MinLength = minLength,
                MaxLength = maxLength,
                PassThreshold = passThreshold,
            },
        };
    }
}
