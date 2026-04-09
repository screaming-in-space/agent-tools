using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Agent.SDK.Console;

/// <summary>
/// Wraps an <see cref="AIFunction"/> to report tool invocation progress
/// to the active <see cref="IAgentOutput"/>. Each call triggers
/// <see cref="IAgentOutput.ToolStarted"/> / <see cref="IAgentOutput.ToolCompleted"/>.
/// </summary>
public sealed class ToolProgressWrapper : AIFunction
{
    private readonly AIFunction _inner;
    private readonly IAgentOutput _output;

    public ToolProgressWrapper(AIFunction inner, IAgentOutput output)
    {
        _inner = inner;
        _output = output;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var detail = ExtractDetail(arguments);
        var friendly = FriendlyName(_inner.Name);

        _output.ToolStarted(friendly, detail);
        var sw = Stopwatch.StartNew();

        try
        {
            return await _inner.InvokeAsync(arguments, cancellationToken);
        }
        finally
        {
            sw.Stop();
            _output.ToolCompleted(friendly, sw.Elapsed);
        }
    }

    private static string? ExtractDetail(AIFunctionArguments arguments)
    {
        foreach (var key in (ReadOnlySpan<string>)["filePath", "directoryPath", "path"])
        {
            if (arguments.TryGetValue(key, out var value) && value is string s && s.Length > 0)
            {
                return Path.GetFileName(s);
            }
        }

        return null;
    }

    private static string FriendlyName(string functionName) => functionName switch
    {
        "ListMarkdownFiles" => "Listing files",
        "ReadFileContent" => "Reading file",
        "ExtractStructure" => "Extracting structure",
        "WriteOutput" => "Writing output",
        _ => functionName
    };
}

/// <summary>
/// Extension to wrap tool lists with progress reporting.
/// </summary>
public static class ToolProgressExtensions
{
    /// <summary>
    /// Wraps each <see cref="AIFunction"/> in the list with progress reporting.
    /// In headless mode, returns the original list unchanged.
    /// </summary>
    public static IList<AITool> WithProgress(this IList<AITool> tools, IAgentOutput output)
    {
        if (!output.IsInteractive)
        {
            return tools;
        }

        return tools.Select(tool => tool is AIFunction func
            ? new ToolProgressWrapper(func, output)
            : tool).ToList<AITool>();
    }
}
