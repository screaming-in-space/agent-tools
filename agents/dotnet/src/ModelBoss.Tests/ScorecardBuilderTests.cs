using Agent.SDK.Configuration;
using ModelBoss.Benchmarks;

namespace ModelBoss.Tests;

public class ScorecardBuilderTests
{
    private static readonly AgentModelOptions DefaultModelOptions = new()
    {
        Endpoint = "http://localhost:1234/v1",
        ApiKey = "no-key",
        Model = "test-model",
    };

    [Fact]
    public void Build_WithResults_ProducesValidScorecard()
    {
        var benchmarks = MakeBenchmarkResults("prompt_a", tokensPerSecond: 25.0, durationSec: 2.0);
        var accuracy = MakeAccuracyResult("prompt_a", score: 0.85, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal("test-model", scorecard.ModelId);
        Assert.Equal("default", scorecard.ConfigKey);
        Assert.True(scorecard.CompositeScore > 0);
        Assert.Equal(0.85, scorecard.MeanAccuracyScore);
        Assert.Equal(1, scorecard.PromptsPassedCount);
        Assert.Equal(1, scorecard.TotalPromptsCount);
    }

    [Fact]
    public void Build_EmptyResults_ReturnsZeroScores()
    {
        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>();
        var accuracy = new Dictionary<string, AccuracyResult>();

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(0, scorecard.MedianTokensPerSecond);
        Assert.Equal(0, scorecard.MeanAccuracyScore);
        Assert.Equal(0, scorecard.CompositeScore);
        Assert.Equal(0, scorecard.TotalPromptsCount);
        Assert.Empty(scorecard.PromptResults);
    }

    [Fact]
    public void Build_MultiplePrompts_AggregatesCorrectly()
    {
        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            ["prompt_a"] = [MakeSingleResult("prompt_a", tokensPerSecond: 20.0, durationSec: 3.0)],
            ["prompt_b"] = [MakeSingleResult("prompt_b", tokensPerSecond: 30.0, durationSec: 2.0)],
        };

        var accuracy = new Dictionary<string, AccuracyResult>
        {
            ["prompt_a"] = new AccuracyResult
            {
                ModelId = "test-model",
                PromptName = "prompt_a",
                Score = 0.9,
                Passed = true,
                Checks = [],
            },
            ["prompt_b"] = new AccuracyResult
            {
                ModelId = "test-model",
                PromptName = "prompt_b",
                Score = 0.7,
                Passed = true,
                Checks = [],
            },
        };

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(2, scorecard.TotalPromptsCount);
        Assert.Equal(2, scorecard.PromptsPassedCount);
        Assert.Equal(0.8, scorecard.MeanAccuracyScore, precision: 3);
        Assert.Equal(2, scorecard.PromptResults.Count);
    }

    [Fact]
    public void Build_FailedBenchmarks_ExcludedFromSpeedMetrics()
    {
        var failedResult = new BenchmarkResult
        {
            ModelId = "test-model",
            PromptName = "prompt_a",
            TotalDuration = TimeSpan.FromSeconds(5),
            TimeToFirstToken = TimeSpan.FromSeconds(5),
            OutputTokens = 0,
            InputTokens = 50,
            RawOutput = "",
            Success = false,
            Error = "Timeout",
        };

        var successResult = MakeSingleResult("prompt_a", tokensPerSecond: 25.0, durationSec: 2.0);

        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            ["prompt_a"] = [failedResult, successResult],
        };

        var accuracy = MakeAccuracyResult("prompt_a", score: 0.8, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(25.0, scorecard.MedianTokensPerSecond, precision: 1);
    }

