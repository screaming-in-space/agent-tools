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
    private readonly Tools.FileTools? _fileTools;

    public ToolProgressWrapper(AIFunction inner, IAgentOutput output, Tools.FileTools? fileTools = null)
    {
        _inner = inner;
        _output = output;
        _fileTools = fileTools;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonElement JsonSchema => _inner.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var detail = ExtractDetail(arguments, _fileTools);
        var friendly = FriendlyName(_inner.Name);

        await _output.ToolStartedAsync(friendly, detail);
        var sw = Stopwatch.StartNew();

        // Yield to ensure the "Tool started" message is rendered before any potential hot loop/long-running tool execution.
        await Task.Yield();

        try
        {
            var result = await _inner.InvokeAsync(arguments, cancellationToken);
            sw.Stop();

            var resultStr = result?.ToString() ?? "";
            var isError = resultStr.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
            await _output.ToolCompletedAsync(friendly, sw.Elapsed, detail, success: !isError);

            if (isError)
            {
                await AgentErrorLog.LogAsync(friendly, $"{detail ?? _inner.Name}: {resultStr}");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _output.ToolCompletedAsync(friendly, sw.Elapsed, detail, success: false);
            await AgentErrorLog.LogAsync(friendly, $"{detail ?? _inner.Name}: exception", ex);
            throw;
        }
    }

    private static ReadOnlySpan<string> PrimaryFileKeys => new[] { "filePath", "directoryPath", "path" };
    private static ReadOnlySpan<string> PrimaryGitKeys => new[] { "commitSha", "date", "language" };
    private static ReadOnlySpan<string> SecondaryContextKeys => new[] { "commitSha", "date" };

    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.Ordinal)
    {
        // FileTools
        ["ListMarkdownFiles"] = "Listing files",
        ["ReadFileContent"] = "Reading file",
        ["ExtractStructure"] = "Extracting structure",
        ["WriteOutput"] = "Writing output",
        // CodeCommentTools
        ["ListSourceFiles"] = "Listing source files",
        ["ExtractComments"] = "Extracting comments",
        ["ExtractCodePatterns"] = "Scanning patterns",
        // StructureTools
        ["ListProjects"] = "Listing projects",
        ["ReadProjectFile"] = "Reading project",
        ["MapDependencyGraph"] = "Mapping dependencies",
        ["DetectArchitecturePattern"] = "Classifying architecture",
        // QualityTools
        ["AnalyzeCSharpFile"] = "Analyzing C# file",
        ["AnalyzeCSharpProject"] = "Analyzing project quality",
        ["AnalyzeSourceFile"] = "Analyzing source file",
        ["CheckEditorConfig"] = "Checking editorconfig",
        // GitTools
        ["GetGitLog"] = "Reading git log",
        ["GetGitDiff"] = "Reading git diff",
        ["GetGitStats"] = "Computing git stats",
        ["CheckJournalExists"] = "Checking journal",
    };

    private static string? ExtractDetail(AIFunctionArguments arguments, Tools.FileTools? fileTools)
    {
        // Primary: extract file/directory path and make it relative
        foreach (var key in PrimaryFileKeys)
        {
            if (arguments.TryGetValue(key, out var value) && ExtractString(value) is { Length: > 0 } s)
            {
                if (fileTools is not null)
                {
                    var root = fileTools.RootDirectory;
                    if (root.Length > 0)
                    {
                        var resolved = fileTools.ResolveSafePath(s);
                        if (resolved is not null)
                        {
                            var relative = Path.GetRelativePath(root, resolved).Replace('\\', '/');
                            if (relative != ".")
                            {
                                return AppendSecondaryDetail(relative, arguments);
                            }
                        }
                    }
                }

                var pathDetail = s.Replace('\\', '/');
                return AppendSecondaryDetail(pathDetail, arguments);
            }
        }

        // Fallback: if no path found, try secondary params alone
        foreach (var key in PrimaryGitKeys)
        {
            if (arguments.TryGetValue(key, out var value) && ExtractString(value) is { Length: > 0 } s)
            {
                return s;
            }
        }

        return null;
    }

    private static string AppendSecondaryDetail(string primary, AIFunctionArguments arguments)
    {
        foreach (var key in SecondaryContextKeys)
        {
            if (arguments.TryGetValue(key, out var value) && ExtractString(value) is { Length: > 0 } s)
            {
                return $"{primary} ({s})";
            }
        }

        return primary;
    }

    private static string? ExtractString(object? value) => value switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        _ => value?.ToString()
    };

    private static string FriendlyName(string functionName)
        => FriendlyNames.GetValueOrDefault(functionName, functionName);
}

/// <summary>
/// Extension to wrap tool lists with progress reporting.
/// </summary>
public static class ToolProgressExtensions
{
    /// <summary>
    /// Wraps each <see cref="AIFunction"/> in the list with progress reporting.
    /// Always wraps regardless of interactive/headless mode so that tool call
    /// counting works in both modes.
    /// </summary>
    public static IList<AITool> WithProgress(this IList<AITool> tools, IAgentOutput output, Tools.FileTools? fileTools = null)
    {
        return tools.Select(tool => tool is AIFunction func
            ? new ToolProgressWrapper(func, output, fileTools)
            : tool).ToList();
    }
}
