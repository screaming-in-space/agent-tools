using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Telemetry;
using Agent.SDK.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using Sterling.Telemetry;
using Sterling.Tools;

namespace Sterling;

/// <summary>
/// Core agent: resolves CLI options, builds one M.E.AI pipeline,
/// makes one <see cref="IChatClient.GetResponseAsync"/> call, done.
/// </summary>
public record SterlingAgent(ILogger<SterlingAgent> Logger, IConfiguration Configuration)
{
    public async Task<int> RunAsync(ParseResult parseResult, CancellationToken ct)
    {
        // ── Resolve CLI arguments ──────────────────────────────────────

        var directory = parseResult.GetValue(AgentCommandSetup.DirectoryArg)!;
        var targetPath = directory.FullName;

        if (!directory.Exists)
        {
            Logger.LogError("Directory '{TargetPath}' does not exist", targetPath);
            return 1;
        }

        var configKey = parseResult.GetValue(AgentCommandSetup.ConfigKeyOption);
        var modelOptions = AgentModelOptions.Resolve(Configuration, configKey);
        var outputPath = parseResult.GetValue(AgentCommandSetup.OutputOption)
            ?? Path.Combine(targetPath, "QUALITY.md");

        var output = AgentConsole.Output;
        var modelDisplay = string.IsNullOrEmpty(modelOptions.Model) ? "(server default)" : modelOptions.Model;
        await output.UpdateStatusAsync($"Target: {targetPath}");
        await output.UpdateStatusAsync($"Model: {modelDisplay}");
        await output.UpdateStatusAsync($"Output: {outputPath}");

        // ── Validate endpoint ──────────────────────────────────────────

        var health = await EndpointHealthCheck.ValidateAsync(modelOptions, ct: ct);

        if (!health.IsHealthy)
        {
            Logger.LogError("Endpoint health check failed: {Error}", health.Error);
            return 1;
        }

        if (!health.IsModelLoaded)
        {
            Logger.LogError("Model not loaded: {Error}", health.Error);
            return 1;
        }

        await output.UpdateStatusAsync($"Endpoint healthy — {health.LoadedModels.Count} model(s) loaded");

        // ── Build tools ────────────────────────────────────────────────

        var fileTools = new FileTools(targetPath);
        var qualityTools = new QualityTools(fileTools);
        var sterlingTools = new SterlingTools(fileTools, qualityTools);

        var tools = new AITool[]
        {
            AIFunctionFactory.Create(sterlingTools.ListSourceFiles),
            AIFunctionFactory.Create(sterlingTools.AnalyzeFile),
            AIFunctionFactory.Create(sterlingTools.ReadFile),
            AIFunctionFactory.Create(sterlingTools.WriteReport),
        };

        // ── Build pipeline ─────────────────────────────────────────────

        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(modelOptions.Endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };

        var chatClient = new OpenAIClient(new ApiKeyCredential(modelOptions.ApiKey), clientOptions)
            .GetChatClient(string.IsNullOrEmpty(modelOptions.Model) ? "local" : modelOptions.Model)
            .AsIChatClient();

        var agent = new ChatClientBuilder(chatClient)
            .UseOpenTelemetry(loggerFactory, sourceName: SterlingTrace.Instance.Source.Name)
            .UseFunctionInvocation(loggerFactory, c => c.MaximumIterationsPerRequest = 50)
            .Build();

        // ── Run ────────────────────────────────────────────────────────

        var stopwatch = Stopwatch.StartNew();
        using var span = SterlingTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("sterling.target", targetPath);

        try
        {
            await output.StartAsync("Sterling", ct);

            var response = await agent.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt.Build(targetPath, outputPath)),
                    new ChatMessage(ChatRole.User, $"Review the C# codebase in: {targetPath}"),
                ],
                new ChatOptions { Tools = tools },
                ct);

            stopwatch.Stop();
            SterlingMetrics.RunDuration.Record(stopwatch.Elapsed.TotalSeconds);
            span?.SetSuccess();

            await output.UpdateStatusAsync($"Completed in {stopwatch.Elapsed.TotalSeconds:F1}s");
            Logger.LogInformation("Sterling completed in {Duration:F1}s", stopwatch.Elapsed.TotalSeconds);

            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            span?.RecordError(ex);
            Logger.LogError(ex, "Sterling run failed");
            return 1;
        }
    }
}
