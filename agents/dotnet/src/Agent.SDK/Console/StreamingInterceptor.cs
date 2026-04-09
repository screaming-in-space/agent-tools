using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Agent.SDK.Console;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that intercepts streaming text from
/// every LLM call — including intermediate calls between tool invocations.
/// <para>
/// Insert this <b>before</b> <c>UseFunctionInvocation</c> in the pipeline so
/// it sees each individual LLM round-trip, not just the final response.
/// </para>
/// </summary>
public sealed class StreamingInterceptor : DelegatingChatClient
{
    private readonly IAgentOutput _output;

    public StreamingInterceptor(IChatClient inner, IAgentOutput output) : base(inner)
    {
        _output = output;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(
            messages, options, cancellationToken).ConfigureAwait(false))
        {
            // Route text content to the output for live display
            if (update.Text is { Length: > 0 } text)
            {
                _output.AppendThinking(text);
            }

            yield return update;
        }
    }
}

/// <summary>
/// Extension method to insert a <see cref="StreamingInterceptor"/> into the pipeline.
/// </summary>
public static class StreamingInterceptorExtensions
{
    /// <summary>
    /// Adds a streaming interceptor that feeds LLM text tokens to <see cref="IAgentOutput.AppendThinking"/>.
    /// Call this <b>before</b> <c>UseFunctionInvocation</c> in the builder chain.
    /// </summary>
    public static ChatClientBuilder UseStreamingInterceptor(
        this ChatClientBuilder builder,
        IAgentOutput output)
    {
        return builder.Use(inner => new StreamingInterceptor(inner, output));
    }
}
