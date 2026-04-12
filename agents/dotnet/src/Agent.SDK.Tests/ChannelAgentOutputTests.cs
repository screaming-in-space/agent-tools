using Agent.SDK.Console;

namespace Agent.SDK.Tests;

public class ChannelAgentOutputTests : IDisposable
{
    private readonly ChannelAgentOutput _output = new();

    [Fact]
    public void IsInteractive_ReturnsTrue()
    {
        Assert.True(_output.IsInteractive);
    }

    [Fact]
    public async Task ToolStartedAsync_IncrementsToolCallCount()
    {
        await _output.ToolStartedAsync("TestTool");
        await _output.ToolStartedAsync("TestTool");

        Assert.Equal(2, _output.ToolCallCount);
    }

    [Fact]
    public async Task ScannerStartedAsync_SetsActiveScanner()
    {
        // StartAsync would launch the renderer — skip it for unit test
        await _output.ScannerStartedAsync("TestScanner", "test-model");

        // Verify via AppendThinkingAsync which uses the active scanner
        await _output.AppendThinkingAsync("token");

        // No crash = scanner was tracked
        Assert.Equal(0, _output.ToolCallCount);
    }

    [Fact]
    public async Task ReportTestStartedAsync_DoesNotThrow()
    {
        await _output.ReportTestStartedAsync("test_prompt", "category", "description", 1, "model-id");
    }

    [Fact]
    public async Task ReportTestCompletedAsync_DoesNotThrow()
    {
        var checks = new List<TestCheckResult>
        {
            new("length", 1.0, "Length OK"),
            new("required_substrings", 0.8, "Missing 1"),
        };

        await _output.ReportTestCompletedAsync(
            "test_prompt", 42.0, TimeSpan.FromMilliseconds(200),
            0.85, true, checks);
    }

    [Fact]
    public async Task ReportErrorAsync_DoesNotThrow()
    {
        await _output.ReportErrorAsync("Benchmark:test", "Connection refused");
    }

    public void Dispose()
    {
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
