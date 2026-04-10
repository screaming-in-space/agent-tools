using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrimeSceneInvestigator;

/// <summary>
/// Asks the LLM to assign a model config key to each scanner based on
/// what's loaded and what each scanner needs.
/// </summary>
internal sealed record ScannerPlanner(ILogger Logger, IConfiguration Configuration)
{
    /// <summary>
    /// Plans model assignments for each scanner. Returns a dictionary of
    /// scanner name → <see cref="AgentModelOptions"/>. Returns empty when
    /// planning is unnecessary (0-1 models) or fails.
    /// </summary>
    public async Task<Dictionary<string, AgentModelOptions>> PlanAsync(
        AgentContext context,
        AgentScanOptions scanOptions,
        List<string> loadedModels,
        CancellationToken ct)
    {
        var output = AgentConsole.Output;
        var allConfigs = AgentModelOptions.ResolveAll(Configuration);

        // Filter to configs whose model is actually loaded
        var availableConfigs = allConfigs
            .Where(kv => loadedModels.Any(loaded =>
                loaded.Contains(kv.Value.Model, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Model.Contains(loaded, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // If 0 or 1 available models, skip planning — use context default for all
        if (availableConfigs.Count <= 1)
        {
            Logger.LogInformation("Skipping planner: {Count} model(s) available, using default for all scanners",
                availableConfigs.Count);
            return [];
        }

        // Build enabled scanner list
        var enabledScanners = new List<string>();
        if (scanOptions.ScanMarkdown) enabledScanners.Add("markdown");
        if (scanOptions.ScanCodeComments) enabledScanners.Add("rules");
        if (scanOptions.ScanCodePattern) { enabledScanners.Add("structure"); enabledScanners.Add("quality"); }
        if (scanOptions.ScanGitHistory) enabledScanners.Add("journal");
        enabledScanners.Add("done"); // always runs

        await output.ScannerStartedAsync("Planner", context.ModelOptions.Model);
        Logger.LogInformation("Running planner with {ModelCount} available models: {Models}",
            availableConfigs.Count, string.Join(", ", availableConfigs.Keys));
        var plannerSw = Stopwatch.StartNew();

        try
        {
            var systemPrompt = PlannerPrompt.Build(enabledScanners, loadedModels, availableConfigs);

            var agent = context.GetAgentClient();
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, "Assign the best model to each scanner. Respond with JSON only."),
            };

            var chatOptions = new ChatOptions();
            if (context.ModelOptions.Temperature.HasValue)
                chatOptions.Temperature = context.ModelOptions.Temperature;
            if (context.ModelOptions.MaxOutputTokens.HasValue)
                chatOptions.MaxOutputTokens = context.ModelOptions.MaxOutputTokens;

            var plannerBuf = new StringBuilder();
            await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    plannerBuf.Append(text);
                }
            }

            var responseText = plannerBuf.ToString();
            if (responseText.Length == 0)
            {
                Logger.LogWarning("Planner returned empty response, using default model for all scanners");
                return [];
            }

            // Parse the JSON from the response (may be wrapped in ```json ... ```)
            var plan = ParsePlannerResponse(responseText, allConfigs);

            plannerSw.Stop();
            await output.ScannerCompletedAsync("Planner", plannerSw.Elapsed, success: true);

            foreach (var (scanner, options) in plan)
            {
                Logger.LogInformation("Planner assigned {Scanner} → {Model}", scanner, options.Model);
            }

            return plan;
        }
        catch (Exception ex)
        {
            plannerSw.Stop();
            await output.ScannerCompletedAsync("Planner", plannerSw.Elapsed, success: false);
            Logger.LogError(ex, "Planner failed, using default model for all scanners");
            return [];
        }
    }

    /// <summary>
    /// Extracts the JSON object from the planner response and maps config keys to model options.
    /// </summary>
    internal static Dictionary<string, AgentModelOptions> ParsePlannerResponse(
        string response, Dictionary<string, AgentModelOptions> allConfigs)
    {
        var result = new Dictionary<string, AgentModelOptions>(StringComparer.OrdinalIgnoreCase);

        var cleaned = Regex.Replace(response, @"```(?:json)?", "").Trim();
        var start = cleaned.IndexOf('{');
        if (start < 0) { return result; }

        var depth = 0;
        var end = -1;
        for (var i = start; i < cleaned.Length; i++)
        {
            if (cleaned[i] == '{') { depth++; }
            else if (cleaned[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
        }

        if (end < 0) { return result; }
        var jsonMatch = cleaned.AsSpan(start, end - start + 1);

        try
        {
            var assignments = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonMatch);
            if (assignments is null) return result;

            foreach (var (scanner, configKey) in assignments)
            {
                if (string.Equals(configKey, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    result[scanner] = AgentModelOptions.Skipped;
                }
                else if (allConfigs.TryGetValue(configKey, out var options))
                {
                    result[scanner] = options;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON from the LLM — fall back to defaults
        }

        return result;
    }
}
