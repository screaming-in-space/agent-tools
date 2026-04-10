using ModelBoss.Benchmarks;

namespace ModelBoss.Tests;

public class LlmJudgeTests
{
    // ── ParseScore: bracket pattern [[N]] ──────────────────────────────

    [Fact]
    public void ParseScore_BracketPattern_ExtractsScore()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "The response is good. It follows instructions well.\n\n[[7]]");

        Assert.Equal(7, score);
        Assert.True(parsed);
    }

    [Fact]
    public void ParseScore_BracketPatternMidText_ExtractsScore()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "Overall rating: [[8]] based on the criteria above.");

        Assert.Equal(8, score);
        Assert.True(parsed);
    }

    [Theory]
    [InlineData("[[1]]", 1)]
    [InlineData("[[5]]", 5)]
    [InlineData("[[10]]", 10)]
    public void ParseScore_BracketPattern_AllValidScores(string input, int expected)
    {
        var (score, parsed) = LlmJudge.ParseScore(input);

        Assert.Equal(expected, score);
        Assert.True(parsed);
    }

    [Fact]
    public void ParseScore_BracketScoreAbove10_ClampedTo10()
    {
        var (score, parsed) = LlmJudge.ParseScore("[[15]]");

        Assert.Equal(10, score);
        Assert.True(parsed);
    }

    [Fact]
    public void ParseScore_BracketScoreBelow1_ClampedTo1()
    {
        var (score, parsed) = LlmJudge.ParseScore("[[0]]");

        Assert.Equal(1, score);
        Assert.True(parsed);
    }

    // ── ParseScore: labeled pattern (score: N) ─────────────────────────

    [Theory]
    [InlineData("Score: 8", 8)]
    [InlineData("rating: 6", 6)]
    [InlineData("SCORE=9", 9)]
    public void ParseScore_LabeledPattern_ExtractsScore(string input, int expected)
    {
        var (score, parsed) = LlmJudge.ParseScore(input);

        Assert.Equal(expected, score);
        Assert.True(parsed);
    }

    [Fact]
    public void ParseScore_MultipleLabeledScores_UsesLast()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "Instruction following score: 8\nAccuracy score: 6\nOverall score: 7");

        Assert.Equal(7, score);
        Assert.True(parsed);
    }

    // ── ParseScore: trailing digit fallback ────────────────────────────

    [Fact]
    public void ParseScore_TrailingDigitOnLine_ExtractsScore()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "The model did a decent job overall.\n6");

        Assert.Equal(6, score);
        Assert.True(parsed);
    }

    // ── ParseScore: priority ───────────────────────────────────────────

    [Fact]
    public void ParseScore_BracketTakesPriorityOverLabeled()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "Score: 5\n\n[[8]]");

        Assert.Equal(8, score);
        Assert.True(parsed);
    }

    // ── ParseScore: edge cases ─────────────────────────────────────────

    [Fact]
    public void ParseScore_EmptyString_ReturnsDefaultAndNotParsed()
    {
        var (score, parsed) = LlmJudge.ParseScore("");

        Assert.Equal(1, score);
        Assert.False(parsed);
    }

    [Fact]
    public void ParseScore_NullString_ReturnsDefaultAndNotParsed()
    {
        var (score, parsed) = LlmJudge.ParseScore(null!);

        Assert.Equal(1, score);
        Assert.False(parsed);
    }

    [Fact]
    public void ParseScore_NoScorePattern_ReturnsDefaultAndNotParsed()
    {
        var (score, parsed) = LlmJudge.ParseScore(
            "This is a response with no numeric score pattern at all.");

        Assert.Equal(1, score);
        Assert.False(parsed);
    }

    // ── JudgeResult: NormalizedScore ───────────────────────────────────

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(5, 4.0 / 9.0)]
    [InlineData(10, 1.0)]
    public void JudgeResult_NormalizedScore_MapsCorrectly(int score, double expected)
    {
        var result = new JudgeResult
        {
            ModelId = "test",
            JudgeModelId = "judge",
            PromptName = "test_prompt",
            Score = score,
            Reasoning = "test",
            Parsed = true,
        };

        Assert.Equal(expected, result.NormalizedScore, precision: 6);
    }
}
