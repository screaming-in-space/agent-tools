using System.ClientModel;
using System.Text.RegularExpressions;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ModelBoss.Benchmarks;

/// <summary>
/// LLM-as-judge scorer inspired by MT-Bench. Uses the best-performing model
/// from the benchmark run to evaluate other models' responses on a 1-10 scale.
/// The judge model never evaluates its own responses.
/// </summary>
public sealed partial class LlmJudge(ILogger logger)
{
    /// <summary>
    /// Judges a single model's response to a benchmark prompt.
    /// Sends the original prompt, the model's response, and a scoring rubric to the judge model.
    /// Parses a 1-10 score from the judge's response.
    /// </summary>
    public async Task<JudgeResult> JudgeAsync(
        IChatClient judgeClient,
        string judgeModelId,
        BenchmarkPrompt prompt,
        string modelId,
        string modelResponse,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(judgeClient);
        ArgumentNullException.ThrowIfNull(prompt);
        modelResponse ??= "";

        var rubricPrompt = prompt.IsMultiTurn
            ? BuildMultiTurnRubric(prompt, modelResponse)
            : BuildSingleTurnRubric(prompt, modelResponse);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, JudgeSystemPrompt),
            new(ChatRole.User, rubricPrompt),
        };

        var chatOptions = new ChatOptions
        {
            Temperature = 0.0f,
            MaxOutputTokens = 1024,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            var responseBuilder = new System.Text.StringBuilder();

            await foreach (var update in judgeClient.GetStreamingResponseAsync(messages, chatOptions, timeoutCts.Token))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent text && text.Text is { Length: > 0 })
                    {
                        responseBuilder.Append(text.Text);
                    }
                }
            }

            var rawResponse = responseBuilder.ToString();
            var (score, parsed) = ParseScore(rawResponse);

            logger.LogInformation(
                "Judge scored {ModelId} on {Prompt}: {Score}/10 (parsed={Parsed})",
                modelId, prompt.Name, score, parsed);

            return new JudgeResult
            {
                ModelId = modelId,
                JudgeModelId = judgeModelId,
                PromptName = prompt.Name,
                Score = score,
                Reasoning = rawResponse,
                Parsed = parsed,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Judge timed out evaluating {ModelId} on {Prompt}", modelId, prompt.Name);
            await AgentErrorLog.LogAsync($"Judge:{prompt.Name}", $"Timed out judging {modelId}");

            return new JudgeResult
            {
                ModelId = modelId,
                JudgeModelId = judgeModelId,
                PromptName = prompt.Name,
                Score = 1,
                Reasoning = "Judge timed out",
                Parsed = false,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Judge failed evaluating {ModelId} on {Prompt}", modelId, prompt.Name);
            await AgentErrorLog.LogAsync($"Judge:{prompt.Name}", $"Judge failed for {modelId}: {ex.Message}", ex);

            return new JudgeResult
            {
                ModelId = modelId,
                JudgeModelId = judgeModelId,
                PromptName = prompt.Name,
                Score = 1,
                Reasoning = $"Judge error: {ex.Message}",
                Parsed = false,
            };
        }
    }

    /// <summary>
    /// Judges all prompts for a single model. Returns results keyed by prompt name.
    /// </summary>
    public async Task<Dictionary<string, JudgeResult>> JudgeSuiteAsync(
        IChatClient judgeClient,
        string judgeModelId,
        string modelId,
        IReadOnlyList<BenchmarkPrompt> prompts,
        Dictionary<string, string> rawOutputs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(rawOutputs);

        var results = new Dictionary<string, JudgeResult>(prompts.Count);

        foreach (var prompt in prompts)
        {
            ct.ThrowIfCancellationRequested();

            if (!rawOutputs.TryGetValue(prompt.Name, out var output) || string.IsNullOrEmpty(output))
            {
                continue;
            }

            var result = await JudgeAsync(judgeClient, judgeModelId, prompt, modelId, output, ct);
            results[prompt.Name] = result;
        }

        return results;
    }

    /// <summary>
    /// Builds an <see cref="IChatClient"/> for the judge model using the same pattern as
    /// <see cref="BenchmarkRunner"/>.
    /// </summary>
    public static IChatClient BuildJudgeClient(AgentModelOptions modelOptions)
    {
        ArgumentNullException.ThrowIfNull(modelOptions);

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(modelOptions.Endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(3),
        };

        var credential = new ApiKeyCredential(modelOptions.ApiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        return openAiClient
            .GetChatClient(string.IsNullOrEmpty(modelOptions.Model) ? "local" : modelOptions.Model)
            .AsIChatClient();
    }

    // ── Rubric construction ────────────────────────────────────────────

    private static string BuildSingleTurnRubric(BenchmarkPrompt prompt, string modelResponse)
    {
        return $"""
            [Prompt]
            System: {prompt.SystemMessage}
            User: {prompt.UserMessage}

            [Model Response]
            {modelResponse}

            [Scoring Criteria]
            Evaluate the response on these dimensions:
            1. Instruction Following: Did the model follow all instructions precisely?
            2. Accuracy: Is the content factually correct and relevant?
            3. Completeness: Does the response address the full scope of the prompt?
            4. Format Compliance: Does the response match the requested format (JSON, markdown, etc.)?
            5. Conciseness: Is the response appropriately concise without unnecessary filler?

            Provide your reasoning, then on the final line write exactly: [[score]] where score is 1-10.
            """;
    }

    private static string BuildMultiTurnRubric(BenchmarkPrompt prompt, string modelResponse)
    {
        var turnsText = new System.Text.StringBuilder();
        turnsText.AppendLine($"System: {prompt.SystemMessage}");

        for (var i = 0; i < prompt.Turns.Count; i++)
        {
            turnsText.AppendLine($"User (Turn {i + 1}): {prompt.Turns[i].UserMessage}");
        }

        return $"""
            [Multi-Turn Conversation]
            {turnsText}
            [Model Responses (separated by turn markers)]
            {modelResponse}

            [Scoring Criteria]
            Evaluate the multi-turn conversation on these dimensions:
            1. Instruction Following: Did the model follow instructions precisely in each turn?
            2. Context Retention: Did later turns reference and build upon earlier context?
            3. Coherence: Is the overall conversation coherent and logically consistent?
            4. Adaptation: Did the model correctly adapt when instructions changed between turns?
            5. Format Compliance: Did each turn match the requested format?

            Provide your reasoning, then on the final line write exactly: [[score]] where score is 1-10.
            """;
    }

    // ── Score parsing ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts the numeric score from the judge's response.
    /// Looks for the <c>[[N]]</c> pattern; falls back to last bare digit 1-10.
    /// </summary>
    internal static (int Score, bool Parsed) ParseScore(string judgeResponse)
    {
        if (string.IsNullOrWhiteSpace(judgeResponse))
        {
            return (1, false);
        }

        // Primary: [[N]] or [[NN]]
        var bracketMatch = BracketScoreRegex().Match(judgeResponse);
        if (bracketMatch.Success && int.TryParse(bracketMatch.Groups[1].Value, out var bracketScore))
        {
            return (Math.Clamp(bracketScore, 1, 10), true);
        }

        // Fallback: last occurrence of "score: N", "Score: N", "rating: N"
        var labelMatch = LabeledScoreRegex().Matches(judgeResponse);
        if (labelMatch.Count > 0)
        {
            var last = labelMatch[^1];
            if (int.TryParse(last.Groups[1].Value, out var labelScore))
            {
                return (Math.Clamp(labelScore, 1, 10), true);
            }
        }

        // Last resort: final standalone digit on a line
        var digitMatch = TrailingDigitRegex().Matches(judgeResponse);
        if (digitMatch.Count > 0)
        {
            var last = digitMatch[^1];
            if (int.TryParse(last.Groups[1].Value, out var digitScore) && digitScore is >= 1 and <= 10)
            {
                return (digitScore, true);
            }
        }

        return (1, false);
    }

    // ── Constants ──────────────────────────────────────────────────────

    private const string JudgeSystemPrompt = """
        You are an impartial judge evaluating AI model responses. Score each response on a scale of 1-10.

        Scoring guide:
        - 1-2: Completely wrong, off-topic, or refuses to answer
        - 3-4: Partially addresses the prompt but with major errors or missing key elements
        - 5-6: Adequate response with some issues (minor errors, unnecessary filler, slight format violations)
        - 7-8: Good response that follows instructions well with minor imperfections
        - 9-10: Excellent response that precisely follows all instructions with correct content and format

        Be strict. A score of 10 means flawless execution. Most competent responses should score 6-8.

        After your reasoning, you MUST end with the score in double brackets: [[score]]
        Example: [[7]]
        """;

    [GeneratedRegex(@"\[\[(\d{1,2})\]\]")]
    private static partial Regex BracketScoreRegex();

    [GeneratedRegex(@"(?:score|rating)\s*[:=]\s*(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledScoreRegex();

    [GeneratedRegex(@"^.*?(\d{1,2})\s*$", RegexOptions.Multiline)]
    private static partial Regex TrailingDigitRegex();
}