    [Fact]
    public void Build_CompositeFormula_WeightsCorrectly()
    {
        // 50 tok/s = 1.0 normalized speed; accuracy = 1.0; pass rate = 1.0
        // Composite = (1.0 * 0.6) + (1.0 * 0.3) + (1.0 * 0.1) = 1.0
        var benchmarks = MakeBenchmarkResults("prompt_a", tokensPerSecond: 50.0, durationSec: 1.0);
        var accuracy = MakeAccuracyResult("prompt_a", score: 1.0, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(1.0, scorecard.CompositeScore, precision: 3);
    }

    [Fact]
    public void Build_SpeedNormalization_CapsAtOne()
    {
        // 100 tok/s should still normalize to 1.0, not 2.0
        var benchmarks = MakeBenchmarkResults("prompt_a", tokensPerSecond: 100.0, durationSec: 0.5);
        var accuracy = MakeAccuracyResult("prompt_a", score: 1.0, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(1.0, scorecard.CompositeScore, precision: 3);
    }

    [Fact]
    public void Build_PassRate_ReflectsPassedCount()
    {
        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            ["prompt_a"] = [MakeSingleResult("prompt_a", tokensPerSecond: 25.0, durationSec: 2.0)],
            ["prompt_b"] = [MakeSingleResult("prompt_b", tokensPerSecond: 25.0, durationSec: 2.0)],
        };

        var accuracy = new Dictionary<string, AccuracyResult>
        {
            ["prompt_a"] = new AccuracyResult
            {
                ModelId = "test-model",
                PromptName = "prompt_a",
                Score = 0.9,
                Passed = true,
                Checks = [],
            },
            ["prompt_b"] = new AccuracyResult
            {
                ModelId = "test-model",
                PromptName = "prompt_b",
                Score = 0.3,
                Passed = false,
                Checks = [],
            },
        };

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.Equal(0.5, scorecard.PassRate);
        Assert.Equal(1, scorecard.PromptsPassedCount);
    }

    [Fact]
    public void Build_WithRegistryInfo_PreservesMetadata()
    {
        var registry = new ModelSummary
        {
            ParamsB = 4.0,
            ActiveParamsB = 4.0,
            Architecture = "dense",
            ContextK = 128,
            VramQ4Gb = 3,
            ToolCalling = "full",
            Thinking = true,
        };

        var benchmarks = MakeBenchmarkResults("prompt_a", tokensPerSecond: 25.0, durationSec: 2.0);
        var accuracy = MakeAccuracyResult("prompt_a", score: 0.8, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registry);

        Assert.NotNull(scorecard.RegistryInfo);
        Assert.Equal(4.0, scorecard.RegistryInfo.ParamsB);
        Assert.Equal("dense", scorecard.RegistryInfo.Architecture);
    }

    [Fact]
    public void Build_WithThinkingTokens_PopulatesThinkingMetrics()
    {
        var result = MakeThinkingResult(
            "prompt_a", outputTokens: 50, thinkingTokens: 200,
            durationSec: 10.0, thinkingDurationSec: 8.0);

        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            ["prompt_a"] = [result],
        };
        var accuracy = MakeAccuracyResult("prompt_a", score: 0.9, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.True(scorecard.UsesThinking);
        Assert.Equal(200, scorecard.TotalThinkingTokens);
        Assert.Equal(TimeSpan.FromSeconds(8.0), scorecard.MedianThinkingDuration);
    }

    [Fact]
    public void Build_WithThinkingTokens_SeparatesGenerationRate()
    {
        // 10s total, 8s thinking → 2s generation time, 50 output tokens → 25 gen tok/s
        var result = MakeThinkingResult(
            "prompt_a", outputTokens: 50, thinkingTokens: 200,
            durationSec: 10.0, thinkingDurationSec: 8.0);

        var benchmarks = new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            ["prompt_a"] = [result],
        };
        var accuracy = MakeAccuracyResult("prompt_a", score: 0.9, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        // Overall: 50 / 10 = 5.0 tok/s
        Assert.Equal(5.0, scorecard.MedianTokensPerSecond, precision: 1);
        // Generation only: 50 / 2 = 25.0 tok/s
        Assert.Equal(25.0, scorecard.MedianGenerationTokensPerSecond, precision: 1);
    }

    [Fact]
    public void Build_WithoutThinkingTokens_UsesThinkingIsFalse()
    {
        var benchmarks = MakeBenchmarkResults("prompt_a", tokensPerSecond: 25.0, durationSec: 2.0);
        var accuracy = MakeAccuracyResult("prompt_a", score: 0.8, passed: true);

        var scorecard = ScorecardBuilder.Build(
            "default", DefaultModelOptions, benchmarks, accuracy, registryInfo: null);

        Assert.False(scorecard.UsesThinking);
        Assert.Equal(0, scorecard.TotalThinkingTokens);
        Assert.Equal(TimeSpan.Zero, scorecard.MedianThinkingDuration);
    }

    [Fact]
    public void BenchmarkResult_GenerationTokensPerSecond_ExcludesThinkingTime()
    {
        // 5s total, 3s thinking → 2s generation, 40 output tokens → 20 gen tok/s
        var result = MakeThinkingResult(
            "prompt_a", outputTokens: 40, thinkingTokens: 100,
            durationSec: 5.0, thinkingDurationSec: 3.0);

        Assert.Equal(20.0, result.GenerationTokensPerSecond, precision: 1);
        // Total tok/s: 40 / 5 = 8.0
        Assert.Equal(8.0, result.TokensPerSecond, precision: 1);
    }

    [Fact]
    public void BenchmarkResult_GenerationTokensPerSecond_NoThinking_EqualsTokensPerSecond()
    {
        var result = MakeSingleResult("prompt_a", tokensPerSecond: 30.0, durationSec: 2.0);

        // No thinking → generation time == total time → rates should match
        Assert.Equal(result.TokensPerSecond, result.GenerationTokensPerSecond, precision: 1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static BenchmarkResult MakeSingleResult(
        string promptName, double tokensPerSecond, double durationSec)
    {
        var duration = TimeSpan.FromSeconds(durationSec);
        var tokens = (int)(tokensPerSecond * durationSec);

        return new BenchmarkResult
        {
            ModelId = "test-model",
            PromptName = promptName,
            TotalDuration = duration,
            TimeToFirstToken = TimeSpan.FromMilliseconds(150),
            OutputTokens = tokens,
            InputTokens = 50,
            RawOutput = new string('x', tokens * 4),
            Success = true,
        };
    }

    private static Dictionary<string, IReadOnlyList<BenchmarkResult>> MakeBenchmarkResults(
        string promptName, double tokensPerSecond, double durationSec)
    {
        return new Dictionary<string, IReadOnlyList<BenchmarkResult>>
        {
            [promptName] = [MakeSingleResult(promptName, tokensPerSecond, durationSec)],
        };
    }

    private static Dictionary<string, AccuracyResult> MakeAccuracyResult(
        string promptName, double score, bool passed)
    {
        return new Dictionary<string, AccuracyResult>
        {
            [promptName] = new AccuracyResult
            {
                ModelId = "test-model",
                PromptName = promptName,
                Score = score,
                Passed = passed,
                Checks = [],
            },
        };
    }

    private static BenchmarkResult MakeThinkingResult(
        string promptName, int outputTokens, int thinkingTokens,
        double durationSec, double thinkingDurationSec)
    {
        var duration = TimeSpan.FromSeconds(durationSec);
        var thinkingDuration = TimeSpan.FromSeconds(thinkingDurationSec);

        return new BenchmarkResult
        {
            ModelId = "test-model",
            PromptName = promptName,
            TotalDuration = duration,
            TimeToFirstToken = TimeSpan.FromMilliseconds(150) + thinkingDuration,
            TimeToFirstThinking = TimeSpan.FromMilliseconds(150),
            ThinkingDuration = thinkingDuration,
            OutputTokens = outputTokens,
            ThinkingTokens = thinkingTokens,
            InputTokens = 50,
            RawOutput = new string('x', outputTokens * 4),
            Success = true,
        };
    }
}
