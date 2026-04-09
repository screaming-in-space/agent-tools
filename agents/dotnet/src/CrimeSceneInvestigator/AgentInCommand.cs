using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Telemetry;
using Agent.SDK.Tools;
using CrimeSceneInvestigator.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CrimeSceneInvestigator;

/// <summary>
/// Core agent logic: resolves CLI options, builds the M.E.AI pipeline, and
/// runs the crime-scene investigation agent. Called from the System.CommandLine action.
/// </summary>
public record AgentInCommand(ILogger<AgentInCommand> Logger, IConfiguration Configuration)
{
    /// <summary>
    /// Runs the crime scene investigator agent with the parsed CLI values.
    /// Returns <c>0</c> on success, <c>1</c> on failure.
    /// </summary>
    public async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        var (exitCode, context) = await SetupAsync(parseResult, ct);
        if (context is null)
        {
            return exitCode;
        }

        var output = AgentConsole.Output;
        IChatClient agent = await context.GetAgentClientAsync();

        // ── Run the agent ───────────────────────────────────────────────

        var systemPrompt = SystemPrompt.Build(context.TargetPath, context.OutputPath);
        var stopwatch = Stopwatch.StartNew();

        await output.StartAsync("Crime Scene Investigator", ct);

        using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("csi.target", context.TargetPath);

        try
        {
            var chatOptions = new ChatOptions
            {
                Tools = context.Tools.WithProgress(output),
            };

            if (context.ModelOptions.Temperature.HasValue)
            {
                chatOptions.Temperature = context.ModelOptions.Temperature;
            }

            if (context.ModelOptions.TopP.HasValue)
            {
                chatOptions.TopP = context.ModelOptions.TopP;
            }

            if (context.ModelOptions.MaxOutputTokens.HasValue)
            {
                chatOptions.MaxOutputTokens = context.ModelOptions.MaxOutputTokens;
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, $"Investigate the markdown files in: {context.TargetPath}"),
            };

            // Stream the response to show LLM thinking in real-time
            string? responseText = null;
            await foreach (var update in agent.GetStreamingResponseAsync(
                messages,
                chatOptions,
                ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    output.AppendThinking(text);
                    responseText = (responseText ?? "") + text;
                }
            }

            stopwatch.Stop();
            CsiMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            var filesProcessed = output.ToolCallCount;

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: output.ToolCallCount,
                FilesProcessed: filesProcessed,
                Duration: stopwatch.Elapsed,
                OutputPath: Path.GetRelativePath(context.TargetPath, context.OutputPath),
                Success: true), ct);

            output.WriteResponse(responseText ?? "");
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);

            await output.StopAsync(new AgentRunSummary(
                ToolCallCount: output.ToolCallCount,
                FilesProcessed: output.ToolCallCount,
                Duration: stopwatch.Elapsed,
                OutputPath: Path.GetRelativePath(context.TargetPath, context.OutputPath),
                Success: false), ct);

            Logger.LogError(ex, "Agent run failed");
            return 1;
        }
    }

    /// <summary>
    /// Resolves CLI options, binds model config, validates the endpoint,
    /// registers tools, and returns the context needed to run the agent.
    /// Returns exit code <c>1</c> with a <c>null</c> context on failure.
    /// </summary>
    private async Task<(int ExitCode, AgentContext? Context)> SetupAsync(
        ParseResult parseResult, CancellationToken ct)
    {
        var directory = parseResult.GetValue(AgentCommandSetup.DirectoryArg)!;
        var targetPath = directory.FullName;

        if (!directory.Exists)
        {
            Logger.LogError("Directory '{TargetPath}' does not exist", targetPath);
            return (1, null);
        }

        var configKey = parseResult.GetValue(AgentCommandSetup.ConfigKeyOption);
        var modelOptions = AgentModelOptions.Resolve(Configuration, configKey);

        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(targetPath, "CONTEXT.md");

        var output = AgentConsole.Output;
        var modelDisplay = string.IsNullOrEmpty(modelOptions.Model) ? "(server default)" : modelOptions.Model;

        output.UpdateStatus($"Target: {targetPath}");
        output.UpdateStatus($"Config: Models:{configKey ?? AgentModelOptions.DefaultKey}");
        output.UpdateStatus($"Endpoint: {modelOptions.Endpoint}");
        output.UpdateStatus($"Model: {modelDisplay}");
        output.UpdateStatus($"Output: {outputPath}");

        // ── Validate endpoint + model ───────────────────────────────────

        var health = await EndpointHealthCheck.ValidateAsync(modelOptions, ct: ct);

        if (!health.IsHealthy)
        {
            output.UpdateStatus($"Endpoint health check failed: {health.Error}");
            return (1, null);
        }

        output.UpdateStatus($"Endpoint healthy - {health.LoadedModels.Count} model(s) loaded");

        if (!health.IsModelLoaded)
        {
            output.UpdateStatus($"Model not loaded: {health.Error}");
            return (1, null);
        }

        // ── Register tools ──────────────────────────────────────────────

        FileTools.RootDirectory = targetPath;

        var tools = new AITool[]
        {
            AIFunctionFactory.Create(FileTools.ListMarkdownFiles),
            AIFunctionFactory.Create(FileTools.ReadFileContent),
            AIFunctionFactory.Create(FileTools.ExtractStructure),
            AIFunctionFactory.Create(FileTools.WriteOutput),
        };

        return (0, new AgentContext(targetPath, outputPath, modelOptions, tools));
    }
}
