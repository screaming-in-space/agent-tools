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

    /// <summary>Tracks the active content section so headers are written only on transitions.</summary>
    private enum ContentMode { None, Thinking, Response }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _callCount);
        await AgentDebugLog.WriteAsync($"\n── LLM Call #{call} ──────────────────────────────────────\n");

        var mode = ContentMode.None;

        await foreach (var update in base.GetStreamingResponseAsync(
            messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                        if (mode != ContentMode.Thinking)
                        {
                            await AgentDebugLog.WriteAsync("\n[THINKING]\n");
                            mode = ContentMode.Thinking;
                        }

                        await AgentDebugLog.WriteAsync(reasoning.Text);
                        await output.AppendThinkingAsync(reasoning.Text);
                        break;

                    case TextContent text when text.Text is { Length: > 0 }:
                        if (mode != ContentMode.Response)
                        {
                            await AgentDebugLog.WriteAsync("\n[RESPONSE]\n");
                            mode = ContentMode.Response;
                        }

                        await AgentDebugLog.WriteAsync(text.Text);
                        await output.WriteResponseAsync(text.Text);
                        break;

                    case FunctionCallContent fcc:
                        mode = ContentMode.None;
                        var args = fcc.Arguments is not null
                            ? string.Join(", ", fcc.Arguments.Select(a => $"{a.Key}={AgentFileLog.Truncate(a.Value?.ToString(), 80)}"))
                            : "";
                        await AgentDebugLog.WriteAsync($"\n[TOOL_CALL] {fcc.Name}\n  ({args})\n");
                        break;

                    case FunctionResultContent frc:
                        mode = ContentMode.None;
                        await AgentDebugLog.WriteAsync($"\n[TOOL_RESULT] {frc.CallId}\n  {AgentFileLog.Truncate(frc.Result?.ToString(), 2000)}\n");
                        break;

                    default:
                        mode = ContentMode.None;
                        await AgentDebugLog.WriteAsync($"\n[{content.GetType().Name}]\n");
                        break;
                }
            }

            yield return update;
        }

        await AgentDebugLog.WriteAsync($"\n── End Call #{call} ─────────────────────────────────────\n\n");
        await AgentDebugLog.FlushAsync();
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
