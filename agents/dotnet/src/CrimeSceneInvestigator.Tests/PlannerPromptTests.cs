using Agent.SDK.Configuration;

namespace CrimeSceneInvestigator.Tests;

public sealed class PlannerPromptTests
{
    private static readonly string[] AllScannerNames =
        ["markdown", "structure", "rules", "quality", "journal", "done"];

    private static readonly IReadOnlyList<string> DefaultLoadedModels =
        ["qwen2.5-7b-instruct"];

    private static readonly IReadOnlyDictionary<string, AgentModelOptions> DefaultConfiguredModels =
        new Dictionary<string, AgentModelOptions>
        {
            ["default"] = new() { Model = "qwen2.5-7b-instruct", Endpoint = "http://localhost:1234/v1" },
        };

    // ── Build returns scanner manifest information ──

    [Fact]
    public void Build_WithAllScanners_ContainsScannerManifestInfo()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("tools", prompt);
        Assert.Contains("complexity", prompt);
    }

    // ── Build includes all scanner names ──

    [Theory]
    [InlineData("markdown")]
    [InlineData("structure")]
    [InlineData("rules")]
    [InlineData("quality")]
    [InlineData("journal")]
    [InlineData("done")]
    public void Build_WithAllScanners_ContainsScannerName(string scannerName)
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains($"**{scannerName}**", prompt);
    }

    [Fact]
    public void Build_WithSubsetOfScanners_ExcludesDisabledScanners()
    {
        string[] enabled = ["markdown", "rules"];

        var prompt = PlannerPrompt.Build(enabled, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("**markdown**", prompt);
        Assert.Contains("**rules**", prompt);
        Assert.DoesNotContain("**quality**", prompt);
        Assert.DoesNotContain("**journal**", prompt);
    }

    // ── Build includes complexity ratings ──

    [Theory]
    [InlineData("light")]
    [InlineData("medium")]
    [InlineData("heavy")]
    public void Build_WithAllScanners_ContainsComplexityRating(string complexity)
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains($"{complexity} complexity", prompt);
    }

    // ── Build includes JSON output format instructions ──

    [Fact]
    public void Build_ReturnsPrompt_ContainsJsonOutputFormat()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("JSON object", prompt);
        Assert.Contains("scanner name to config key", prompt);
    }

    [Fact]
    public void Build_ReturnsPrompt_ContainsJsonExample()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        // The example JSON has scanner-to-key mappings
        Assert.Contains("\"markdown\":", prompt);
        Assert.Contains("\"default\"", prompt);
    }

    // ── Build includes model information ──

    [Fact]
    public void Build_WithConfiguredModels_ContainsModelConfigKey()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("**default**", prompt);
    }

    [Fact]
    public void Build_WithConfiguredModels_ContainsModelEndpoint()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("http://localhost:1234/v1", prompt);
    }

    [Fact]
    public void Build_WithConfiguredModels_ContainsModelName()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("qwen2.5-7b-instruct", prompt);
    }

    [Fact]
    public void Build_WithLoadedModels_ContainsLoadedModelId()
    {
        var prompt = PlannerPrompt.Build(AllScannerNames, DefaultLoadedModels, DefaultConfiguredModels);

        Assert.Contains("qwen2.5-7b-instruct", prompt);
    }

    [Fact]
    public void Build_WithMultipleModels_ContainsAllConfigKeys()
    {
        var configured = new Dictionary<string, AgentModelOptions>
        {
            ["default"] = new() { Model = "qwen2.5-7b-instruct", Endpoint = "http://localhost:1234/v1" },
            ["gemma-26b"] = new() { Model = "gemma-2-27b-it", Endpoint = "http://localhost:1234/v1" },
        };
        string[] loaded = ["qwen2.5-7b-instruct", "gemma-2-27b-it"];

        var prompt = PlannerPrompt.Build(AllScannerNames, loaded, configured);

        Assert.Contains("**default**", prompt);
        Assert.Contains("**gemma-26b**", prompt);
        Assert.Contains("gemma-2-27b-it", prompt);
    }

    // ── AllScanners static data ──

    [Fact]
    public void AllScanners_ContainsExpectedCount()
    {
        Assert.Equal(6, PlannerPrompt.AllScanners.Length);
    }

    [Theory]
    [InlineData("markdown", "light")]
    [InlineData("structure", "light")]
    [InlineData("rules", "heavy")]
    [InlineData("quality", "heavy")]
    [InlineData("journal", "medium")]
    [InlineData("done", "medium")]
    public void AllScanners_HasCorrectComplexity(string name, string expectedComplexity)
    {
        var scanner = Assert.Single(PlannerPrompt.AllScanners, s => s.Name == name);

        Assert.Equal(expectedComplexity, scanner.Complexity);
    }
}
