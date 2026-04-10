using System.ClientModel;
using System.Diagnostics;
using Agent.SDK.Console;
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

            if (prompt.IsMultiTurn)
            {
                await ExecuteMultiTurnAsync(client, prompt, options.ModelOptions, ct);
            }
            else
            {
                await ExecuteSingleAsync(client, prompt, options.ModelOptions, ct);
            }
        }

        // ── Measured iterations ────────────────────────────────────────
        var results = new List<BenchmarkResult>(options.MeasuredIterations);

        for (var i = 0; i < options.MeasuredIterations; i++)
        {
            logger.LogInformation("Iteration {Iteration}/{Total} for {Prompt} on {Model}",
                i + 1, options.MeasuredIterations, prompt.Name, options.ModelOptions.Model);

            var result = prompt.IsMultiTurn
                ? await ExecuteMultiTurnAsync(client, prompt, options.ModelOptions, ct)
                : await ExecuteSingleAsync(client, prompt, options.ModelOptions, ct);
            results.Add(result);

            logger.LogInformation(
                "  → {TokensPerSec:F1} tok/s (gen: {GenTokS:F1}), TTFT={Ttft:F0}ms, Think={ThinkTok} tok/{ThinkMs:F0}ms, Total={Total:F1}s",
                result.TokensPerSecond,
                result.GenerationTokensPerSecond,
                result.TimeToFirstToken.TotalMilliseconds,
                result.ThinkingTokens,
                result.ThinkingDuration.TotalMilliseconds,
                result.TotalDuration.TotalSeconds);
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
        var firstVisibleToken = false;
        var firstThinkingToken = false;
        var ttft = TimeSpan.Zero;
        var ttfThinking = TimeSpan.Zero;
        var lastThinkingTime = TimeSpan.Zero;
        var outputTokenCount = 0;
        var thinkingTokenCount = 0;
        var responseBuilder = new System.Text.StringBuilder();

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, promptCt))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                            if (!firstThinkingToken)
                            {
                                ttfThinking = ttftSw.Elapsed;
                                firstThinkingToken = true;
                            }

                            lastThinkingTime = sw.Elapsed;
                            thinkingTokenCount += EstimateTokens(reasoning.Text);
                            break;

                        case TextContent text when text.Text is { Length: > 0 }:
                            if (!firstVisibleToken)
                            {
                                ttft = ttftSw.Elapsed;
                                firstVisibleToken = true;
                            }

                            responseBuilder.Append(text.Text);
                            outputTokenCount += EstimateTokens(text.Text);
                            break;
                    }
                }
            }

            sw.Stop();

            var thinkingDuration = firstThinkingToken
                ? (firstVisibleToken ? ttft - ttfThinking : lastThinkingTime - ttfThinking)
                : TimeSpan.Zero;

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : sw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : sw.Elapsed,
                ThinkingDuration = thinkingDuration,
                OutputTokens = outputTokenCount,
                ThinkingTokens = thinkingTokenCount,
                InputTokens = EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                RawOutput = responseBuilder.ToString(),
                Success = true,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();

            var thinkingDuration = firstThinkingToken
                ? (firstVisibleToken ? ttft - ttfThinking : lastThinkingTime - ttfThinking)
                : TimeSpan.Zero;

            await AgentErrorLog.LogAsync(
                $"Benchmark:{prompt.Name}",
                $"Timed out after {prompt.Timeout} on {modelOptions.Model} ({outputTokenCount} tokens generated before timeout)");

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : sw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : sw.Elapsed,
                ThinkingDuration = thinkingDuration,
                OutputTokens = outputTokenCount,
                ThinkingTokens = thinkingTokenCount,
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
            await AgentErrorLog.LogAsync($"Benchmark:{prompt.Name}", $"Failed on {modelOptions.Model}: {ex.Message}", ex);

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = sw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : sw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : sw.Elapsed,
                ThinkingDuration = TimeSpan.Zero,
                OutputTokens = 0,
                ThinkingTokens = 0,
                InputTokens = EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                RawOutput = "",
                Success = false,
                Error = ex.Message,
            };
        }
    }

    /// <summary>
    /// Executes a multi-turn conversation benchmark (MT-Bench style).
    /// Sends each turn sequentially, maintaining full conversation history.
    /// Returns a single aggregated BenchmarkResult with all turns' output concatenated
    /// (separated by turn markers for per-turn scoring).
    /// </summary>
    private async Task<BenchmarkResult> ExecuteMultiTurnAsync(
        IChatClient client,
        BenchmarkPrompt prompt,
        Agent.SDK.Configuration.AgentModelOptions modelOptions,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt.SystemMessage),
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

        var overallSw = Stopwatch.StartNew();
        var firstVisibleToken = false;
        var firstThinkingToken = false;
        var ttft = TimeSpan.Zero;
        var ttfThinking = TimeSpan.Zero;
        var lastThinkingTime = TimeSpan.Zero;
        var totalOutputTokens = 0;
        var totalThinkingTokens = 0;
        var totalInputTokens = EstimateTokens(prompt.SystemMessage);
        var responseBuilder = new System.Text.StringBuilder();

        try
        {
            for (var turnIndex = 0; turnIndex < prompt.Turns.Count; turnIndex++)
            {
                var turn = prompt.Turns[turnIndex];
                messages.Add(new ChatMessage(ChatRole.User, turn.UserMessage));
                totalInputTokens += EstimateTokens(turn.UserMessage);

                if (turnIndex > 0)
                {
                    responseBuilder.Append($"\n---TURN_{turnIndex + 1}---\n");
                }

                logger.LogDebug("  Turn {Turn}/{Total} for {Prompt}",
                    turnIndex + 1, prompt.Turns.Count, prompt.Name);

                var turnBuilder = new System.Text.StringBuilder();
                var ttftSw = Stopwatch.StartNew();

                await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, promptCt))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                                if (!firstThinkingToken)
                                {
                                    ttfThinking = overallSw.Elapsed;
                                    firstThinkingToken = true;
                                }

                                lastThinkingTime = overallSw.Elapsed;
                                totalThinkingTokens += EstimateTokens(reasoning.Text);
                                break;

                            case TextContent text when text.Text is { Length: > 0 }:
                                if (!firstVisibleToken)
                                {
                                    ttft = overallSw.Elapsed;
                                    firstVisibleToken = true;
                                }

                                turnBuilder.Append(text.Text);
                                totalOutputTokens += EstimateTokens(text.Text);
                                break;
                        }
                    }
                }

                var turnResponse = turnBuilder.ToString();
                responseBuilder.Append(turnResponse);

                // Add assistant response to conversation history for next turn
                messages.Add(new ChatMessage(ChatRole.Assistant, turnResponse));
                totalInputTokens += EstimateTokens(turnResponse);
            }

            overallSw.Stop();

            var thinkingDuration = firstThinkingToken
                ? (firstVisibleToken ? ttft - ttfThinking : lastThinkingTime - ttfThinking)
                : TimeSpan.Zero;

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = overallSw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : overallSw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : overallSw.Elapsed,
                ThinkingDuration = thinkingDuration,
                OutputTokens = totalOutputTokens,
                ThinkingTokens = totalThinkingTokens,
                InputTokens = totalInputTokens,
                RawOutput = responseBuilder.ToString(),
                Success = true,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            overallSw.Stop();

            var thinkingDuration = firstThinkingToken
                ? (firstVisibleToken ? ttft - ttfThinking : lastThinkingTime - ttfThinking)
                : TimeSpan.Zero;

            await AgentErrorLog.LogAsync(
                $"Benchmark:{prompt.Name}",
                $"Multi-turn timed out after {prompt.Timeout} on {modelOptions.Model} ({totalOutputTokens} tokens before timeout)");

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = overallSw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : overallSw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : overallSw.Elapsed,
                ThinkingDuration = thinkingDuration,
                OutputTokens = totalOutputTokens,
                ThinkingTokens = totalThinkingTokens,
                InputTokens = totalInputTokens,
                RawOutput = responseBuilder.ToString(),
                Success = false,
                Error = $"Timed out after {prompt.Timeout}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            overallSw.Stop();
            logger.LogWarning(ex, "Multi-turn benchmark {Prompt} failed on {Model}", prompt.Name, modelOptions.Model);
            await AgentErrorLog.LogAsync($"Benchmark:{prompt.Name}", $"Multi-turn failed on {modelOptions.Model}: {ex.Message}", ex);

            return new BenchmarkResult
            {
                ModelId = modelOptions.Model,
                PromptName = prompt.Name,
                TotalDuration = overallSw.Elapsed,
                TimeToFirstToken = firstVisibleToken ? ttft : overallSw.Elapsed,
                TimeToFirstThinking = firstThinkingToken ? ttfThinking : overallSw.Elapsed,
                ThinkingDuration = TimeSpan.Zero,
                OutputTokens = 0,
                ThinkingTokens = 0,
                InputTokens = totalInputTokens,
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
