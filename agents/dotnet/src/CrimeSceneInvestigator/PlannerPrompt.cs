using Agent.SDK.Configuration;

namespace CrimeSceneInvestigator;

/// <summary>
/// Describes a scanner for the planner: name, tool count, and complexity hint.
/// </summary>
public sealed record ScannerManifest(string Name, int ToolCount, string Complexity, string Description);

/// <summary>
/// Builds the system prompt for the model-assignment planning step.
/// The planner LLM looks at available models and scanner requirements,
/// then assigns the best model config key to each scanner.
/// </summary>
public static class PlannerPrompt
{
    /// <summary>
    /// All scanners with their complexity metadata. Used by both the planner
    /// prompt and <see cref="AgentInCommand"/> to build the scanner manifest.
    /// </summary>
    public static readonly ScannerManifest[] AllScanners =
    [
        new("markdown", 4, "light", "Lists and reads markdown files, writes a context map"),
        new("structure", 5, "light", "Parses .csproj files, builds dependency graph, classifies architecture"),
        new("rules", 5, "heavy", "Extracts code comments across many files, synthesizes design rules"),
        new("quality", 8, "heavy", "Roslyn C# analysis, cross-project metrics, editorconfig checking"),
        new("journal", 5, "medium", "Reads git log/diffs, synthesizes daily journal entries"),
        new("done", 5, "medium", "Reads prior scanner outputs, produces completion checklist"),
    ];

    /// <summary>
    /// Builds the system prompt for the planner.
    /// </summary>
    /// <param name="enabledScanners">Scanner names that are enabled for this run.</param>
    /// <param name="loadedModels">Model IDs currently loaded in the inference server.</param>
    /// <param name="configuredModels">Config key → model options from appsettings.json.</param>
    public static string Build(
        IReadOnlyList<string> enabledScanners,
        IReadOnlyList<string> loadedModels,
        IReadOnlyDictionary<string, AgentModelOptions> configuredModels)
    {
        var scannerBlock = string.Join("\n", AllScanners
            .Where(s => enabledScanners.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(s => $"- **{s.Name}** ({s.ToolCount} tools, {s.Complexity} complexity): {s.Description}"));

        var loadedBlock = string.Join("\n", loadedModels
            .Select(m => $"- {m}"));

        var configBlock = string.Join("\n", configuredModels
            .Select(kv => $"- **{kv.Key}**: model=`{kv.Value.Model}`, endpoint=`{kv.Value.Endpoint}`"));

        var jsonExample = """
            {"markdown": "default", "structure": "default", "rules": "gemma-26b", "quality": "gemma-26b", "journal": "default", "done": "default"}
            """;

        return $"""
            You are a planning assistant. Your job is to assign the best model to each scanner
            based on model capability and scanner complexity.

            ## Rules

            - **Heavy** scanners need models with strong function-calling and reasoning (prefer larger models).
            - **Medium** scanners work with mid-range models.
            - **Light** scanners work fine with smaller, faster models.
            - Only assign models that are both **configured** (have a config key) AND **loaded** (appear in the loaded models list).
            - If a configured model's `model` field matches (or is contained in) a loaded model ID, it's available.
            - If only one model is available for all scanners, assign it to everything.
            - Prefer faster/smaller models when they can handle the complexity — don't over-allocate.

            ## Loaded Models (currently in VRAM)

            {loadedBlock}

            ## Configured Model Keys (from appsettings.json)

            {configBlock}

            ## Scanners to Assign

            {scannerBlock}

            ## Output Format

            Respond with ONLY a JSON object mapping scanner name to config key. No other text.
            Example: {jsonExample}
            """;
    }
}
