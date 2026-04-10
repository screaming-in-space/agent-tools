using System.Text;

namespace Agent.SDK.Console;

/// <summary>
/// Per-scanner data collected during a run, used to produce REASONING.md.
/// </summary>
public sealed class ScannerTrace
{
    public required string Name { get; init; }
    public required string ModelName { get; init; }
    public List<(string Tool, string? Detail, double Seconds, bool Success)> Tools { get; } = [];
    public List<(string Prompt, double TokPerSec, double AccuracyScore)> Metrics { get; } = [];
    public StringBuilder Thinking { get; } = new();
    public StringBuilder Response { get; } = new();
    public TimeSpan? Elapsed { get; set; }
    public bool? Success { get; set; }
}

/// <summary>
/// Writes REASONING.md to the output directory. Consumes <see cref="ScannerTrace"/>
/// data collected by either output implementation.
/// </summary>
public static class AgentReasoningLog
{
    public static async Task WriteAsync(
        string outputDirectory,
        string agentName,
        AgentRunSummary summary,
        IReadOnlyList<ScannerTrace> scanners)
    {
        var reasoningPath = Path.Combine(outputDirectory, "REASONING.md");

        try
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await using var writer = new StreamWriter(reasoningPath, append: false, Encoding.UTF8);
            await writer.WriteLineAsync("# Reasoning Trace").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync($"> Agent: {agentName}").ConfigureAwait(false);
            await writer.WriteLineAsync($"> Duration: {summary.Duration.TotalSeconds:F1}s").ConfigureAwait(false);
            await writer.WriteLineAsync($"> Tools: {summary.ToolCallCount} calls").ConfigureAwait(false);
            await writer.WriteLineAsync($"> Status: {(summary.Success ? "Success" : "Failed")}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);

            foreach (var scanner in scanners)
            {
                var status = scanner.Success switch
                {
                    true => "Success",
                    false => "Failed",
                    null => "Pending",
                };
                var elapsed = scanner.Elapsed.HasValue ? $"{scanner.Elapsed.Value.TotalSeconds:F1}s" : "-";

                await writer.WriteLineAsync($"## {scanner.Name}").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync($"- **Model:** {scanner.ModelName}").ConfigureAwait(false);
                await writer.WriteLineAsync($"- **Duration:** {elapsed}").ConfigureAwait(false);
                await writer.WriteLineAsync($"- **Status:** {status}").ConfigureAwait(false);
                await writer.WriteLineAsync($"- **Tool calls:** {scanner.Tools.Count}").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);

                if (scanner.Tools.Count > 0)
                {
                    foreach (var (tool, detail, secs, toolOk) in scanner.Tools)
                    {
                        var entry = detail is not null ? $"{tool} ({detail})" : tool;
                        var marker = toolOk ? "" : " ✗ FAILED";
                        await writer.WriteLineAsync($"  - {entry} ({secs:F1}s){marker}").ConfigureAwait(false);
                    }

                    await writer.WriteLineAsync().ConfigureAwait(false);
                }

                var thinking = scanner.Thinking.ToString().Trim();
                if (thinking.Length > 0)
                {
                    await writer.WriteLineAsync("### Thinking").ConfigureAwait(false);
                    await writer.WriteLineAsync().ConfigureAwait(false);
                    await writer.WriteLineAsync(thinking).ConfigureAwait(false);
                    await writer.WriteLineAsync().ConfigureAwait(false);
                }

                var response = scanner.Response.ToString().Trim();
                if (response.Length > 0)
                {
                    await writer.WriteLineAsync("### Response").ConfigureAwait(false);
                    await writer.WriteLineAsync().ConfigureAwait(false);
                    await writer.WriteLineAsync(response).ConfigureAwait(false);
                    await writer.WriteLineAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            await AgentErrorLog.LogAsync("ReasoningLog", $"Failed to write {reasoningPath}", ex).ConfigureAwait(false);
        }
    }
}
