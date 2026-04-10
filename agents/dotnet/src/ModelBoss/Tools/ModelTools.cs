using System.ComponentModel;
using System.Text;
using Agent.SDK.Configuration;
using Microsoft.Extensions.Configuration;

namespace ModelBoss.Tools;

/// <summary>
/// Agent tools for model discovery: registry data, endpoint queries, config resolution.
/// </summary>
public sealed class ModelTools(ModelRegistry registry, IConfiguration configuration)
{
    [Description("Lists all models from the registry with parameters, architecture, VRAM requirements, and scanner ratings.")]
    public string ListRegisteredModels()
    {
        if (registry.Models.Count == 0)
        {
            return "No models registered. Check context/models/_registry.json.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("| Model | Params | Active | Arch | Context | VRAM Q4 | Tool Calling | Thinking |");
        sb.AppendLine("|-------|--------|--------|------|---------|---------|--------------|----------|");

        foreach (var (slug, model) in registry.Models)
        {
            sb.AppendLine(
                $"| {slug} | {model.ParamsB}B | {model.ActiveParamsB}B | {model.Architecture} " +
                $"| {model.ContextK}K | {model.VramQ4Gb}GB | {model.ToolCalling} | {model.Thinking} |");
        }

        return sb.ToString();
    }

    [Description("Lists all model configurations from appsettings.json with their endpoint, model name, and parameters.")]
    public string ListConfiguredModels()
    {
        var configs = AgentModelOptions.ResolveAll(configuration);

        if (configs.Count == 0)
        {
            return "No model configurations found in appsettings.json.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("| Config Key | Model | Endpoint | Temperature | MaxTokens |");
        sb.AppendLine("|------------|-------|----------|-------------|-----------|");

        foreach (var (key, options) in configs)
        {
            sb.AppendLine(
                $"| {key} | {options.Model} | {options.Endpoint} " +
                $"| {options.Temperature?.ToString("F1") ?? "default"} | {options.MaxOutputTokens?.ToString() ?? "default"} |");
        }

        return sb.ToString();
    }

    [Description("Queries the endpoint for currently loaded models. Returns the model IDs reported by GET /v1/models.")]
    public async Task<string> GetLoadedModelsAsync(string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "Error: endpoint is required.";
        }

        var options = new AgentModelOptions { Endpoint = endpoint };
        var health = await EndpointHealthCheck.ValidateAsync(options, ct: ct);

        if (!health.IsHealthy)
        {
            return $"Endpoint unhealthy: {health.Error}";
        }

        if (health.LoadedModels.Count == 0)
        {
            return "Endpoint is healthy but no models are loaded.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Endpoint: {endpoint}");
        sb.AppendLine($"Loaded models ({health.LoadedModels.Count}):");

        foreach (var model in health.LoadedModels)
        {
            // Cross-reference with registry
            var registryMatch = registry.Models
                .FirstOrDefault(m => model.Contains(m.Key, StringComparison.OrdinalIgnoreCase));

            if (registryMatch.Value is not null)
            {
                sb.AppendLine($"  - {model} (registry: {registryMatch.Key}, {registryMatch.Value.ParamsB}B {registryMatch.Value.Architecture})");
            }
            else
            {
                sb.AppendLine($"  - {model} (not in registry)");
            }
        }

        return sb.ToString();
    }

    [Description("Gets detailed registry profile for a specific model including scanner ratings and inference settings.")]
    public string GetModelProfile(string modelSlug)
    {
        if (string.IsNullOrWhiteSpace(modelSlug))
        {
            return "Error: modelSlug is required.";
        }

        if (!registry.Models.TryGetValue(modelSlug, out var model))
        {
            var available = string.Join(", ", registry.Models.Keys);
            return $"Model '{modelSlug}' not found in registry. Available: {available}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## {modelSlug}");
        sb.AppendLine();
        sb.AppendLine($"- **Parameters:** {model.ParamsB}B total, {model.ActiveParamsB}B active");
        sb.AppendLine($"- **Architecture:** {model.Architecture}");
        sb.AppendLine($"- **Context:** {model.ContextK}K tokens");
        sb.AppendLine($"- **VRAM:** Q4={model.VramQ4Gb}GB, Q8={model.VramQ8Gb}GB, BF16={model.VramBf16Gb}GB");
        sb.AppendLine($"- **Tool Calling:** {model.ToolCalling}");
        sb.AppendLine($"- **Thinking:** {model.Thinking}");

        if (model.Inference is not null)
        {
            sb.AppendLine($"- **Recommended:** temp={model.Inference.Temperature}, top_p={model.Inference.TopP}");
            if (model.Inference.TopK.HasValue)
            {
                sb.AppendLine($"  top_k={model.Inference.TopK}");
            }
        }

        if (model.Scanners is not null)
        {
            sb.AppendLine();
            sb.AppendLine("### Scanner Ratings");
            sb.AppendLine($"- Markdown: {model.Scanners.Markdown}");
            sb.AppendLine($"- Structure: {model.Scanners.Structure}");
            sb.AppendLine($"- Rules: {model.Scanners.Rules}");
            sb.AppendLine($"- Quality: {model.Scanners.Quality}");
            sb.AppendLine($"- Journal: {model.Scanners.Journal}");
            sb.AppendLine($"- Done: {model.Scanners.Done}");
        }

        if (model.Notes is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"**Notes:** {model.Notes}");
        }

        return sb.ToString();
    }
}
