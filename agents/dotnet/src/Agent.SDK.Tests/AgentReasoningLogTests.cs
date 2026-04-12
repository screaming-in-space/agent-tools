using Agent.SDK.Console;

namespace Agent.SDK.Tests;

public sealed class AgentReasoningLogTests : IDisposable
{
    private readonly string _dir;

    public AgentReasoningLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"reasoning-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ProducesReasoningFile()
    {
        var summary = new AgentRunSummary(
            ToolCallCount: 5, FilesProcessed: 5, Duration: TimeSpan.FromSeconds(10),
            OutputPath: "context", FullOutputPath: _dir, Success: true);

        var scanners = new List<ScannerTrace>
        {
            new() { Name = "Test Scanner", ModelName = "test-model" },
        };
        scanners[0].Elapsed = TimeSpan.FromSeconds(3);
        scanners[0].Success = true;

        await AgentReasoningLog.WriteAsync(_dir, "TestAgent", summary, scanners);

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "REASONING.md"), TestContext.Current.CancellationToken);
        Assert.Contains("# Reasoning Trace", content);
        Assert.Contains("> Agent: TestAgent", content);
        Assert.Contains("## Test Scanner", content);
        Assert.Contains("- **Model:** test-model", content);
        Assert.Contains("- **Status:** Success", content);
    }

    [Fact]
    public async Task WriteAsync_IncludesToolCalls()
    {
        var summary = new AgentRunSummary(
            ToolCallCount: 2, FilesProcessed: 2, Duration: TimeSpan.FromSeconds(5),
            OutputPath: "context", FullOutputPath: _dir, Success: true);

        var scanner = new ScannerTrace { Name = "Scanner", ModelName = "model" };
        scanner.Tools.Add(("Reading file", "src/Program.cs", 0.1, true));
        scanner.Tools.Add(("Writing output", "context/MAP.md", 0.0, true));
        scanner.Elapsed = TimeSpan.FromSeconds(2);
        scanner.Success = true;

        await AgentReasoningLog.WriteAsync(_dir, "Agent", summary, [scanner]);

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "REASONING.md"), TestContext.Current.CancellationToken);
        Assert.Contains("Reading file (src/Program.cs)", content);
        Assert.Contains("Writing output (context/MAP.md)", content);
    }

    [Fact]
    public async Task WriteAsync_IncludesFailedToolMarker()
    {
        var summary = new AgentRunSummary(
            ToolCallCount: 1, FilesProcessed: 1, Duration: TimeSpan.FromSeconds(1),
            OutputPath: "context", FullOutputPath: _dir, Success: false);

        var scanner = new ScannerTrace { Name = "Scanner", ModelName = "model" };
        scanner.Tools.Add(("Reading file", "missing.cs", 0.0, false));
        scanner.Elapsed = TimeSpan.FromSeconds(1);
        scanner.Success = false;

        await AgentReasoningLog.WriteAsync(_dir, "Agent", summary, [scanner]);

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "REASONING.md"), TestContext.Current.CancellationToken);
        Assert.Contains("✗ FAILED", content);
        Assert.Contains("- **Status:** Failed", content);
    }

    [Fact]
    public async Task WriteAsync_IncludesThinkingAndResponse()
    {
        var summary = new AgentRunSummary(
            ToolCallCount: 0, FilesProcessed: 0, Duration: TimeSpan.FromSeconds(1),
            OutputPath: "context", FullOutputPath: _dir, Success: true);

        var scanner = new ScannerTrace { Name = "Scanner", ModelName = "model" };
        scanner.Thinking.Append("I need to analyze the files first.");
        scanner.Response.Append("# Context Map\n\nHere is the output.");
        scanner.Elapsed = TimeSpan.FromSeconds(1);
        scanner.Success = true;

        await AgentReasoningLog.WriteAsync(_dir, "Agent", summary, [scanner]);

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "REASONING.md"), TestContext.Current.CancellationToken);
        Assert.Contains("### Thinking", content);
        Assert.Contains("I need to analyze the files first.", content);
        Assert.Contains("### Response", content);
        Assert.Contains("# Context Map", content);
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_dir, "new", "output");
        var summary = new AgentRunSummary(
            ToolCallCount: 0, FilesProcessed: 0, Duration: TimeSpan.FromSeconds(0),
            OutputPath: "context", FullOutputPath: nested, Success: true);

        await AgentReasoningLog.WriteAsync(nested, "Agent", summary, []);

        Assert.True(File.Exists(Path.Combine(nested, "REASONING.md")));
    }
}
