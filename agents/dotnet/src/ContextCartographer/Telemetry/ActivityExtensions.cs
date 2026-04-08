using System.Diagnostics;

namespace ContextCartographer.Telemetry;

/// <summary>
/// Fluent extensions on <see cref="Activity"/>? for ergonomic span instrumentation.
/// All methods are null-safe — they no-op when the span is <c>null</c> (no listener attached).
/// </summary>
public static class ActivityExtensions
{
    public static Activity? WithTag(this Activity? activity, string key, object? value)
    {
        activity?.SetTag(key, value);
        return activity;
    }

    public static Activity? RecordError(this Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);

        return activity;
    }

    public static Activity? SetSuccess(this Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
        return activity;
    }
}
