using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.SDK.Configuration;

/// <summary>
/// Compact model + GPU registry loaded from <c>context/models/_registry.json</c> and
/// <c>context/gpu/_registry.json</c>. The planner uses this instead of full Markdown
/// profiles to stay within a ~1,500-token budget for model/GPU context.
/// </summary>
public sealed record ModelRegistry
{
    /// <summary>All registered models keyed by slug (e.g. <c>google-gemma-4-31b-it</c>).</summary>
    public IReadOnlyDictionary<string, ModelEntry> Models { get; init; } =
        new Dictionary<string, ModelEntry>();

    /// <summary>All registered GPUs keyed by slug (e.g. <c>rtx-5090-msi-suprim</c>).</summary>
    public IReadOnlyDictionary<string, GpuEntry> Gpus { get; init; } =
        new Dictionary<string, GpuEntry>();

    /// <summary>
    /// Loads both registries from JSON files under <paramref name="repoRoot"/>.
    /// Returns an empty registry (no models, no GPUs) when files are missing or
    /// malformed — the planner falls back to config-key-only mode.
    /// </summary>
    /// <param name="repoRoot">
    /// Repository root directory containing <c>context/models/_registry.json</c>
    /// and <c>context/gpu/_registry.json</c>.
    /// </param>
    public static ModelRegistry Load(string repoRoot)
    {
        var modelsPath = Path.Combine(repoRoot, "context", "models", "_registry.json");
        var gpuPath = Path.Combine(repoRoot, "context", "gpu", "_registry.json");

        var models = LoadFile<ModelRegistryFile>(modelsPath)?.Models
            ?? new Dictionary<string, ModelEntry>();

        var gpus = LoadFile<GpuRegistryFile>(gpuPath)?.Gpus
            ?? new Dictionary<string, GpuEntry>();

        return new ModelRegistry { Models = models, Gpus = gpus };
    }

    private static T? LoadFile<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

// ─── Model registry JSON shape ───────────────────────────────────────

/// <summary>Top-level shape of <c>context/models/_registry.json</c>.</summary>
internal sealed record ModelRegistryFile
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public Dictionary<string, ModelEntry> Models { get; init; } = [];
}

/// <summary>Compact model descriptor consumed by the planner.</summary>
public sealed record ModelEntry
{
    public double ParamsB { get; init; }
    public string Architecture { get; init; } = "";
    public double ActiveParamsB { get; init; }
    public int ContextK { get; init; }
    public int VramQ4Gb { get; init; }
    public int VramQ8Gb { get; init; }
    public int VramBf16Gb { get; init; }
    public string ToolCalling { get; init; } = "";
    public bool Thinking { get; init; }
    public InferenceSettings? Inference { get; init; }
    public ScannerRatings? Scanners { get; init; }
    public string? Notes { get; init; }
    public string? Profile { get; init; }
}

/// <summary>Recommended inference parameters from the model vendor.</summary>
public sealed record InferenceSettings
{
    public double Temperature { get; init; }
    public double TopP { get; init; }
    public int? TopK { get; init; }
}

/// <summary>Per-scanner suitability ratings.</summary>
public sealed record ScannerRatings
{
    public ScannerRating Markdown { get; init; }
    public ScannerRating Structure { get; init; }
    public ScannerRating Rules { get; init; }
    public ScannerRating Quality { get; init; }
    public ScannerRating Journal { get; init; }
    public ScannerRating Done { get; init; }
}

/// <summary>How well a model handles a given scanner.</summary>
public enum ScannerRating
{
    Marginal,
    Good,
    Excellent,
}

// ─── GPU registry JSON shape ─────────────────────────────────────────

/// <summary>Top-level shape of <c>context/gpu/_registry.json</c>.</summary>
internal sealed record GpuRegistryFile
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public Dictionary<string, GpuEntry> Gpus { get; init; } = [];
}

/// <summary>Compact GPU descriptor consumed by the planner.</summary>
public sealed record GpuEntry
{
    public int VramGb { get; init; }
    public int BandwidthGbS { get; init; }
    public string ComputeCapability { get; init; } = "";
    public int CudaCores { get; init; }

    /// <summary>
    /// Per-model fit map. Key = model slug. Value maps quantization level
    /// (<c>q4</c>, <c>q8</c>) to fit status: <c>true</c>, <c>false</c>,
    /// or <c>"tight"</c> (fits but may need reduced context).
    /// </summary>
    public Dictionary<string, Dictionary<string, JsonElement>> Fits { get; init; } = [];

    public string? Profile { get; init; }

    /// <summary>
    /// Checks whether <paramref name="modelSlug"/> fits at the given
    /// <paramref name="quantization"/> level on this GPU.
    /// Returns <c>true</c> for fit / tight, <c>false</c> for no-fit or unknown.
    /// </summary>
    public bool CanFit(string modelSlug, string quantization = "q4")
    {
        if (!Fits.TryGetValue(modelSlug, out var quantMap)) return false;
        if (!quantMap.TryGetValue(quantization, out var value)) return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => true, // "tight" still fits
            _ => false,
        };
    }
}
