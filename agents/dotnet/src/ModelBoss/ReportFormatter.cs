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
        var hasJudge = ranked.Any(s => s.MeanJudgeScore.HasValue || s.IsJudgeModel);

        sb.AppendLine("## Rankings");
        sb.AppendLine();

        if (hasJudge)
        {
            sb.AppendLine("| Rank | Model | Config | Composite | Accuracy | Judge | Tok/s | Gen tok/s | TTFT (ms) | Think (ms) | Pass Rate |");
            sb.AppendLine("|------|-------|--------|-----------|----------|-------|-------|-----------|-----------|------------|-----------|");
        }
        else
        {
            sb.AppendLine("| Rank | Model | Config | Composite | Accuracy | Tok/s | Gen tok/s | TTFT (ms) | Think (ms) | Pass Rate |");
            sb.AppendLine("|------|-------|--------|-----------|----------|-------|-----------|-----------|------------|-----------|");
        }

        for (var i = 0; i < ranked.Count; i++)
        {
            var card = ranked[i];
            var thinkCol = card.UsesThinking
                ? $"{card.MedianThinkingDuration.TotalMilliseconds:F0}"
                : "-";
            var genCol = card.UsesThinking
                ? $"{card.MedianGenerationTokensPerSecond:F1}"
                : "-";

            if (hasJudge)
            {
                var judgeCol = card.IsJudgeModel
                    ? "★ judge"
                    : card.MeanJudgeScore.HasValue
                        ? $"{card.MeanJudgeScore.Value:F1}/10"
                        : "-";

                sb.AppendLine(
                    $"| {i + 1} | {card.ModelId} | {card.ConfigKey} " +
                    $"| {card.CompositeScore:F3} | {card.MeanAccuracyScore:F3} " +
                    $"| {judgeCol} " +
                    $"| {card.MedianTokensPerSecond:F1} | {genCol} " +
                    $"| {card.MedianTimeToFirstToken.TotalMilliseconds:F0} | {thinkCol} " +
                    $"| {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0}) |");
            }
            else
            {
                sb.AppendLine(
                    $"| {i + 1} | {card.ModelId} | {card.ConfigKey} " +
                    $"| {card.CompositeScore:F3} | {card.MeanAccuracyScore:F3} " +
                    $"| {card.MedianTokensPerSecond:F1} | {genCol} " +
                    $"| {card.MedianTimeToFirstToken.TotalMilliseconds:F0} | {thinkCol} " +
                    $"| {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0}) |");
            }
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

            if (card.UsesThinking)
            {
                sb.AppendLine($"- **Generation tok/s: {card.MedianGenerationTokensPerSecond:F1}** (excluding thinking overhead)");
                sb.AppendLine($"- Thinking tokens: {card.TotalThinkingTokens:N0} total across all runs");
                sb.AppendLine($"- Median thinking time: {card.MedianThinkingDuration.TotalMilliseconds:F0}ms");
            }

            sb.AppendLine();
            sb.AppendLine("**Accuracy:**");
            sb.AppendLine($"- Mean: {card.MeanAccuracyScore:F3}");
            sb.AppendLine($"- Pass rate: {card.PromptsPassedCount}/{card.TotalPromptsCount} ({card.PassRate:P0})");

            if (card.IsJudgeModel)
            {
                sb.AppendLine($"- **Judge model** — scored other models' responses (own responses not judged)");
            }
            else if (card.MeanJudgeScore.HasValue)
            {
                sb.AppendLine($"- Judge score: {card.MeanJudgeScore.Value:F1}/10 (normalized: {card.MeanJudgeNormalized!.Value:F3})");
                sb.AppendLine($"- Judged prompts: {card.JudgedPromptCount}");
            }

            sb.AppendLine();

            if (card.PromptResults.Count > 0)
            {
                var showThinking = card.UsesThinking;
                var showJudge = card.PromptResults.Any(pr => pr.Judge is not null);

                if (showThinking && showJudge)
                {
                    sb.AppendLine("| Prompt | Category | Tok/s | Gen tok/s | Think (ms) | Duration | Accuracy | Judge | Pass |");
                    sb.AppendLine("|--------|----------|-------|-----------|------------|----------|----------|-------|------|");
                }
                else if (showThinking)
                {
                    sb.AppendLine("| Prompt | Category | Tok/s | Gen tok/s | Think (ms) | Duration | Accuracy | Pass |");
                    sb.AppendLine("|--------|----------|-------|-----------|------------|----------|----------|------|");
                }
                else if (showJudge)
                {
                    sb.AppendLine("| Prompt | Category | Tok/s | Duration | Accuracy | Judge | Pass |");
                    sb.AppendLine("|--------|----------|-------|----------|----------|-------|------|");
                }
                else
                {
                    sb.AppendLine("| Prompt | Category | Tok/s | Duration | Accuracy | Pass |");
                    sb.AppendLine("|--------|----------|-------|----------|----------|------|");
                }

                foreach (var pr in card.PromptResults)
                {
                    var judgeStr = pr.Judge is not null ? $"{pr.Judge.Score}/10" : "-";

                    if (showThinking && showJudge)
                    {
                        sb.AppendLine(
                            $"| {pr.PromptName} | {pr.Category} " +
                            $"| {pr.Benchmark.TokensPerSecond:F1} | {pr.Benchmark.GenerationTokensPerSecond:F1} " +
                            $"| {pr.Benchmark.ThinkingDuration.TotalMilliseconds:F0} | {pr.Benchmark.TotalDuration.TotalSeconds:F1}s " +
                            $"| {pr.Accuracy.Score:F2} | {judgeStr} | {(pr.Accuracy.Passed ? "\u2713" : "\u2717")} |");
                    }
                    else if (showThinking)
                    {
                        sb.AppendLine(
                            $"| {pr.PromptName} | {pr.Category} " +
                            $"| {pr.Benchmark.TokensPerSecond:F1} | {pr.Benchmark.GenerationTokensPerSecond:F1} " +
                            $"| {pr.Benchmark.ThinkingDuration.TotalMilliseconds:F0} | {pr.Benchmark.TotalDuration.TotalSeconds:F1}s " +
                            $"| {pr.Accuracy.Score:F2} | {(pr.Accuracy.Passed ? "\u2713" : "\u2717")} |");
                    }
                    else if (showJudge)
                    {
                        sb.AppendLine(
                            $"| {pr.PromptName} | {pr.Category} " +
                            $"| {pr.Benchmark.TokensPerSecond:F1} | {pr.Benchmark.TotalDuration.TotalSeconds:F1}s " +
                            $"| {pr.Accuracy.Score:F2} | {judgeStr} | {(pr.Accuracy.Passed ? "\u2713" : "\u2717")} |");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"| {pr.PromptName} | {pr.Category} " +
                            $"| {pr.Benchmark.TokensPerSecond:F1} | {pr.Benchmark.TotalDuration.TotalSeconds:F1}s " +
                            $"| {pr.Accuracy.Score:F2} | {(pr.Accuracy.Passed ? "\u2713" : "\u2717")} |");
                    }
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
        sb.AppendLine("- Benchmark suites: instruction_following, extraction, markdown_generation, reasoning, multi_turn, context_window");
        sb.AppendLine("- Accuracy scoring: deterministic (substring matching, structure validation, bigram similarity)");
        sb.AppendLine("- Speed metrics: streaming token counting with Stopwatch-based timing");
        sb.AppendLine("- Thinking tokens: tracked separately via TextReasoningContent; generation tok/s excludes thinking overhead");

        if (hasJudge)
        {
            var judgeModel = ranked.FirstOrDefault(s => s.IsJudgeModel);
            sb.AppendLine($"- **LLM-as-judge:** best-scoring model ({judgeModel?.ModelId ?? "N/A"}) evaluates other models on 1-10 scale");
            sb.AppendLine("- Judge rubric: instruction following, accuracy, completeness, format compliance, conciseness");
            sb.AppendLine("- Judge model's own responses are not judged (deterministic scoring only)");
            sb.AppendLine("- Composite (with judge): `(accuracy × 0.35) + (judge × 0.30) + (normalized_speed × 0.25) + (pass_rate × 0.10)`");
            sb.AppendLine("- Composite (judge model): `(accuracy × 0.60) + (normalized_speed × 0.30) + (pass_rate × 0.10)`");
        }
        else
        {
            sb.AppendLine("- Composite formula: `(accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)`");
        }

        sb.AppendLine("- Speed normalization: 50 tok/s = 1.0 (linear)");

        return sb.ToString();
    }
}
