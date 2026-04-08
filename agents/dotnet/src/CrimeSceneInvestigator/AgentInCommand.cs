using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Logging;
using Agent.SDK.Telemetry;
using CrimeSceneInvestigator.Telemetry;
using CrimeSceneInvestigator.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using Serilog;

namespace CrimeSceneInvestigator;

/// <summary>
/// Core agent logic: resolves CLI options, builds the M.E.AI pipeline, and
/// runs the crime-scene investigation agent. Called from the System.CommandLine action.
/// </summary>
public record AgentInCommand(ILogger<AgentInCommand> Logger, IConfiguration Configuration)
{
    /// <summary>Resolved bootstrap state passed from <see cref="SetupAsync"/> to the agent run.</summary>
    private sealed record AgentContext(
        string TargetPath,
        string OutputPath,
        AgentModelOptions ModelOptions,
        IList<AITool> Tools);

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

        // ── Build the client pipeline ───────────────────────────────────

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(context.ModelOptions.Endpoint) };
        var credential = new ApiKeyCredential(context.ModelOptions.ApiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        // If model is empty, ChatClient requires a non-null model string.
        // LM Studio ignores the model field when only one model is loaded.
        var chatClient = openAiClient
            .GetChatClient(string.IsNullOrEmpty(context.ModelOptions.Model) ? "local" : context.ModelOptions.Model)
            .AsIChatClient();

        using var loggerFactory = AgentLogging.CreateLoggerFactory();

        IChatClient agent = new ChatClientBuilder(chatClient)
            .UseOpenTelemetry(loggerFactory, sourceName: CsiTrace.Instance.Source.Name)
            .UseFunctionInvocation(loggerFactory, c => c.MaximumIterationsPerRequest = 50)
            .Build();

        // ── Run the agent ───────────────────────────────────────────────

        var systemPrompt = SystemPrompt.Build(context.TargetPath, context.OutputPath);
        var stopwatch = Stopwatch.StartNew();

        Log.Information("Running agent...");

        using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("csi.target", context.TargetPath);

        try
        {
            var response = await agent.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, $"Investigate the markdown files in: {context.TargetPath}"),
                ],
                new ChatOptions { Tools = context.Tools },
                ct);

            stopwatch.Stop();
            CsiMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            Logger.LogInformation("Agent completed in {Duration:F1}s", stopwatch.Elapsed.TotalSeconds);
            Console.WriteLine();
            Console.WriteLine(response.Text);
            Console.WriteLine();
            Logger.LogInformation("Done. Output written to: {OutputPath}", context.OutputPath);
            return 0;
        }
        catch (Exception ex)
        {
            span?.RecordError(ex);
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

        Logger.LogInformation("Target:   {TargetPath}", targetPath);
        Logger.LogInformation("Config:   Models:{ConfigKey}", configKey ?? AgentModelOptions.DefaultKey);
        Logger.LogInformation("Endpoint: {Endpoint}", modelOptions.Endpoint);
        Logger.LogInformation("Model:    {Model}", string.IsNullOrEmpty(modelOptions.Model) ? "(server default)" : modelOptions.Model);
        Logger.LogInformation("Output:   {OutputPath}", outputPath);

        // ── Validate endpoint + model ───────────────────────────────────

        var health = await EndpointHealthCheck.ValidateAsync(modelOptions, ct: ct);

        if (!health.IsHealthy)
        {
            Logger.LogError("Endpoint health check failed: {Error}", health.Error);
            return (1, null);
        }

        Logger.LogInformation("Endpoint healthy — {Count} model(s) loaded: {Models}",
            health.LoadedModels.Count, string.Join(", ", health.LoadedModels));

        if (!health.IsModelLoaded)
        {
            Logger.LogError("Model not loaded: {Error}", health.Error);
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
