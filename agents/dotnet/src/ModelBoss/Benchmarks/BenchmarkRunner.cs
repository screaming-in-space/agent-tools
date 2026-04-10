using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ModelBoss.Benchmarks;

/// <summary>
/// Executes benchmark prompts against a model endpoint and captures raw timing data.
/// Follows BenchmarkDotNet philosophy: warmup iterations, then measured iterations,
/// with precise stopwatch-based timing per request.
/// </summary>
public sealed class BenchmarkRunner(ILogger<BenchmarkRunner> logger)
{
    /// <summary>
    /// Runs a single benchmark prompt against the specified model.
    /// Includes <paramref name="warmupIterations"/> unmeasured runs followed by
    /// <paramref name="measuredIterations"/> measured runs. Returns all measured results.
    /// </summary>
    public async Task<IReadOnlyList<BenchmarkResult>> RunAsync(
        BenchmarkPrompt prompt,
        BenchmarkRunOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(options);

        var client = BuildChatClient(options);

        // ── Warmup ─────────────────────────────────────────────────────
        for (var w = 0; w < options.WarmupIterations; w++)
        {
            logger.LogDebug("Warmup {Iteration}/{Total} for {Prompt} on {Model}",
                w + 1, options.WarmupIterations, prompt.Name, options.ModelOptions.Model);

            await ExecuteSingleAsync(client, prompt, options.ModelOptions, ct);
        }

        // ── Measured iterations ────────────────────────────────────────
        var results = new List<BenchmarkResult>(options.MeasuredIterations);

        for (var i = 0; i < options.MeasuredIterations; i++)
        {
            logger.LogInformation("Iteration {Iteration}/{Total} for {Prompt} on {Model}",
                i + 1, options.MeasuredIterations, prompt.Name, options.ModelOptions.Model);

            var result = await ExecuteSingleAsync(client, prompt, options.ModelOptions, ct);
            results.Add(result);

            logger.LogInformation(
                "  → {TokensPerSec:F1} tok/s, TTFT={Ttft:F0}ms, Total={Total:F1}s, Tokens={Tokens}",
                result.TokensPerSecond,
                result.TimeToFirstToken.TotalMilliseconds,
                result.TotalDuration.TotalSeconds,
                result.OutputTokens);
        }

        return results;
    }

    /// <summary>
    /// Runs the full benchmark suite against one model. Returns results keyed by prompt name.
    /// </summary>
    public async Task<Dictionary<string, IReadOnlyList<BenchmarkResult>>> RunSuiteAsync(
        IReadOnlyList<BenchmarkPrompt> prompts,
        BenchmarkRunOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(options);

        var results = new Dictionary<string, IReadOnlyList<BenchmarkResult>>(prompts.Count);

        foreach (var prompt in prompts)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogInformation("── Benchmark: {PromptName} ({Category}) ──", prompt.Name, prompt.Category);
            var promptResults = await RunAsync(prompt, options, ct);
            results[prompt.Name] = promptResults;
        }

        return results;
    }

    private async Task<BenchmarkResult> ExecuteSingleAsync(
        IChatClient client,
        BenchmarkPrompt prompt,
        Agent.SDK.Configuration.AgentModelOptions modelOptions,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt.SystemMessage),
            new(ChatRole.User, prompt.UserMessage),
        };

        var chatOptions = new ChatOptions();
        if (modelOptions.Temperature.HasValue)
        {
            chatOptions.Temperature = modelOptions.Temperature;
        }

        if (modelOptions.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = modelOptions.MaxOutputTokens;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(prompt.Timeout);
        var promptCt = timeoutCts.Token;

        var sw = Stopwatch.StartNew();
        var ttftSw = Stopwatch.StartNew();
        var firstTokenReceived = false;
        var ttft = TimeSpan.Zero;
        var outputTokenCount = 0;
        var responseBuilder = new System.Text.StringBuilder();

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, promptCt))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    if (!firstTokenReceived)
                    {
                        ttft = ttftSw.Elapsed;
                        firstTokenReceived = true;
                    }

                    responseBuilder.Append(text);
                    outputTokenCount += EstimateTokens(text);
                }
            }

            sw.Stop();

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstTokenReceived ? ttft : sw.Elapsed,
                OutputTokens = outputTokenCount,
                InputTokens = EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                RawOutput = responseBuilder.ToString(),
                Success = true,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstTokenReceived ? ttft : sw.Elapsed,
                OutputTokens = outputTokenCount,
                InputTokens = EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                RawOutput = responseBuilder.ToString(),
                Success = false,
                Error = $"Timed out after {prompt.Timeout}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            logger.LogWarning(ex, "Benchmark {Prompt} failed on {Model}", prompt.Name, modelOptions.Model);

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstTokenReceived ? ttft : sw.Elapsed,
                OutputTokens = 0,
                InputTokens = EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                RawOutput = "",
                Success = false,
                Error = ex.Message,
            };
        }
    }

    private static IChatClient BuildChatClient(BenchmarkRunOptions options)
    {
        var modelOptions = options.ModelOptions;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(modelOptions.Endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(5),
        };

        var credential = new ApiKeyCredential(modelOptions.ApiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        return openAiClient
            .GetChatClient(string.IsNullOrEmpty(modelOptions.Model) ? "local" : modelOptions.Model)
            .AsIChatClient();
    }

    /// <summary>
    /// Rough token estimate: ~4 chars per token for English text.
    /// Good enough for benchmarking — we're comparing relative performance, not billing.
    /// </summary>
    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}

/// <summary>
/// Configuration for a benchmark run against one model.
/// </summary>
public sealed record BenchmarkRunOptions
{
    /// <summary>Model configuration to benchmark.</summary>
    public required Agent.SDK.Configuration.AgentModelOptions ModelOptions { get; init; }

    /// <summary>Number of warmup iterations (not measured). Default: 1.</summary>
    public int WarmupIterations { get; init; } = 1;

    /// <summary>Number of measured iterations per prompt. Default: 3.</summary>
    public int MeasuredIterations { get; init; } = 3;
}
