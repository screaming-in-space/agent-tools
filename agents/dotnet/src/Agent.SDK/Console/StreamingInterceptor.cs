using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Agent.SDK.Console;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that intercepts streaming content from
/// every LLM call — including intermediate calls between tool invocations.
/// Captures both regular text and reasoning/thinking content.
/// Writes a full debug trace to <c>{BaseDirectory}/streaming-debug.log</c>.
/// </summary>
public sealed class StreamingInterceptor : DelegatingChatClient
{
    private readonly IAgentOutput _output;
    private readonly string _debugLogPath;
    private int _callCount;

    public StreamingInterceptor(IChatClient inner, IAgentOutput output) : base(inner)
    {
        _output = output;
        _debugLogPath = Path.Combine(AppContext.BaseDirectory, "streaming-debug.log");

        // Start fresh each run
        try { File.WriteAllText(_debugLogPath, $"=== Streaming Debug Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n"); }
        catch { /* best effort */ }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _callCount);
        DebugLog($"\n── LLM Call #{call} ──────────────────────────────────────\n");

        await foreach (var update in base.GetStreamingResponseAsync(
            messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                        DebugLog($"[THINKING] {reasoning.Text}");
                        _output.AppendThinking(reasoning.Text);
                        break;

                    case TextContent text when text.Text is { Length: > 0 }:
                        DebugLog($"[RESPONSE] {text.Text}");
                        _output.AppendThinking(text.Text);
                        break;

                    case FunctionCallContent fcc:
                        var args = fcc.Arguments is not null
                            ? string.Join(", ", fcc.Arguments.Select(a => $"{a.Key}={Truncate(a.Value?.ToString(), 80)}"))
                            : "";
                        DebugLog($"\n[TOOL_CALL] {fcc.Name}({args})\n");
                        break;

                    case FunctionResultContent frc:
                        DebugLog($"\n[TOOL_RESULT] {frc.CallId} = {Truncate(frc.Result?.ToString(), 200)}\n");
                        break;

                    default:
                        DebugLog($"[{content.GetType().Name}]");
                        break;
                }
            }

            yield return update;
        }

        DebugLog($"\n── End Call #{call} ─────────────────────────────────────\n\n");
    }

    private void DebugLog(string text)
    {
        try { File.AppendAllText(_debugLogPath, text); }
        catch { /* best effort — don't break the agent if log write fails */ }
    }

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max] + "...";
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
