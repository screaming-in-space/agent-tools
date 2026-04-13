using System.Diagnostics.Metrics;
using Agent.SDK.Telemetry;

namespace Sterling.Telemetry;

public static class SterlingTrace
{
    public static readonly AgentTrace Instance = new("Sterling");
}

public static class SterlingMetrics
{
    private static readonly Meter Meter = new("Sterling");

    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("sterling.run.duration", "s", "Total agent run duration");
}
