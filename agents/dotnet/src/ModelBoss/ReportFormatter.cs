using System.Text;
using Agent.SDK.Configuration;
using ModelBoss.Benchmarks;

namespace ModelBoss;

/// <summary>
/// Formats the final benchmark report as markdown from a list of scorecards.
/// </summary>
internal static class ReportFormatter
{
    public static string FormatReport(
        List<ModelScorecard> scorecards,
        ModelRegistry registry,
        List<string> loadedModels)
    {
        var ranked = scorecards.OrderByDescending(s => s.CompositeScore).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("# Model Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"> Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"> Models tested: {ranked.Count}");
        sb.AppendLine($"> Models loaded: {string.Join(", ", loadedModels)}");
        sb.AppendLine();

        // ── Rankings ───────────────────────────────────────────────────
        sb.AppendLine("## Rankings");
        sb.AppendLine();
        sb.AppendLine("| Rank | Model | Config | Composite | Accuracy | Tok/s (median) | TTFT (ms) | Pass Rate |");
        sb.AppendLine("|------|-------|--------|-----------|----------|----------------|-----------|-----------|");

        for (var i = 0; i < ranked.Count; i++)
        {
            var card = ranked[i];
            sb.AppendLine(
                $"| {i + 1} | {card.ModelId} | {card.ConfigKey} " +
                $"| {card.CompositeScore:F3} | {card.MeanAccuracyScore:F3} " +
                $"| {card.MedianTokensPerSecond:F1} | {card.MedianTimeToFirstToken.TotalMilliseconds:F0} " +
                $"| {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0}) |");
        }

        sb.AppendLine();

        // ── Hardware Summary ───────────────────────────────────────────
        sb.AppendLine("## Hardware Summary");
        sb.AppendLine();

        if (registry.Gpus.Count > 0)
        {
            sb.AppendLine("| GPU | VRAM | Bandwidth | CUDA Cores |");
            sb.AppendLine("|-----|------|-----------|------------|");

            foreach (var (slug, gpu) in registry.Gpus)
            {
                sb.AppendLine($"| {slug} | {gpu.VramGb}GB | {gpu.BandwidthGbS} GB/s | {gpu.CudaCores} |");
            }

            sb.AppendLine();
        }

        // ── Per-Model Scorecards ───────────────────────────────────────
        sb.AppendLine("## Per-Model Scorecards");
        sb.AppendLine();

        foreach (var card in ranked)
        {
            sb.AppendLine($"### {card.ModelId} (config: {card.ConfigKey})");
            sb.AppendLine();

            if (card.RegistryInfo is not null)
            {
                var info = card.RegistryInfo;
                sb.AppendLine($"- **Parameters:** {info.ParamsB}B total, {info.ActiveParamsB}B active");
                sb.AppendLine($"- **Architecture:** {info.Architecture}");
                sb.AppendLine($"- **Context:** {info.ContextK}K, VRAM Q4: {info.VramQ4Gb}GB");
                sb.AppendLine($"- **Tool Calling:** {info.ToolCalling}, Thinking: {info.Thinking}");
                sb.AppendLine();
            }

            sb.AppendLine("**Speed:**");
            sb.AppendLine($"- Median tok/s: {card.MedianTokensPerSecond:F1}");
            sb.AppendLine($"- P5 tok/s: {card.P5TokensPerSecond:F1}");
            sb.AppendLine($"- Median TTFT: {card.MedianTimeToFirstToken.TotalMilliseconds:F0}ms");
            sb.AppendLine($"- Median total: {card.MedianTotalDuration.TotalSeconds:F1}s");
            sb.AppendLine();
            sb.AppendLine("**Accuracy:**");
            sb.AppendLine($"- Mean: {card.MeanAccuracyScore:F3}");
            sb.AppendLine($"- Pass rate: {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0})");
            sb.AppendLine();

            if (card.PromptResults.Count > 0)
            {
                sb.AppendLine("| Prompt | Category | Tok/s | Duration | Accuracy | Pass |");
                sb.AppendLine("|--------|----------|-------|----------|----------|------|");

                foreach (var pr in card.PromptResults)
                {
                    var category = pr.Benchmark.PromptName.Contains("format") || pr.Benchmark.PromptName.Contains("list") || pr.Benchmark.PromptName.Contains("stop")
                        ? "instruct" : pr.Benchmark.PromptName.Contains("extract")
                        ? "extract" : pr.Benchmark.PromptName.Contains("generate") || pr.Benchmark.PromptName.Contains("table")
                        ? "markdown" : "reason";

                    sb.AppendLine(
                        $"| {pr.PromptName} | {category} " +
                        $"| {pr.Benchmark.TokensPerSecond:F1} | {pr.Benchmark.TotalDuration.TotalSeconds:F1}s " +
                        $"| {pr.Accuracy.Score:F2} | {(pr.Accuracy.Passed ? "✓" : "✗")} |");
                }

                sb.AppendLine();
            }
        }

        // ── Recommendations ────────────────────────────────────────────
        sb.AppendLine("## Recommendations");
        sb.AppendLine();

        if (ranked.Count > 0)
        {
            var best = ranked[0];
            sb.AppendLine($"- **Best overall:** {best.ModelId} — composite {best.CompositeScore:F3}");

            var fastest = ranked.OrderByDescending(s => s.MedianTokensPerSecond).First();
            sb.AppendLine($"- **Best speed:** {fastest.ModelId} — {fastest.MedianTokensPerSecond:F1} tok/s, {fastest.MedianTimeToFirstToken.TotalMilliseconds:F0}ms TTFT");

            var mostAccurate = ranked.OrderByDescending(s => s.MeanAccuracyScore).First();
            sb.AppendLine($"- **Best accuracy:** {mostAccurate.ModelId} — {mostAccurate.MeanAccuracyScore:F3} mean, {mostAccurate.PassRate:P0} pass rate");

            var bestValue = ranked.OrderByDescending(s => s.MeanAccuracyScore * s.MedianTokensPerSecond).First();
            sb.AppendLine($"- **Best value (speed × accuracy):** {bestValue.ModelId}");
        }

        sb.AppendLine();

        // ── Methodology ────────────────────────────────────────────────
        sb.AppendLine("## Methodology");
        sb.AppendLine();
        sb.AppendLine("- Warmup iterations: 1");
        sb.AppendLine("- Measured iterations: per-run config (default 3)");
        sb.AppendLine("- Benchmark suites: instruction_following, extraction, markdown_generation, reasoning");
        sb.AppendLine("- Accuracy scoring: deterministic (substring matching, structure validation, bigram similarity)");
        sb.AppendLine("- Speed metrics: streaming token counting with Stopwatch-based timing");
        sb.AppendLine("- Composite formula: `(accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)`");
        sb.AppendLine("- Speed normalization: 50 tok/s = 1.0 (linear)");

        return sb.ToString();
    }
}
