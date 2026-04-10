using Serilog;

namespace Agent.SDK.Console;

/// <summary>Routes all output through Serilog. No Spectre rendering.</summary>
public sealed class PlainAgentOutput : IAgentOutput
{
    private readonly List<ScannerTrace> _scanners = [];
    private readonly Dictionary<string, ScannerTrace> _scannerWork = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeScanner;
    private string _agentName = "Agent";

    public bool IsInteractive => false;
    public int ToolCallCount { get; private set; }

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        _agentName = agentName;
        Log.Information("Starting {AgentName}...", agentName);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string status)
    {
        Log.Information("{Status}", status);
        return Task.CompletedTask;
    }

    public Task ScannerSkippedAsync(string scannerName, string reason)
    {
        var trace = new ScannerTrace { Name = scannerName, ModelName = "—" };
        trace.Elapsed = TimeSpan.Zero;
        trace.Success = null;
        trace.Response.Append(reason);
        _scanners.Add(trace);
        Log.Warning("Skipped: {ScannerName} — {Reason}", scannerName, reason);
        return Task.CompletedTask;
    }

    public Task ScannerStartedAsync(string scannerName, string modelName)
    {
        _activeScanner = scannerName;
        if (!_scannerWork.ContainsKey(scannerName))
        {
            var trace = new ScannerTrace { Name = scannerName, ModelName = modelName };
            _scannerWork[scannerName] = trace;
            _scanners.Add(trace);
        }

        Log.Information("Running: {ScannerName} [{Model}]", scannerName, modelName);
        return Task.CompletedTask;
    }

    public Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success)
    {
        if (_scannerWork.TryGetValue(scannerName, out var work))
        {
            work.Elapsed = elapsed;
            work.Success = success;
        }

        if (_activeScanner == scannerName)
        {
            _activeScanner = null;
        }

        if (success)
        {
            Log.Information("Completed: {ScannerName} in {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
        }
        else
        {
            Log.Warning("Failed: {ScannerName} after {Elapsed:F1}s", scannerName, elapsed.TotalSeconds);
        }

        return Task.CompletedTask;
    }

    public Task ToolStartedAsync(string toolName, string? detail = null)
    {
        ToolCallCount++;
        Log.Information("  {ToolName} {Detail}", toolName, detail ?? "");
        return Task.CompletedTask;
    }

    public Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true)
    {
        if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
        {
            work.Tools.Add((toolName, detail, elapsed.TotalSeconds, success));
        }

        if (success)
        {
            Log.Debug("  {ToolName} completed in {Elapsed:F1}s", toolName, elapsed.TotalSeconds);
        }
        else
        {
            Log.Warning("  {ToolName} failed after {Elapsed:F1}s", toolName, elapsed.TotalSeconds);
        }

        return Task.CompletedTask;
    }

    public Task AppendThinkingAsync(string token) => Task.CompletedTask;

    public Task ReportPromptResultAsync(string promptName, double tokensPerSecond, double accuracyScore)
    {
        if (_activeScanner is not null && _scannerWork.TryGetValue(_activeScanner, out var work))
        {
            work.Metrics.Add((promptName, tokensPerSecond, accuracyScore));
        }

        Log.Information("  {Prompt}: {TokS:F1} tok/s, accuracy={Score:P0}", promptName, tokensPerSecond, accuracyScore);
        return Task.CompletedTask;
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        Log.Information("Completed in {Duration:F1}s - {ToolCalls} tool calls, output: {Output}",
            summary.Duration.TotalSeconds, summary.ToolCallCount, summary.OutputPath);
        await AgentReasoningLog.WriteAsync(summary.FullOutputPath, _agentName, summary, _scanners);
    }

    public Task WriteResponseAsync(string text) => Task.CompletedTask;

    public void Dispose() { }
}
