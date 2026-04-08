using System.Diagnostics.Metrics;
using Agent.SDK.Telemetry;

namespace CrimeSceneInvestigator.Telemetry;

/// <summary>
/// Tracing for Crime Scene Investigator agent runs.
/// </summary>
public static class CsiTrace
{
    public static readonly AgentTrace Instance = new("CrimeSceneInvestigator");
}

/// <summary>
/// Metrics for Crime Scene Investigator agent runs.
/// </summary>
public static class CsiMetrics
{
    public static readonly Meter Meter = new("CrimeSceneInvestigator");

    /// <summary>Counts files discovered during the scan phase.</summary>
    public static readonly Counter<long> FilesDiscovered =
        Meter.CreateCounter<long>(
            "csi.files.discovered",
            unit: "{file}",
            description: "Total markdown files discovered.");

    /// <summary>Counts files read by the agent during investigation.</summary>
    public static readonly Counter<long> FilesRead =
        Meter.CreateCounter<long>(
            "csi.files.read",
            unit: "{file}",
            description: "Total files read by the agent.");

    /// <summary>Counts tool invocations dispatched by the agent.</summary>
    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>(
            "csi.tool.invocations",
            unit: "{invocation}",
            description: "Total tool invocations dispatched.");

    /// <summary>Records agent run duration in seconds.</summary>
    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>(
            "csi.run.duration",
            unit: "s",
            description: "Agent run duration.");
}
