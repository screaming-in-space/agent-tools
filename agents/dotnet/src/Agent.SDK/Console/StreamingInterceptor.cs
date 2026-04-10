using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Agent.SDK.Console;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that intercepts streaming content from
/// every LLM call — including intermediate calls between tool invocations.
/// Routes thinking tokens to <see cref="IAgentOutput.AppendThinking"/> and
/// response tokens to <see cref="IAgentOutput.WriteResponse"/>.
/// Delegates debug tracing to <see cref="AgentDebugLog"/>.
/// </summary>
public sealed class StreamingInterceptor(IChatClient inner, IAgentOutput output) : DelegatingChatClient(inner)
{
    private int _callCount;

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _callCount);
        AgentDebugLog.Write($"\n── LLM Call #{call} ──────────────────────────────────────\n");

        await foreach (var update in base.GetStreamingResponseAsync(
            messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                        AgentDebugLog.Write($"[THINKING] {reasoning.Text}");
                        await output.AppendThinkingAsync(reasoning.Text);
                        break;

                    case TextContent text when text.Text is { Length: > 0 }:
                        AgentDebugLog.Write($"[RESPONSE] {text.Text}");
                        await output.WriteResponseAsync(text.Text);
                        break;

                    case FunctionCallContent fcc:
                        var args = fcc.Arguments is not null
                            ? string.Join(", ", fcc.Arguments.Select(a => $"{a.Key}={AgentDebugLog.Truncate(a.Value?.ToString(), 80)}"))
                            : "";
                        AgentDebugLog.Write($"\n[TOOL_CALL] {fcc.Name}({args})\n");
                        break;

                    case FunctionResultContent frc:
                        AgentDebugLog.Write($"\n[TOOL_RESULT] {frc.CallId} = {AgentDebugLog.Truncate(frc.Result?.ToString(), 2000)}\n");
                        break;

                    default:
                        AgentDebugLog.Write($"[{content.GetType().Name}]");
                        break;
                }
            }

            yield return update;
        }

        AgentDebugLog.Write($"\n── End Call #{call} ─────────────────────────────────────\n\n");
        AgentDebugLog.Flush();
    }
}

/// <summary>
/// Extension method to insert a <see cref="StreamingInterceptor"/> into the pipeline.
/// </summary>
public static class StreamingInterceptorExtensions
{
    public static ChatClientBuilder UseStreamingInterceptor(
        this ChatClientBuilder builder,
        IAgentOutput output)
    {
        return builder.Use(inner => new StreamingInterceptor(inner, output));
    }
}
