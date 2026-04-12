using System.Threading.Channels;

namespace Agent.SDK.Console;

/// <summary>
/// <see cref="IAgentOutput"/> implementation that writes <see cref="UIMessage"/> records
/// to a bounded channel. The <see cref="ChannelAgentRenderer"/> consumes from the reader side.
/// Never blocks producers — uses <see cref="ChannelWriter{T}.TryWrite"/> with drop-oldest policy.
/// </summary>
public sealed class ChannelAgentOutput : IAgentOutput
{
    private readonly Channel<UIMessage> _channel;
    private readonly ChannelWriter<UIMessage> _writer;
    private readonly ChannelAgentRenderer _renderer;
    private readonly List<ScannerTrace> _scannerTraces = [];
    private string? _activeScanner;
    private int _toolCallCount;
    private string _agentName = "Agent";
    private Task? _renderTask;
    private CancellationTokenSource? _renderCts;

    public ChannelAgentOutput()
    {
        _channel = Channel.CreateBounded<UIMessage>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _writer = _channel.Writer;
        _renderer = new ChannelAgentRenderer(_channel.Reader);
    }

    public bool IsInteractive => true;
    public int ToolCallCount => _toolCallCount;

    public string AgentName => _agentName;

    public Task StartAsync(string agentName, CancellationToken ct = default)
    {
        _agentName = agentName;
        _renderCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _renderer.AgentName = agentName;
        _renderTask = Task.Run(() => _renderer.RunAsync(_renderCts.Token), _renderCts.Token);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string status)
    {
        _writer.TryWrite(new StatusMessage(status, DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task ScannerStartedAsync(string scannerName, string modelName)
    {
        _activeScanner = scannerName;
        var trace = new ScannerTrace { Name = scannerName, ModelName = modelName };
        _scannerTraces.Add(trace);

        _writer.TryWrite(new ModelPhaseStartedMessage(
            scannerName, modelName, null, null, null, DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success)
    {
        var trace = _scannerTraces.Find(t => t.Name == scannerName);
        if (trace is not null)
        {
            trace.Elapsed = elapsed;
            trace.Success = success;
        }

        _writer.TryWrite(new ModelPhaseCompletedMessage(scannerName, elapsed, success, DateTimeOffset.Now));
        _activeScanner = null;
        return Task.CompletedTask;
    }

    public Task ScannerSkippedAsync(string scannerName, string reason)
    {
        var trace = new ScannerTrace { Name = scannerName, ModelName = "skipped" };
        trace.Success = null;
        _scannerTraces.Add(trace);
        return Task.CompletedTask;
    }

    public Task ToolStartedAsync(string toolName, string? detail = null)
    {
        Interlocked.Increment(ref _toolCallCount);
        return Task.CompletedTask;
    }

    public Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true)
    {
        var trace = _scannerTraces.Find(t => t.Name == _activeScanner);
        trace?.Tools.Add((toolName, detail, elapsed.TotalSeconds, success));
        return Task.CompletedTask;
    }

    public Task AppendThinkingAsync(string token)
    {
        var trace = _scannerTraces.Find(t => t.Name == _activeScanner);
        trace?.Thinking.Append(token);

        _writer.TryWrite(new ThinkingTokenMessage(token, _activeScanner ?? "unknown", DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task WriteResponseAsync(string text)
    {
        var trace = _scannerTraces.Find(t => t.Name == _activeScanner);
        trace?.Response.Append(text);

        _writer.TryWrite(new ResponseTokenMessage(text, _activeScanner ?? "unknown", DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task ReportPromptResultAsync(string promptName, double tokensPerSecond, double accuracyScore)
    {
        var trace = _scannerTraces.Find(t => t.Name == _activeScanner);
        trace?.Metrics.Add((promptName, tokensPerSecond, accuracyScore));
        return Task.CompletedTask;
    }

    public Task ReportTestStartedAsync(string promptName, string category, string description, string modelId)
    {
        _writer.TryWrite(new TestStartedMessage(
            promptName, category, description, 0, modelId, DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task ReportTestCompletedAsync(string promptName, double tokensPerSecond, TimeSpan ttft,
        double accuracyScore, bool passed, IReadOnlyList<TestCheckResult> checks)
    {
        _writer.TryWrite(new TestCompletedMessage(
            promptName, tokensPerSecond, ttft, accuracyScore, passed, checks, DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public Task ReportErrorAsync(string source, string message)
    {
        _writer.TryWrite(new ErrorMessage(source, message, DateTimeOffset.Now));
        return Task.CompletedTask;
    }

    public async Task StopAsync(AgentRunSummary summary, CancellationToken ct = default)
    {
        _writer.Complete();

        if (_renderTask is not null)
        {
            try
            {
                await _renderTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }

        ChannelAgentRenderer.RenderSummary(summary);

        // Write REASONING.md (requires output directory from summary)
        if (!string.IsNullOrEmpty(summary.FullOutputPath))
        {
            var outputDir = Directory.Exists(summary.FullOutputPath)
                ? summary.FullOutputPath
                : Path.GetDirectoryName(summary.FullOutputPath) ?? ".";
            await AgentReasoningLog.WriteAsync(outputDir, AgentName, summary, _scannerTraces);
        }
    }

    public void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }
}
