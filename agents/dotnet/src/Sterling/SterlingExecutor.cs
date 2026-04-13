using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Sterling;

/// <summary>
/// Request message for the Sterling workflow executor.
/// </summary>
public sealed record SterlingRequest(string TargetPath, string OutputPath);

/// <summary>
/// MAF workflow executor wrapping Sterling for graph composition.
/// Receives a <see cref="SterlingRequest"/>, runs the quality review,
/// returns the output file path.
///
/// <example>
/// <code>
/// var sterling = new SterlingExecutor(chatClient);
/// var builder = new WorkflowBuilder(sterling);
/// var workflow = builder.Build();
/// </code>
/// </example>
/// </summary>
internal sealed partial class SterlingExecutor(IChatClient chatClient) : Executor("Sterling")
{
    [MessageHandler]
    private async ValueTask<string> HandleAsync(SterlingRequest request, IWorkflowContext context)
    {
        var agent = SterlingAgent.BuildAgent(chatClient, request.TargetPath, request.OutputPath);

        await agent.RunAsync($"Review the C# codebase in: {request.TargetPath}");

        return request.OutputPath;
    }
}
