using System.Diagnostics.Metrics;

namespace ContextCartographer.Telemetry;

/// <summary>
/// Shared <see cref="Meter"/> for Context Cartographer agents.
/// Counters and histograms are created once and reused for the process lifetime.
/// </summary>
public static class CartographerMetrics
{
    public static readonly Meter Meter = new("ContextCartographer");

    /// <summary>Counts files discovered during the scan phase.</summary>
    public static readonly Counter<long> FilesDiscovered =
        Meter.CreateCounter<long>(
            "cartographer.files.discovered",
            unit: "{file}",
            description: "Total markdown files discovered.");

    /// <summary>Counts files read by the agent during mapping.</summary>
    public static readonly Counter<long> FilesRead =
        Meter.CreateCounter<long>(
            "cartographer.files.read",
            unit: "{file}",
            description: "Total files read by the agent.");

    /// <summary>Counts tool invocations dispatched by the agent.</summary>
    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>(
            "cartographer.tool.invocations",
            unit: "{invocation}",
            description: "Total tool invocations dispatched.");

    /// <summary>Records agent run duration in seconds.</summary>
    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>(
            "cartographer.run.duration",
            unit: "s",
            description: "Agent run duration.");
}
