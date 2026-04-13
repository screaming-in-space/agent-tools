using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Telemetry;
using Agent.SDK.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using Sterling.Telemetry;
using Sterling.Tools;

namespace Sterling;

/// <summary>
/// CLI entry point: resolves options, builds a <see cref="ChatClientAgent"/>,
/// runs it once, and reports the result.
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

        // ── Build + run ────────────────────────────────────────────────

        var chatClient = CreateChatClient(modelOptions);
        var agent = BuildAgent(chatClient, targetPath, outputPath);

        var stopwatch = Stopwatch.StartNew();
        using var span = SterlingTrace.Instance.StartSpan("agent-run", ActivityKind.Client);
        span?.WithTag("sterling.target", targetPath);

        try
        {
            await output.StartAsync("Sterling", ct);

            await agent.RunAsync(
                $"Review the C# codebase in: {targetPath}",
                cancellationToken: ct);

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

    // ── Reusable agent construction ────────────────────────────────────

    /// <summary>
    /// Builds a MAF <see cref="ChatClientAgent"/> with Sterling's tools and prompt.
    /// Usable from both the CLI path and the <see cref="SterlingExecutor"/>.
    /// </summary>
    public static AIAgent BuildAgent(IChatClient chatClient, string targetPath, string outputPath)
    {
        var fileTools = new FileTools(targetPath);
        var qualityTools = new QualityTools(fileTools);
        var sterlingTools = new SterlingTools(fileTools, qualityTools);

        AITool[] tools =
        [
            AIFunctionFactory.Create(sterlingTools.ListSourceFiles),
            AIFunctionFactory.Create(sterlingTools.AnalyzeFile),
            AIFunctionFactory.Create(sterlingTools.ReadFile),
            AIFunctionFactory.Create(sterlingTools.WriteReport),
        ];

        return new ChatClientAgent(
            chatClient,
            name: "Sterling",
            instructions: SystemPrompt.Build(targetPath, outputPath),
            tools: tools);
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> from model options.
    /// Separated so the executor can build its own client if needed.
    /// </summary>
    public static IChatClient CreateChatClient(AgentModelOptions options)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions)
            .GetChatClient(string.IsNullOrEmpty(options.Model) ? "local" : options.Model)
            .AsIChatClient();
    }
}
