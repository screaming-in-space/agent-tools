using System.CommandLine;

namespace CrimeSceneInvestigator;

/// <summary>
/// System.CommandLine definitions for an Agent.
/// Argument, options, and root command assembly live here.
/// The action delegates to <see cref="AgentInCommand.RunAsync"/>.
/// </summary>
public static class AgentCommandSetup
{
    public static readonly Argument<DirectoryInfo> DirectoryArg = new("directory")
    {
        Description = "The markdown directory to investigate"
    };

    public static readonly Option<string?> ConfigKeyOption = new("--config-key")
    {
        Description = "Model configuration key in appsettings.json under Models:{key} (default: \"default\")"
    };

    public static readonly Option<string?> OutputOption = new("--output")
    {
        Description = "Output file path (default: CONTEXT.md in target directory)"
    };

    public static readonly Option<bool> HeadlessOption = new("--headless")
    {
        Description = "Disable rich terminal UI and use plain log output"
    };

    public static readonly Option<string?> ScanOption = new("--scan")
    {
        Description = "Comma-separated scanners to run: markdown,comments,structure,journal (default: all enabled in config)"
    };

    /// <summary>
    /// Builds the root command with all arguments, options, and the action wired to <see cref="AgentInCommand.RunAsync"/>.
    /// </summary>
    public static RootCommand CreateRootCommand(Func<ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new RootCommand("Crime Scene Investigator - Scan a markdown directory and produce a structured context map.")
        {
            DirectoryArg,
            ConfigKeyOption,
            OutputOption,
            HeadlessOption,
            ScanOption,
        };

        command.SetAction(action);

        return command;
    }
}
