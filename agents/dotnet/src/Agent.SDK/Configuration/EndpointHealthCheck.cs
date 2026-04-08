using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Agent.SDK.Configuration;

/// <summary>
/// Validates that an OpenAI-compatible endpoint is reachable and has the
/// expected model loaded. Calls <c>GET /v1/models</c> and checks the response.
/// </summary>
public static class EndpointHealthCheck
{
    /// <summary>
    /// Probes the endpoint's <c>/models</c> route and verifies the configured model is loaded.
    /// </summary>
    /// <param name="options">Model options containing endpoint, API key, and expected model.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/>; a temporary one is created when <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="HealthCheckResult"/> with status and loaded model list.</returns>
    public static async Task<HealthCheckResult> ValidateAsync(
        AgentModelOptions options,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var disposeClient = httpClient is null;
        httpClient ??= new HttpClient();

        try
        {
            // Normalize endpoint: strip trailing /v1 if present, then append /v1/models.
            var baseUri = options.Endpoint.TrimEnd('/');
            if (baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                baseUri = baseUri[..^3];
            }

            var modelsUrl = $"{baseUri}/v1/models";

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            if (!string.Equals(options.ApiKey, "no-key", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new("Bearer", options.ApiKey);
            }

            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new HealthCheckResult(
                    IsHealthy: false,
                    IsModelLoaded: false,
                    LoadedModels: [],
                    Error: $"Endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadFromJsonAsync<ModelsResponse>(ct);
            var loadedModels = body?.Data?.Select(m => m.Id).Where(id => id is not null).ToList()
                ?? [];

            // When model is empty, any loaded model is acceptable (server-default mode).
            var modelConfigured = !string.IsNullOrEmpty(options.Model);
            var isModelLoaded = !modelConfigured
                || loadedModels.Exists(id => string.Equals(id, options.Model, StringComparison.OrdinalIgnoreCase));

            return new HealthCheckResult(
                IsHealthy: true,
                IsModelLoaded: isModelLoaded,
                LoadedModels: loadedModels!,
                Error: isModelLoaded
                    ? null
                    : $"Model '{options.Model}' not found. Loaded: {string.Join(", ", loadedModels)}");
        }
        catch (HttpRequestException ex)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                IsModelLoaded: false,
                LoadedModels: [],
                Error: $"Cannot reach endpoint '{options.Endpoint}': {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                IsModelLoaded: false,
                LoadedModels: [],
                Error: $"Health check timed out connecting to '{options.Endpoint}'");
        }
        finally
        {
            if (disposeClient)
            {
                httpClient.Dispose();
            }
        }
    }

    /// <summary>Result of an endpoint health check.</summary>
    /// <param name="IsHealthy">True when the endpoint responded with 2xx.</param>
    /// <param name="IsModelLoaded">True when the configured model appears in the loaded models list.</param>
    /// <param name="LoadedModels">Model IDs returned by <c>GET /v1/models</c>.</param>
    /// <param name="Error">Human-readable error message, or <c>null</c> on success.</param>
    public sealed record HealthCheckResult(
        bool IsHealthy,
        bool IsModelLoaded,
        List<string> LoadedModels,
        string? Error);

    /// <summary>OpenAI-compatible <c>/v1/models</c> response shape.</summary>
    private sealed record ModelsResponse(
        [property: JsonPropertyName("data")] List<ModelEntry>? Data);

    /// <summary>Single model entry in the models list.</summary>
    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string? Id);
}
