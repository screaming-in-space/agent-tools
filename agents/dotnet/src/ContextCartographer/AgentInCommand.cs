using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using ContextCartographer.Telemetry;
using ContextCartographer.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using Serilog;

namespace ContextCartographer;

/// <summary>
/// Core agent logic: resolves CLI options, builds the M.E.AI pipeline, and
/// runs the context-mapping agent. Called from the System.CommandLine action.
/// </summary>
public static class AgentInCommand
{
    /// <summary>
    /// Runs the context cartographer agent with the parsed CLI values.
    /// Returns <c>0</c> on success, <c>1</c> on failure.
    /// </summary>
    public static async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        var directory = parseResult.GetValue(Commands.DirectoryArg)!;
        var targetPath = directory.FullName;

        if (!directory.Exists)
        {
            Log.Error("Directory '{TargetPath}' does not exist", targetPath);
            return 1;
        }

        var endpoint = parseResult.GetValue(Commands.EndpointOption)
            ?? Env("CARTOGRAPHER_ENDPOINT")
            ?? "http://localhost:1234/v1";

        var apiKey = parseResult.GetValue(Commands.ApiKeyOption)
            ?? Env("CARTOGRAPHER_API_KEY")
            ?? "no-key";

        var model = parseResult.GetValue(Commands.ModelOption)
            ?? Env("CARTOGRAPHER_MODEL")
            ?? "";

        var outputPath = parseResult.GetValue(Commands.OutputOption)
            ?? Path.Combine(targetPath, "CONTEXT.md");

        Log.Information("Target:   {TargetPath}", targetPath);
        Log.Information("Endpoint: {Endpoint}", endpoint);
        Log.Information("Model:    {Model}", string.IsNullOrEmpty(model) ? "(server default)" : model);
        Log.Information("Output:   {OutputPath}", outputPath);

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

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());

        IChatClient agent = new ChatClientBuilder(chatClient)
            .UseOpenTelemetry(loggerFactory, sourceName: CartographerTrace.Source.Name)
            .UseFunctionInvocation(loggerFactory, c => c.MaximumIterationsPerRequest = 50)
            .Build();

        // ── Run the agent ───────────────────────────────────────────────

        var systemPrompt = SystemPrompt.Build(targetPath, outputPath);
        var stopwatch = Stopwatch.StartNew();

        Log.Information("Running agent...");

        using var span = CartographerTrace.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("cartographer.target", targetPath);

        try
        {
            var response = await agent.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, $"Map the markdown files in: {targetPath}"),
                ],
                new ChatOptions { Tools = tools },
                ct);

            stopwatch.Stop();
            CartographerMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            Log.Information("Agent completed in {Duration:F1}s", stopwatch.Elapsed.TotalSeconds);
            Console.WriteLine();
            Console.WriteLine(response.Text);
            Console.WriteLine();
            Log.Information("Done. Output written to: {OutputPath}", outputPath);

            return 0;
        }
        catch (Exception ex)
        {
            span?.RecordError(ex);
            Log.Error(ex, "Agent run failed");
            return 1;
        }
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
