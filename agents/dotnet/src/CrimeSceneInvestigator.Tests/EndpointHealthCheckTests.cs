using System.Net;
using System.Text;
using Agent.SDK.Configuration;

namespace CrimeSceneInvestigator.Tests;

public sealed class EndpointHealthCheckTests
{
    private static readonly AgentModelOptions DefaultOptions = new()
    {
        Endpoint = "http://localhost:1234/v1",
        ApiKey = "no-key",
        Model = "unsloth/nvidia-nemotron-3-nano-4b"
    };

    [Fact]
    public async Task WhenModelIsLoadedThenReturnsHealthyAndModelLoaded()
    {
        using var client = CreateClient("""
            {
              "data": [
                { "id": "unsloth/nvidia-nemotron-3-nano-4b", "object": "model" },
                { "id": "jina-embeddings-v5-text-nano-retrieval", "object": "model" }
              ],
              "object": "list"
            }
            """);

        var result = await EndpointHealthCheck.ValidateAsync(DefaultOptions, client);

        Assert.True(result.IsHealthy);
        Assert.True(result.IsModelLoaded);
        Assert.Null(result.Error);
        Assert.Contains("unsloth/nvidia-nemotron-3-nano-4b", result.LoadedModels);
        Assert.Contains("jina-embeddings-v5-text-nano-retrieval", result.LoadedModels);
    }

    [Fact]
    public async Task WhenModelNotLoadedThenReturnsHealthyButModelNotLoaded()
    {
        using var client = CreateClient("""
            {
              "data": [
                { "id": "some-other-model", "object": "model" }
              ],
              "object": "list"
            }
            """);

        var result = await EndpointHealthCheck.ValidateAsync(DefaultOptions, client);

        Assert.True(result.IsHealthy);
        Assert.False(result.IsModelLoaded);
        Assert.Contains("not found", result.Error);
        Assert.Contains("some-other-model", result.Error);
    }

    [Fact]
    public async Task WhenEndpointReturnsErrorThenReturnsUnhealthy()
    {
        using var client = CreateClient("{}", HttpStatusCode.ServiceUnavailable);

        var result = await EndpointHealthCheck.ValidateAsync(DefaultOptions, client);

        Assert.False(result.IsHealthy);
        Assert.False(result.IsModelLoaded);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task WhenEndpointUnreachableThenReturnsUnhealthy()
    {
        using var client = CreateClient(throwOnSend: true);

        var result = await EndpointHealthCheck.ValidateAsync(DefaultOptions, client);

        Assert.False(result.IsHealthy);
        Assert.False(result.IsModelLoaded);
        Assert.Contains("Cannot reach endpoint", result.Error);
    }

    [Fact]
    public async Task WhenModelIsEmptyThenAnyLoadedModelIsAcceptable()
    {
        var options = DefaultOptions with { Model = "" };
        using var client = CreateClient("""
            {
              "data": [
                { "id": "anything-goes", "object": "model" }
              ],
              "object": "list"
            }
            """);

        var result = await EndpointHealthCheck.ValidateAsync(options, client);

        Assert.True(result.IsHealthy);
        Assert.True(result.IsModelLoaded);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task WhenNoModelsLoadedThenModelNotLoaded()
    {
        using var client = CreateClient("""{ "data": [], "object": "list" }""");

        var result = await EndpointHealthCheck.ValidateAsync(DefaultOptions, client);

        Assert.True(result.IsHealthy);
        Assert.False(result.IsModelLoaded);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ModelMatchIsCaseInsensitive()
    {
        var options = DefaultOptions with { Model = "Unsloth/NVIDIA-Nemotron-3-Nano-4B" };
        using var client = CreateClient("""
            {
              "data": [
                { "id": "unsloth/nvidia-nemotron-3-nano-4b", "object": "model" }
              ],
              "object": "list"
            }
            """);

        var result = await EndpointHealthCheck.ValidateAsync(options, client);

        Assert.True(result.IsModelLoaded);
    }

    private static HttpClient CreateClient(
        string json = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        bool throwOnSend = false)
    {
        return new HttpClient(new FakeHandler(json, statusCode, throwOnSend));
    }

    private sealed class FakeHandler(string json, HttpStatusCode statusCode, bool throwOnSend)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (throwOnSend)
            {
                throw new HttpRequestException("Connection refused");
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
