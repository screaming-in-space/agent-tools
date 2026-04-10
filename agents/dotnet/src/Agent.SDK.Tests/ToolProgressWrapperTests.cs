using Agent.SDK.Console;
using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

[Collection("FileTools")]
public sealed class ToolProgressWrapperTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousRoot;

    public ToolProgressWrapperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"wrapper-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _previousRoot = FileTools.RootDirectory;
        FileTools.RootDirectory = _root;
    }

    public void Dispose()
    {
        FileTools.RootDirectory = _previousRoot;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── Truncate (via AgentFileLog.Truncate) ──

    [Theory]
    [InlineData(null, 10, null)]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]
    [InlineData("this is too long", 5, "this ...")]
    public void Truncate_BehavesCorrectly(string? input, int max, string? expected)
    {
        Assert.Equal(expected, AgentFileLog.Truncate(input, max));
    }
}
