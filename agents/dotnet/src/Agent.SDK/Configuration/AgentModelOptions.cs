using Microsoft.Extensions.Configuration;

namespace Agent.SDK.Configuration;

/// <summary>
/// Lightweight model configuration for a single agent. Bound from a named section
/// in <c>appsettings.json</c> under <c>Models:{key}</c>.
/// <para>
/// Resolution order: CLI <c>--config-key</c> selects the section name,
/// defaulting to <c>"default"</c> when omitted.
/// </para>
/// </summary>
public sealed record AgentModelOptions
{
    /// <summary>Configuration section prefix. Each key lives under <c>Models:{key}</c>.</summary>
    public const string SectionPrefix = "Models";

    /// <summary>The config key used when <c>--config-key</c> is not specified.</summary>
    public const string DefaultKey = "default";

    /// <summary>OpenAI-compatible endpoint URL.</summary>
    public string Endpoint { get; init; } = "http://localhost:1234/v1";

    /// <summary>API key. LM Studio and Ollama ignore this; OpenAI/Azure require a real value.</summary>
    public string ApiKey { get; init; } = "no-key";

    /// <summary>
    /// Model identifier as reported by <c>GET /v1/models</c>.
    /// Empty string means "use server default" (LM Studio uses the loaded model).
    /// </summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// Resolves model options from a named <c>Models:{configKey}</c> section in configuration.
    /// Properties not specified in config retain their defaults.
    /// </summary>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="configKey">
    /// Section key under <c>Models</c> (e.g. <c>"default"</c>, <c>"openai"</c>, <c>"ollama"</c>).
    /// Defaults to <see cref="DefaultKey"/> when <c>null</c> or empty.
    /// </param>
    /// <returns>A fully resolved <see cref="AgentModelOptions"/> instance.</returns>
    public static AgentModelOptions Resolve(IConfiguration configuration, string? configKey = null)
    {
        var key = string.IsNullOrWhiteSpace(configKey) ? DefaultKey : configKey;
        var section = configuration.GetSection($"{SectionPrefix}:{key}");

        var options = new AgentModelOptions();
        section.Bind(options);
        return options;
    }
}
