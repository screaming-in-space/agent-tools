using System.CommandLine;

namespace Sterling;

/// <summary>
/// System.CommandLine definitions for Sterling.
/// Positional directory argument plus minimal options.
/// </summary>
public static class AgentCommandSetup
{
    public static readonly Argument<DirectoryInfo> DirectoryArg = new("directory")
    {
        Description = "The C# codebase directory to review"
    };

    public static readonly Option<string?> ConfigKeyOption = new("--config-key")
    {
        Description = "Model configuration key in appsettings.json under Models:{key} (default: \"default\")"
    };

    public static readonly Option<string?> OutputOption = new("--output")
    {
        Description = "Output file path (default: QUALITY.md in target directory)"
    };

    public static readonly Option<bool> HeadlessOption = new("--headless")
    {
        Description = "Disable rich terminal UI and use plain log output"
    };

    public static RootCommand CreateRootCommand(Func<ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new RootCommand(
            "Sterling — code quality reviewer. Deterministic Roslyn analysis with staff-engineer editorial judgment.")
        {
            DirectoryArg,
            ConfigKeyOption,
            OutputOption,
            HeadlessOption,
        };

        command.SetAction(action);

        return command;
    }
}
