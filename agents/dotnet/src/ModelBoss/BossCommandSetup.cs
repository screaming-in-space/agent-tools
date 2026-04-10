using System.CommandLine;

namespace ModelBoss;

/// <summary>
/// System.CommandLine definitions for the ModelBoss agent.
/// Arguments, options, and root command assembly live here.
/// </summary>
public static class BossCommandSetup
{
    public static readonly Option<string?> ConfigKeyOption = new("--config-key")
    {
        Description = "Model configuration key to benchmark (default: benchmarks all configured models)"
    };

    public static readonly Option<string?> OutputOption = new("--output")
    {
        Description = "Output directory for benchmark reports (default: ./benchmarks)"
    };

    public static readonly Option<bool> HeadlessOption = new("--headless")
    {
        Description = "Disable rich terminal UI and use plain log output"
    };

    public static readonly Option<string?> ModelsOption = new("--models")
    {
        Description = "Comma-separated config keys to benchmark (default: all configured models)"
    };

    public static readonly Option<int> IterationsOption = new("--iterations")
    {
        Description = "Number of measured iterations per prompt (default: 3)"
    };

    public static readonly Option<string?> CategoryOption = new("--category")
    {
        Description = "Benchmark category to run: instruction_following, extraction, markdown_generation, reasoning, all (default: all)"
    };

    public static readonly Option<string?> RepoRootOption = new("--repo-root")
    {
        Description = "Repository root for loading model/GPU registries (default: auto-detect from cwd)"
    };

    /// <summary>
    /// Builds the root command with all options and the action wired to <see cref="BossAgent.RunAsync"/>.
    /// </summary>
    public static RootCommand CreateRootCommand(Func<ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new RootCommand("ModelBoss - Benchmark local LLM models with speed, accuracy, and quality scoring.")
        {
            ConfigKeyOption,
            OutputOption,
            HeadlessOption,
            ModelsOption,
            IterationsOption,
            CategoryOption,
            RepoRootOption,
        };

        command.SetAction(action);

        return command;
    }
}
