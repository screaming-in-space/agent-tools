using System.ClientModel;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Agent.SDK.Logging;
using CrimeSceneInvestigator.Telemetry;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CrimeSceneInvestigator;

public sealed record AgentContext(
    string TargetPath,
    string OutputPath,
    AgentModelOptions ModelOptions,
    IList<AITool> Tools,
    AgentScanOptions ScanOptions)
{
    public async Task<IChatClient> GetAgentClientAsync()
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(ModelOptions.Endpoint),
            // Local models can be slow — allow up to 10 minutes per streaming request.
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };
        var credential = new ApiKeyCredential(ModelOptions.ApiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        // If model is empty, ChatClient requires a non-null model string.
        // LM Studio ignores the model field when only one model is loaded.
        var chatClient = openAiClient
            .GetChatClient(string.IsNullOrEmpty(ModelOptions.Model) ? "local" : ModelOptions.Model)
            .AsIChatClient();

        using var loggerFactory = AgentLogging.CreateLoggerFactory();

        IChatClient agent = new ChatClientBuilder(chatClient)
            .UseStreamingInterceptor(AgentConsole.Output)
            .UseOpenTelemetry(loggerFactory, sourceName: CsiTrace.Instance.Source.Name)
            .UseFunctionInvocation(loggerFactory, c => c.MaximumIterationsPerRequest = 50)
            .Build();

        return agent;
    }
}
