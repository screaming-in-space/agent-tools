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
        Assert.Contains(all, p => p.Category == "multi_turn");
        Assert.Contains(all, p => p.Category == "context_window");
    }

    [Fact]
    public void All_CountMatchesSumOfCategories()
    {
        var all = BenchmarkSuites.All();
        var sumOfParts = BenchmarkSuites.InstructionFollowing().Count
            + BenchmarkSuites.Extraction().Count
            + BenchmarkSuites.MarkdownGeneration().Count
            + BenchmarkSuites.Reasoning().Count
            + BenchmarkSuites.MultiTurn().Count
            + BenchmarkSuites.ContextWindow().Count;

        Assert.Equal(sumOfParts, all.Count);
    }

    [Theory]
    [InlineData("instruction_following")]
    [InlineData("extraction")]
    [InlineData("markdown_generation")]
    [InlineData("reasoning")]
    [InlineData("multi_turn")]
    [InlineData("context_window")]
    public void Category_AllPromptsHaveMatchingCategory(string category)
    {
        var prompts = category switch
        {
            "instruction_following" => BenchmarkSuites.InstructionFollowing(),
            "extraction" => BenchmarkSuites.Extraction(),
            "markdown_generation" => BenchmarkSuites.MarkdownGeneration(),
            "reasoning" => BenchmarkSuites.Reasoning(),
            "multi_turn" => BenchmarkSuites.MultiTurn(),
            "context_window" => BenchmarkSuites.ContextWindow(),
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

            if (p.IsMultiTurn)
            {
                Assert.All(p.Turns, t => Assert.False(string.IsNullOrWhiteSpace(t.UserMessage)));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(p.UserMessage));
            }
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

    [Fact]
    public void MultiTurn_AllPromptsAreMultiTurn()
    {
        var prompts = BenchmarkSuites.MultiTurn();

        Assert.True(prompts.Count >= 2, "MultiTurn should have at least 2 prompts");
        Assert.All(prompts, p =>
        {
            Assert.True(p.IsMultiTurn, $"{p.Name} should be multi-turn");
            Assert.True(p.Turns.Count >= 2, $"{p.Name} should have at least 2 turns");
        });
    }

    [Fact]
    public void MultiTurn_EachTurnHasExpectedOutput()
    {
        var prompts = BenchmarkSuites.MultiTurn();

        Assert.All(prompts, p =>
        {
            Assert.All(p.Turns, turn =>
            {
                Assert.False(string.IsNullOrWhiteSpace(turn.UserMessage), $"{p.Name}: turn has empty message");
                Assert.NotNull(turn.Expected);
                Assert.True(turn.Expected.PassThreshold > 0, $"{p.Name}: turn has zero pass threshold");
            });
        });
    }

    [Fact]
    public void ContextWindow_HasMinimumPromptCount()
    {
        var prompts = BenchmarkSuites.ContextWindow();

        Assert.True(prompts.Count >= 3, "ContextWindow should have at least 3 prompts (NIAH + multi-key + variable tracking)");
    }

    [Fact]
    public void ContextWindow_PromptsHaveLongContext()
    {
        var prompts = BenchmarkSuites.ContextWindow();

        Assert.All(prompts, p =>
        {
            // Context window prompts should have substantial user messages
            Assert.True(p.UserMessage.Length > 1000, $"{p.Name}: context window prompt should be >1000 chars, was {p.UserMessage.Length}");
        });
    }

    [Fact]
    public void AllPrompts_HaveValidDifficulty()
    {
        var all = BenchmarkSuites.All();

        Assert.All(all, p => Assert.True(
            p.Difficulty is BenchmarkDifficulty.Level1 or BenchmarkDifficulty.Level2 or BenchmarkDifficulty.Level3,
            $"{p.Name} has invalid difficulty"));
    }

    [Fact]
    public void UpToLevel_Level1_ExcludesHarderPrompts()
    {
        var level1Only = BenchmarkSuites.UpToLevel(BenchmarkDifficulty.Level1);
        var all = BenchmarkSuites.All();

        Assert.True(level1Only.Count < all.Count, "Level 1 filter should exclude some prompts");
        Assert.All(level1Only, p => Assert.Equal(BenchmarkDifficulty.Level1, p.Difficulty));
    }

    [Fact]
    public void UpToLevel_Level3_IncludesAll()
    {
        var level3 = BenchmarkSuites.UpToLevel(BenchmarkDifficulty.Level3);
        var all = BenchmarkSuites.All();

        Assert.Equal(all.Count, level3.Count);
    }

    // ── GetByCategory tests ───────────────────────────────────────────

    [Theory]
    [InlineData("instruction_following")]
    [InlineData("extraction")]
    [InlineData("markdown_generation")]
    [InlineData("reasoning")]
    [InlineData("multi_turn")]
    [InlineData("context_window")]
    public void GetByCategory_ReturnsCorrectSuite(string category)
    {
        var prompts = BenchmarkSuites.GetByCategory(category);

        Assert.True(prompts.Count > 0);
        Assert.All(prompts, p => Assert.Equal(category, p.Category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void GetByCategory_AllOrNull_ReturnsAllPrompts(string? category)
    {
        var prompts = BenchmarkSuites.GetByCategory(category);
        var all = BenchmarkSuites.All();

        Assert.Equal(all.Count, prompts.Count);
    }

    [Fact]
    public void GetByCategory_UnknownCategory_ReturnsAll()
    {
        var prompts = BenchmarkSuites.GetByCategory("nonexistent_category");
        var all = BenchmarkSuites.All();

        Assert.Equal(all.Count, prompts.Count);
    }

    // ── Description tests ─────────────────────────────────────────────

    [Fact]
    public void AllPrompts_HaveNonEmptyDescription()
    {
        var all = BenchmarkSuites.All();

        Assert.All(all, p => Assert.False(
            string.IsNullOrWhiteSpace(p.Description),
            $"{p.Name} is missing a Description"));
    }
}
