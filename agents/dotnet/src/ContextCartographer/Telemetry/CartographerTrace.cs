using System.Diagnostics;

namespace ContextCartographer.Telemetry;

/// <summary>
/// Shared <see cref="ActivitySource"/> for Context Cartographer agents.
/// Use <see cref="StartSpan"/> to create spans that integrate with OpenTelemetry.
/// <para>
/// <c>using var span = CartographerTrace.StartSpan("my-operation");</c>
/// </para>
/// </summary>
public static class CartographerTrace
{
    public static readonly ActivitySource Source = new("ContextCartographer");

    /// <summary>
    /// Starts a new span. Returns <c>null</c> when no trace listener is attached (zero overhead).
    /// The returned <see cref="Activity"/> is <see cref="IDisposable"/>; wrap with <c>using</c>
    /// to automatically end the span on scope exit.
    /// </summary>
    public static Activity? StartSpan(
        string name,
        ActivityKind kind = ActivityKind.Internal,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return tags is null
            ? Source.StartActivity(name, kind)
            : Source.StartActivity(name, kind, default(ActivityContext), tags);
    }
}
