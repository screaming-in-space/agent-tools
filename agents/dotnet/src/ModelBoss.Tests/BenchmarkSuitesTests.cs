using ModelBoss.Benchmarks;

namespace ModelBoss.Tests;

public class BenchmarkSuitesTests
{
    [Fact]
    public void All_ReturnsAllCategories()
    {
        var all = BenchmarkSuites.All();

        Assert.True(all.Count > 0);
        Assert.Contains(all, p => p.Category == "instruction_following");
        Assert.Contains(all, p => p.Category == "extraction");
        Assert.Contains(all, p => p.Category == "markdown_generation");
        Assert.Contains(all, p => p.Category == "reasoning");
    }

    [Fact]
    public void All_CountMatchesSumOfCategories()
    {
        var all = BenchmarkSuites.All();
        var sumOfParts = BenchmarkSuites.InstructionFollowing().Count
            + BenchmarkSuites.Extraction().Count
            + BenchmarkSuites.MarkdownGeneration().Count
            + BenchmarkSuites.Reasoning().Count;

        Assert.Equal(sumOfParts, all.Count);
    }

    [Theory]
    [InlineData("instruction_following")]
    [InlineData("extraction")]
    [InlineData("markdown_generation")]
    [InlineData("reasoning")]
    public void Category_AllPromptsHaveMatchingCategory(string category)
    {
        var prompts = category switch
        {
            "instruction_following" => BenchmarkSuites.InstructionFollowing(),
            "extraction" => BenchmarkSuites.Extraction(),
            "markdown_generation" => BenchmarkSuites.MarkdownGeneration(),
            "reasoning" => BenchmarkSuites.Reasoning(),
            _ => throw new ArgumentException($"Unknown category: {category}"),
        };

        Assert.All(prompts, p => Assert.Equal(category, p.Category));
    }

    [Fact]
    public void AllPrompts_HaveUniqueNames()
    {
        var all = BenchmarkSuites.All();
        var names = all.Select(p => p.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void AllPrompts_HaveNonEmptyMessages()
    {
        var all = BenchmarkSuites.All();

        Assert.All(all, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.SystemMessage));
            Assert.False(string.IsNullOrWhiteSpace(p.UserMessage));
        });
    }

    [Fact]
    public void AllPrompts_HaveExpectedOutput()
    {
        var all = BenchmarkSuites.All();

        Assert.All(all, p =>
        {
            Assert.NotNull(p.Expected);
            Assert.True(p.Expected.PassThreshold > 0);
        });
    }

    [Fact]
    public void AllPrompts_HaveReasonableTimeout()
    {
        var all = BenchmarkSuites.All();

        Assert.All(all, p =>
        {
            Assert.True(p.Timeout > TimeSpan.Zero);
            Assert.True(p.Timeout <= TimeSpan.FromMinutes(5));
        });
    }

    [Fact]
    public void InstructionFollowing_HasMinimumPromptCount()
    {
        var prompts = BenchmarkSuites.InstructionFollowing();

        Assert.True(prompts.Count >= 3, "Instruction following should have at least 3 prompts");
    }

    [Fact]
    public void Extraction_HasMinimumPromptCount()
    {
        var prompts = BenchmarkSuites.Extraction();

        Assert.True(prompts.Count >= 2, "Extraction should have at least 2 prompts");
    }
}
