using Agent.SDK.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelBoss.Benchmarks;

namespace ModelBoss.Tests;

/// <summary>
/// Per-benchmark integration tests. Each test runs a single prompt against
/// nemotron-3-nano-4b via LM Studio (localhost:1234). Tests skip automatically
/// when the endpoint is unreachable or the model is not loaded.
///
/// These are functional smoke tests — they verify the model produces non-empty
/// output and the pipeline doesn't crash. No accuracy scoring.
/// </summary>
[Collection("BenchmarkRunner")]
public sealed class BenchmarkRunnerIntegrationTests : IAsyncLifetime
{
    private static readonly AgentModelOptions ModelOptions = new()
    {
        Endpoint = "http://localhost:1234/v1",
        ApiKey = "no-key",
        Model = "unsloth/nvidia-nemotron-3-nano-4b",
        Temperature = 0.3f,
        MaxOutputTokens = 2048,
    };

    private static readonly BenchmarkRunOptions RunOptions = new()
    {
        ModelOptions = ModelOptions,
        WarmupIterations = 0,
        MeasuredIterations = 1,
    };

    private static readonly ILogger<BenchmarkRunner> Logger =
        NullLoggerFactory.Instance.CreateLogger<BenchmarkRunner>();

    private bool _endpointAvailable;
    private string _skipReason = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            var health = await EndpointHealthCheck.ValidateAsync(ModelOptions);

            if (!health.IsHealthy)
            {
                _skipReason = $"LM Studio not running: {health.Error}";
                return;
            }

            // Check if any loaded model contains "nemotron"
            var nemotronLoaded = health.LoadedModels
                .Exists(m => m.Contains("nemotron", StringComparison.OrdinalIgnoreCase));

            if (!nemotronLoaded)
            {
                _skipReason = $"nemotron not loaded. Loaded: {string.Join(", ", health.LoadedModels)}";
                return;
            }

            _endpointAvailable = true;
        }
        catch (Exception ex)
        {
            _skipReason = $"Health check failed: {ex.Message}";
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Instruction Following ─────────────────────────────────────────

    [Fact] public Task StrictFormatJson() => RunSingleBenchmark("strict_format_json");
    [Fact] public Task ConstrainedList() => RunSingleBenchmark("constrained_list");
    [Fact] public Task StopWhenTold() => RunSingleBenchmark("stop_when_told");
    [Fact] public Task MinimalPromptJson() => RunSingleBenchmark("minimal_prompt_json");
    [Fact] public Task MultiConstraint() => RunSingleBenchmark("multi_constraint");
    [Fact] public Task NegativeInstruction() => RunSingleBenchmark("negative_instruction");

    // ── Extraction ────────────────────────────────────────────────────

    [Fact] public Task ExtractModelSpecs() => RunSingleBenchmark("extract_model_specs");
    [Fact] public Task ExtractKeyValue() => RunSingleBenchmark("extract_key_value");
    [Fact] public Task ExtractFromNoise() => RunSingleBenchmark("extract_from_noise");
    [Fact] public Task ExtractNestedJson() => RunSingleBenchmark("extract_nested_json");

    // ── Markdown Generation ───────────────────────────────────────────

    [Fact] public Task GenerateContextMap() => RunSingleBenchmark("generate_context_map");
    [Fact] public Task GenerateTable() => RunSingleBenchmark("generate_table");

    // ── Reasoning ─────────────────────────────────────────────────────

    [Fact] public Task ModelSelectionReasoning() => RunSingleBenchmark("model_selection_reasoning");
    [Fact] public Task ComparativeAnalysis() => RunSingleBenchmark("comparative_analysis");
    [Fact] public Task QuantitativeReasoning() => RunSingleBenchmark("quantitative_reasoning");
    [Fact] public Task MultiStepDeduction() => RunSingleBenchmark("multi_step_deduction");

    // ── Multi-Turn ────────────────────────────────────────────────────

    [Fact] public Task MtRefineOutput() => RunSingleBenchmark("mt_refine_output");
    [Fact] public Task MtContextRetention() => RunSingleBenchmark("mt_context_retention");
    [Fact] public Task MtInstructionShift() => RunSingleBenchmark("mt_instruction_shift");

    // ── Context Window ────────────────────────────────────────────────

    [Fact] public Task Niah4k() => RunSingleBenchmark("niah_4k");
    [Fact] public Task Niah8k() => RunSingleBenchmark("niah_8k");
    [Fact] public Task NiahMultiKey() => RunSingleBenchmark("niah_multi_key");
    [Fact] public Task RulerVariableTracking() => RunSingleBenchmark("ruler_variable_tracking");

    // ── Runner ────────────────────────────────────────────────────────

    private async Task RunSingleBenchmark(string promptName)
    {
        if (!_endpointAvailable)
        {
            Assert.Skip(_skipReason);
        }

        var prompt = BenchmarkSuites.All().FirstOrDefault(p => p.Name == promptName);
        Assert.NotNull(prompt);

        var runner = new BenchmarkRunner(Logger);
        var results = await runner.RunAsync(prompt, RunOptions, CancellationToken.None);

        Assert.Single(results);
        var result = results[0];

        // Functional assertions — did the pipeline work?
        Assert.Equal(promptName, result.PromptName);
        Assert.True(result.TotalDuration > TimeSpan.Zero, "Duration should be positive");
        Assert.True(result.OutputTokens > 0 || !result.Success,
            "Successful run should produce at least one output token");
        Assert.False(string.IsNullOrEmpty(result.RawOutput) && result.Success,
            "Successful run should have non-empty output");
        Assert.True(result.InputTokens > 0, "Input tokens should be positive");
        Assert.True(result.TimeToFirstToken > TimeSpan.Zero, "TTFT should be positive");

        if (result.Success)
        {
            Assert.True(result.TokensPerSecond > 0, "Tok/s should be positive on success");
        }
    }
}
