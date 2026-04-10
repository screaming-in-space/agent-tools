using System.ClientModel;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Logging;
using CrimeSceneInvestigator.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace CrimeSceneInvestigator;

public sealed record AgentContext(
    string TargetPath,
    string OutputPath,
    AgentModelOptions ModelOptions,
    IList<AITool> Tools,
    AgentScanOptions ScanOptions) : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = AgentLogging.CreateLoggerFactory();

    /// <summary>
    /// Builds a chat client pipeline. Uses <paramref name="overrideOptions"/> when provided,
    /// otherwise falls back to <see cref="ModelOptions"/> from the context.
    /// The returned client is valid for the lifetime of this <see cref="AgentContext"/>.
    /// </summary>
    public IChatClient GetAgentClient(AgentModelOptions? overrideOptions = null)
    {
        var options = overrideOptions ?? ModelOptions;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };
        var credential = new ApiKeyCredential(options.ApiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        var chatClient = openAiClient
            .GetChatClient(string.IsNullOrEmpty(options.Model) ? "local" : options.Model)
            .AsIChatClient();

        return new ChatClientBuilder(chatClient)
            .UseStreamingInterceptor(AgentConsole.Output)
            .UseOpenTelemetry(_loggerFactory, sourceName: CsiTrace.Instance.Source.Name)
            .UseFunctionInvocation(_loggerFactory, c => c.MaximumIterationsPerRequest = 25)
            .Build();
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }
}
