using Agent.SDK.Console;
using Microsoft.Extensions.AI;

namespace Agent.SDK.Tests;

public sealed class StreamingInterceptorTests
{
    // ── Constructor ──

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        var inner = new TestChatClient();
        var output = new StubAgentOutput();

        var interceptor = new StreamingInterceptor(inner, output);

        Assert.NotNull(interceptor);
    }

    [Fact]
    public void Constructor_SetsInnerClient_DelegatesMetadata()
    {
        var inner = new TestChatClient();
        var output = new StubAgentOutput();

        var interceptor = new StreamingInterceptor(inner, output);

        // DelegatingChatClient exposes the inner client's metadata
        Assert.NotNull(interceptor.GetService<IChatClient>());
    }

    // ── Extension method ──

    [Fact]
    public void UseStreamingInterceptor_ReturnsBuilder()
    {
        var inner = new TestChatClient();
        var builder = new ChatClientBuilder(inner);
        var output = new StubAgentOutput();

        var result = builder.UseStreamingInterceptor(output);

        Assert.NotNull(result);
    }

    // ── Helpers ──

    /// <summary>Minimal IChatClient that does nothing.</summary>
    private sealed class TestChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("test-client");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse([]));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return serviceType == typeof(IChatClient) ? this : null;
        }

        public void Dispose() { }
    }

    /// <summary>Stub IAgentOutput that records nothing.</summary>
    private sealed class StubAgentOutput : IAgentOutput
    {
        public bool IsInteractive => false;
        public int ToolCallCount => 0;

        public Task StartAsync(string agentName, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateStatusAsync(string status) => Task.CompletedTask;
        public Task ScannerStartedAsync(string scannerName, string modelName) => Task.CompletedTask;
        public Task ScannerCompletedAsync(string scannerName, TimeSpan elapsed, bool success) => Task.CompletedTask;
        public Task ScannerSkippedAsync(string scannerName, string reason) => Task.CompletedTask;
        public Task ToolStartedAsync(string toolName, string? detail = null) => Task.CompletedTask;
        public Task ToolCompletedAsync(string toolName, TimeSpan elapsed, string? detail = null, bool success = true) => Task.CompletedTask;
        public Task AppendThinkingAsync(string token) => Task.CompletedTask;
        public Task StopAsync(AgentRunSummary summary, CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteResponseAsync(string text) => Task.CompletedTask;
        public void Dispose() { }
    }
}
