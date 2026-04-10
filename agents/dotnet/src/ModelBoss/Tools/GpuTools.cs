using System.ComponentModel;
using System.Text;
using Agent.SDK.Configuration;

namespace ModelBoss.Tools;

/// <summary>
/// Agent tools for GPU discovery and analysis.
/// Reads the GPU registry and reports hardware capabilities.
/// </summary>
public sealed class GpuTools(ModelRegistry registry)
{
    [Description("Lists all registered GPUs with their VRAM, bandwidth, and CUDA core counts.")]
    public string ListGpus()
    {
        if (registry.Gpus.Count == 0)
        {
            return "No GPUs registered. Check context/gpu/_registry.json.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("| GPU | VRAM (GB) | Bandwidth (GB/s) | CUDA Cores | Compute |");
        sb.AppendLine("|-----|-----------|-------------------|------------|---------|");

        foreach (var (slug, gpu) in registry.Gpus)
        {
            sb.AppendLine($"| {slug} | {gpu.VramGb} | {gpu.BandwidthGbS} | {gpu.CudaCores} | {gpu.ComputeCapability} |");
        }

        return sb.ToString();
    }

    [Description("Checks which models fit on a specific GPU at different quantization levels. Returns a compatibility matrix.")]
    public string CheckModelFit(string gpuSlug)
    {
        if (string.IsNullOrWhiteSpace(gpuSlug))
        {
            return "Error: gpuSlug is required.";
        }

        if (!registry.Gpus.TryGetValue(gpuSlug, out var gpu))
        {
            var available = string.Join(", ", registry.Gpus.Keys);
            return $"GPU '{gpuSlug}' not found. Available: {available}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## {gpuSlug} ({gpu.VramGb}GB VRAM)");
        sb.AppendLine();
        sb.AppendLine("| Model | Q4 | Q8 | Notes |");
        sb.AppendLine("|-------|----|----|-------|");

        foreach (var (modelSlug, fits) in gpu.Fits)
        {
            var q4 = fits.TryGetValue("q4", out var q4Val) ? FormatFit(q4Val) : "unknown";
            var q8 = fits.TryGetValue("q8", out var q8Val) ? FormatFit(q8Val) : "unknown";

            var model = registry.Models.TryGetValue(modelSlug, out var entry)
                ? $"{modelSlug} ({entry.ParamsB}B)"
                : modelSlug;

            var notes = entry?.Notes ?? "";
            sb.AppendLine($"| {model} | {q4} | {q8} | {notes} |");
        }

        return sb.ToString();
    }

    private static string FormatFit(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => "✓ fits",
            System.Text.Json.JsonValueKind.False => "✗ no",
            System.Text.Json.JsonValueKind.String => element.GetString() switch
            {
                "tight" => "⚠ tight",
                var s => s ?? "unknown",
            },
            _ => "unknown",
        };
    }
}
