using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Logging;
using Agent.SDK.Telemetry;
using CrimeSceneInvestigator.Telemetry;
using CrimeSceneInvestigator.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using Serilog;

namespace CrimeSceneInvestigator;

/// <summary>
/// Core agent logic: resolves CLI options, builds the M.E.AI pipeline, and
/// runs the crime-scene investigation agent. Called from the System.CommandLine action.
/// </summary>
public record AgentInCommand(ILogger<AgentInCommand> logger)
{
    /// <summary>
    /// Runs the crime scene investigator agent with the parsed CLI values.
    /// Returns <c>0</c> on success, <c>1</c> on failure.
    /// </summary>
    public async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        var directory = parseResult.GetValue(AgentCommandSetup.DirectoryArg)!;
        var targetPath = directory.FullName;

        if (!directory.Exists)
        {
            logger.LogError("Directory '{TargetPath}' does not exist", targetPath);
            return 1;
        }

        var endpoint = parseResult.GetValue(AgentCommandSetup.EndpointOption)
            ?? Env("CSI_ENDPOINT")
            ?? "http://localhost:1234/v1";

        var apiKey = parseResult.GetValue(AgentCommandSetup.ApiKeyOption)
            ?? Env("CSI_API_KEY")
            ?? "no-key";

        var model = parseResult.GetValue(AgentCommandSetup.ModelOption)
            ?? Env("CSI_MODEL")
            ?? "";

        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(targetPath, "CONTEXT.md");

        logger.LogInformation("Target:   {TargetPath}", targetPath);
        logger.LogInformation("Endpoint: {Endpoint}", endpoint);
        logger.LogInformation("Model:    {Model}", string.IsNullOrEmpty(model) ? "(server default)" : model);
        logger.LogInformation("Output:   {OutputPath}", outputPath);

        // ── Configure tools ─────────────────────────────────────────────

        FileTools.RootDirectory = targetPath;

        var tools = new AITool[]
        {
            AIFunctionFactory.Create(FileTools.ListMarkdownFiles),
            AIFunctionFactory.Create(FileTools.ReadFileContent),
            AIFunctionFactory.Create(FileTools.ExtractStructure),
            AIFunctionFactory.Create(FileTools.WriteOutput),
        };

        // ── Build the client pipeline ───────────────────────────────────

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var credential = new ApiKeyCredential(apiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        // If model is empty, ChatClient requires a non-null model string.
        // LM Studio ignores the model field when only one model is loaded.
        var chatClient = openAiClient
            .GetChatClient(string.IsNullOrEmpty(model) ? "local" : model)
            .AsIChatClient();

        using var loggerFactory = AgentLogging.CreateLoggerFactory();

        IChatClient agent = new ChatClientBuilder(chatClient)
            .UseOpenTelemetry(loggerFactory, sourceName: CsiTrace.Instance.Source.Name)
            .UseFunctionInvocation(loggerFactory, c => c.MaximumIterationsPerRequest = 50)
            .Build();

        // ── Run the agent ───────────────────────────────────────────────

        var systemPrompt = SystemPrompt.Build(targetPath, outputPath);
        var stopwatch = Stopwatch.StartNew();

        Log.Information("Running agent...");

        using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("csi.target", targetPath);

        try
        {
            var response = await agent.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, $"Investigate the markdown files in: {targetPath}"),
                ],
                new ChatOptions { Tools = tools },
                ct);

            stopwatch.Stop();
            CsiMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            logger.LogInformation("Agent completed in {Duration:F1}s", stopwatch.Elapsed.TotalSeconds);
            Console.WriteLine();
            Console.WriteLine(response.Text);
            Console.WriteLine();
            logger.LogInformation("Done. Output written to: {OutputPath}", outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            span?.RecordError(ex);
            logger.LogError(ex, "Agent run failed");
            return 1;
        }
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
