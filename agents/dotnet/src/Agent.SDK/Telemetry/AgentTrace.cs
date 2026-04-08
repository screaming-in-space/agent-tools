using System.Diagnostics;

namespace Agent.SDK.Telemetry;

/// <summary>
/// Factory for creating agent-scoped <see cref="ActivitySource"/> instances with a shared
/// <see cref="StartSpan"/> helper. Each agent creates one static instance with its own source name.
/// <para>
/// <c>private static readonly AgentTrace Trace = new("MyAgent");</c>
/// <c>using var span = Trace.StartSpan("operation");</c>
/// </para>
/// </summary>
public sealed class AgentTrace(string sourceName)
{
    public ActivitySource Source { get; } = new(sourceName);

    /// <summary>
    /// Starts a new span. Returns <c>null</c> when no trace listener is attached (zero overhead).
    /// The returned <see cref="Activity"/> is <see cref="IDisposable"/>; wrap with <c>using</c>
    /// to automatically end the span on scope exit.
    /// </summary>
    public Activity? StartSpan(
        string name,
        ActivityKind kind = ActivityKind.Internal,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return tags is null
            ? Source.StartActivity(name, kind)
            : Source.StartActivity(name, kind, default(ActivityContext), tags);
    }
}
