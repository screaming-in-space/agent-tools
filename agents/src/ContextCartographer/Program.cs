using System.ClientModel;
using ContextCartographer;
using ContextCartographer.Tools;
using Microsoft.Extensions.AI;
using OpenAI;

// ── Parse arguments ─────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("""
        Context Cartographer — Scan a markdown directory and produce a structured context map.

        Usage:
          ContextCartographer <directory> [options]

        Options:
          --endpoint <url>    OpenAI-compatible endpoint (default: http://localhost:1234/v1)
          --api-key <key>     API key (default: none — works with LM Studio)
          --model <name>      Model identifier (default: use server default)
          --output <path>     Output file path (default: CONTEXT.md in target directory)

        Environment variables (fallback):
          CARTOGRAPHER_ENDPOINT   Endpoint URL
          CARTOGRAPHER_API_KEY    API key
          CARTOGRAPHER_MODEL      Model name
        """);
    return;
}

var targetPath = Path.GetFullPath(args[0]);
if (!Directory.Exists(targetPath))
{
    Console.Error.WriteLine($"Error: directory '{targetPath}' does not exist.");
    return;
}

var endpoint = GetArg("--endpoint") ?? Env("CARTOGRAPHER_ENDPOINT") ?? "http://localhost:1234/v1";
var apiKey = GetArg("--api-key") ?? Env("CARTOGRAPHER_API_KEY") ?? "no-key";
var model = GetArg("--model") ?? Env("CARTOGRAPHER_MODEL") ?? "";
var outputPath = GetArg("--output") ?? Path.Combine(targetPath, "CONTEXT.md");

Console.WriteLine($"Target:   {targetPath}");
Console.WriteLine($"Endpoint: {endpoint}");
Console.WriteLine($"Model:    {(string.IsNullOrEmpty(model) ? "(server default)" : model)}");
Console.WriteLine($"Output:   {outputPath}");
Console.WriteLine();

// ── Configure tools ─────────────────────────────────────────────────────────

FileTools.RootDirectory = targetPath;

var tools = new AITool[]
{
    AIFunctionFactory.Create(FileTools.ListMarkdownFiles),
    AIFunctionFactory.Create(FileTools.ReadFileContent),
    AIFunctionFactory.Create(FileTools.ExtractStructure),
    AIFunctionFactory.Create(FileTools.WriteOutput),
};

// ── Build the client pipeline ───────────────────────────────────────────────

var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
var credential = new ApiKeyCredential(apiKey);
var openAiClient = new OpenAIClient(credential, clientOptions);

// If model is empty, ChatClient requires a non-null model string.
// LM Studio ignores the model field when only one model is loaded.
var chatClient = openAiClient
    .GetChatClient(string.IsNullOrEmpty(model) ? "local" : model)
    .AsIChatClient();

IChatClient agent = new ChatClientBuilder(chatClient)
    .UseFunctionInvocation(
        loggerFactory: null,
        configure: c => c.MaximumIterationsPerRequest = 50)
    .Build();

// ── Run the agent ───────────────────────────────────────────────────────────

var systemPrompt = SystemPrompt.Build(targetPath, outputPath);

Console.WriteLine("Running agent...");
Console.WriteLine();

var response = await agent.GetResponseAsync(
    [
        new ChatMessage(ChatRole.System, systemPrompt),
        new ChatMessage(ChatRole.User, $"Map the markdown files in: {targetPath}"),
    ],
    new ChatOptions { Tools = tools });

Console.WriteLine(response.Text);
Console.WriteLine();
Console.WriteLine($"Done. Output written to: {outputPath}");

// ── Helpers ─────────────────────────────────────────────────────────────────

string? GetArg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string? Env(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
