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
public sealed class BenchmarkRunner(ILogger<BenchmarkRunner> logger, IAgentOutput? output = null)
{
    /// <summary>
    /// Runs a single benchmark prompt against the specified model.
    /// Includes warmup iterations followed by measured iterations. Returns all measured results.
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

            if (output is not null)
            {
                await output.ReportTestStartedAsync(
                    prompt.Name, prompt.Category, prompt.Description, options.ModelOptions.Model);
            }

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

        var chatOptions = BuildChatOptions(modelOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(prompt.Timeout);
        var promptCt = timeoutCts.Token;

        var tracker = new StreamingTokenTracker(output);

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, promptCt))
            {
                foreach (var content in update.Contents)
                {
                    tracker.Process(content);
                }
            }

            tracker.Stop();
            return tracker.BuildResult(modelOptions.Model, prompt.Name,
                EstimateTokens(prompt.SystemMessage + prompt.UserMessage), success: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            tracker.Stop();
            await AgentErrorLog.LogAsync(
                $"Benchmark:{prompt.Name}",
                $"Timed out after {prompt.Timeout} on {modelOptions.Model} ({tracker.OutputTokenCount} tokens generated before timeout)");

            return tracker.BuildResult(modelOptions.Model, prompt.Name,
                EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                success: false, error: $"Timed out after {prompt.Timeout}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tracker.Stop();
            logger.LogWarning(ex, "Benchmark {Prompt} failed on {Model}", prompt.Name, modelOptions.Model);
            await AgentErrorLog.LogAsync($"Benchmark:{prompt.Name}", $"Failed on {modelOptions.Model}: {ex.Message}", ex);

            return tracker.BuildResult(modelOptions.Model, prompt.Name,
                EstimateTokens(prompt.SystemMessage + prompt.UserMessage),
                success: false, error: ex.Message, clearOutput: true);
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

        var chatOptions = BuildChatOptions(modelOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(prompt.Timeout);
        var promptCt = timeoutCts.Token;

        var tracker = new StreamingTokenTracker(output);
        var totalInputTokens = EstimateTokens(prompt.SystemMessage);

        try
        {
            for (var turnIndex = 0; turnIndex < prompt.Turns.Count; turnIndex++)
            {
                var turn = prompt.Turns[turnIndex];
                messages.Add(new ChatMessage(ChatRole.User, turn.UserMessage));
                totalInputTokens += EstimateTokens(turn.UserMessage);

                if (turnIndex > 0)
                {
                    tracker.AppendTurnMarker(turnIndex + 1);
                }

                logger.LogDebug("  Turn {Turn}/{Total} for {Prompt}",
                    turnIndex + 1, prompt.Turns.Count, prompt.Name);

                var turnBuilder = new System.Text.StringBuilder();

                await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, promptCt))
                {
                    foreach (var content in update.Contents)
                    {
                        tracker.Process(content);

                        if (content is TextContent text && text.Text is { Length: > 0 })
                        {
                            turnBuilder.Append(text.Text);
                        }
                    }
                }

                var turnResponse = turnBuilder.ToString();

                // Add assistant response to conversation history for next turn
                messages.Add(new ChatMessage(ChatRole.Assistant, turnResponse));
                totalInputTokens += EstimateTokens(turnResponse);
            }

            tracker.Stop();
            return tracker.BuildResult(modelOptions.Model, prompt.Name, totalInputTokens, success: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            tracker.Stop();
            await AgentErrorLog.LogAsync(
                $"Benchmark:{prompt.Name}",
                $"Multi-turn timed out after {prompt.Timeout} on {modelOptions.Model} ({tracker.OutputTokenCount} tokens before timeout)");

            return tracker.BuildResult(modelOptions.Model, prompt.Name, totalInputTokens,
                success: false, error: $"Timed out after {prompt.Timeout}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tracker.Stop();
            logger.LogWarning(ex, "Multi-turn benchmark {Prompt} failed on {Model}", prompt.Name, modelOptions.Model);
            await AgentErrorLog.LogAsync($"Benchmark:{prompt.Name}", $"Multi-turn failed on {modelOptions.Model}: {ex.Message}", ex);

            return tracker.BuildResult(modelOptions.Model, prompt.Name, totalInputTokens,
                success: false, error: ex.Message, clearOutput: true);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static ChatOptions BuildChatOptions(Agent.SDK.Configuration.AgentModelOptions modelOptions)
    {
        var chatOptions = new ChatOptions();
        if (modelOptions.Temperature.HasValue)
        {
            chatOptions.Temperature = modelOptions.Temperature;
        }

        if (modelOptions.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = modelOptions.MaxOutputTokens;
        }

        return chatOptions;
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
    internal static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    // ── StreamingTokenTracker ─────────────────────────────────────────

    /// <summary>
    /// Encapsulates all streaming token tracking state: TTFT, thinking/response token counts,
    /// response buffer, and timing. Used by both single-turn and multi-turn execution paths.
    /// </summary>
    private sealed class StreamingTokenTracker(IAgentOutput? output)
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly Stopwatch _ttftSw = Stopwatch.StartNew();
        private readonly System.Text.StringBuilder _responseBuilder = new();

        private bool _firstVisibleToken;
        private bool _firstThinkingToken;
        private TimeSpan _ttft;
        private TimeSpan _ttfThinking;
        private TimeSpan _lastThinkingTime;

        public int OutputTokenCount { get; private set; }
        public int ThinkingTokenCount { get; private set; }

        public void Process(AIContent content)
        {
            switch (content)
            {
                case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                    if (!_firstThinkingToken)
                    {
                        _ttfThinking = _ttftSw.Elapsed;
                        _firstThinkingToken = true;
                    }

                    _lastThinkingTime = _sw.Elapsed;
                    ThinkingTokenCount += EstimateTokens(reasoning.Text);
                    output?.AppendThinkingAsync(reasoning.Text);
                    break;

                case TextContent text when text.Text is { Length: > 0 }:
                    if (!_firstVisibleToken)
                    {
                        _ttft = _ttftSw.Elapsed;
                        _firstVisibleToken = true;
                    }

                    _responseBuilder.Append(text.Text);
                    OutputTokenCount += EstimateTokens(text.Text);
                    output?.WriteResponseAsync(text.Text);
                    break;
            }
        }

        public void AppendTurnMarker(int turnNumber)
        {
            _responseBuilder.Append($"\n---TURN_{turnNumber}---\n");
        }

        public void Stop()
        {
            _sw.Stop();
        }

        public BenchmarkResult BuildResult(
            string modelId, string promptName, int inputTokens,
            bool success, string? error = null, bool clearOutput = false)
        {
            var thinkingDuration = _firstThinkingToken
                ? (_firstVisibleToken ? _ttft - _ttfThinking : _lastThinkingTime - _ttfThinking)
                : TimeSpan.Zero;

            return new BenchmarkResult
            {
                ModelId = modelId,
                PromptName = promptName,
                TotalDuration = _sw.Elapsed,
                TimeToFirstToken = _firstVisibleToken ? _ttft : _sw.Elapsed,
                TimeToFirstThinking = _firstThinkingToken ? _ttfThinking : _sw.Elapsed,
                ThinkingDuration = clearOutput ? TimeSpan.Zero : thinkingDuration,
                OutputTokens = clearOutput ? 0 : OutputTokenCount,
                ThinkingTokens = clearOutput ? 0 : ThinkingTokenCount,
                InputTokens = inputTokens,
                RawOutput = clearOutput ? "" : _responseBuilder.ToString(),
                Success = success,
                Error = error,
            };
        }
    }
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
