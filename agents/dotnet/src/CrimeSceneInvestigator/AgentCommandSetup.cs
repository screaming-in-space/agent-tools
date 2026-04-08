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

    public static readonly Option<string?> EndpointOption = new("--endpoint")
    {
        Description = "OpenAI-compatible endpoint (default: http://localhost:1234/v1)"
    };

    public static readonly Option<string?> ApiKeyOption = new("--api-key")
    {
        Description = "API key (default: none — works with LM Studio)"
    };

    public static readonly Option<string?> ModelOption = new("--model")
    {
        Description = "Model identifier (default: use server default)"
    };

    public static readonly Option<string?> OutputOption = new("--output")
    {
        Description = "Output file path (default: CONTEXT.md in target directory)"
    };

    /// <summary>
    /// Builds the root command with all arguments, options, and the action wired to <see cref="AgentInCommand.RunAsync"/>.
    /// </summary>
    public static RootCommand CreateRootCommand(Func<ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new RootCommand("Crime Scene Investigator — Scan a markdown directory and produce a structured context map.")
        {
            DirectoryArg,
            EndpointOption,
            ApiKeyOption,
            ModelOption,
            OutputOption,
        };

        command.SetAction(action);

        return command;
    }
}
