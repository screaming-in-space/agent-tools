using Agent.SDK.Console;

namespace Agent.SDK.Tests;

public sealed class AgentFileLogTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;

    public AgentFileLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"log-tests-{Guid.NewGuid():N}");
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    // ── AgentErrorLog ──

    [Fact]
    public async Task ErrorLog_Initialize_CreatesFile()
    {
        await AgentErrorLog.InitAsync(_dir);
        await AgentErrorLog.CloseAsync();

        var logPath = Path.Combine(_dir, "agent_error.log");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Agent Error Log", content);
    }

    [Fact]
    public async Task ErrorLog_LogAsync_WritesFormattedEntry()
    {
        await AgentErrorLog.InitAsync(_dir);
        await AgentErrorLog.LogAsync("TestScanner", "Something went wrong");
        await AgentErrorLog.CloseAsync();

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "agent_error.log"));
        Assert.Contains("[TestScanner] Something went wrong", content);
    }

    [Fact]
    public async Task ErrorLog_LogAsyncWithException_WritesExceptionDetails()
    {
        await AgentErrorLog.InitAsync(_dir);
        var ex = new InvalidOperationException("bad state");
        await AgentErrorLog.LogAsync("Scanner", "Tool failed", ex);
        await AgentErrorLog.CloseAsync();

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "agent_error.log"));
        Assert.Contains("[Scanner] Tool failed", content);
        Assert.Contains("InvalidOperationException: bad state", content);
    }

    [Fact]
    public async Task ErrorLog_Initialize_CreatesDirectory()
    {
        var nested = Path.Combine(_dir, "sub", "deep");
        await AgentErrorLog.InitAsync(nested);
        await AgentErrorLog.CloseAsync();

        Assert.True(Directory.Exists(nested));
        Assert.True(File.Exists(Path.Combine(nested, "agent_error.log")));
    }

    // ── AgentDebugLog ──

    [Fact]
    public async Task DebugLog_Initialize_CreatesFile()
    {
        await AgentDebugLog.InitAsync(_dir);
        await AgentDebugLog.WriteAsync("[TEST] hello");
        await AgentDebugLog.FlushAsync();
        await AgentDebugLog.CloseAsync();

        var logPath = Path.Combine(_dir, "agent_streaming_debug.log");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Streaming Debug Log", content);
        Assert.Contains("[TEST] hello", content);
    }

    [Fact]
    public async Task DebugLog_WriteDirect_BuffersUntilFlush()
    {
        await AgentDebugLog.InitAsync(_dir);
        await AgentDebugLog.WriteAsync("buffered");

        var logPath = Path.Combine(_dir, "agent_streaming_debug.log");
        // Content may not be on disk yet (AutoFlush = false)
        await AgentDebugLog.FlushAsync();
        await AgentDebugLog.CloseAsync();

        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("buffered", content);
    }

    // ── Truncate ──

    [Fact]
    public void Truncate_ShortString_ReturnsUnchanged()
    {
        Assert.Equal("hello", AgentFileLog.Truncate("hello", 10));
    }

    [Fact]
    public void Truncate_LongString_TruncatesWithEllipsis()
    {
        var result = AgentFileLog.Truncate("abcdefghij", 5);
        Assert.Equal("abcde...", result);
    }

    [Fact]
    public void Truncate_Null_ReturnsNull()
    {
        Assert.Null(AgentFileLog.Truncate(null, 10));
    }
}
