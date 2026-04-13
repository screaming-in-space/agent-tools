using System.ComponentModel;
using System.Text;
using Agent.SDK.Tools;

namespace Sterling.Tools;

/// <summary>
/// Four tools for code quality review. Three delegate to Agent.SDK.
/// The fourth is a thin file-discovery wrapper.
/// </summary>
public class SterlingTools
{
    private readonly FileTools _fileTools;
    private readonly QualityTools _qualityTools;

    public SterlingTools(FileTools fileTools, QualityTools qualityTools)
    {
        _fileTools = fileTools;
        _qualityTools = qualityTools;
    }

    [Description("Lists all C# source files in the target directory, excluding bin/obj/generated. Returns relative paths sorted alphabetically. Call this first to understand the codebase scope.")]
    public string ListSourceFiles(
        [Description("Absolute path to the directory to scan")] string directoryPath)
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        if (!Directory.Exists(resolved))
        {
            return $"Error: directory '{resolved}' does not exist.";
        }

        var files = Directory.EnumerateFiles(resolved, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var segments = Path.GetRelativePath(resolved, f).Split(Path.DirectorySeparatorChar);
                return !segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("obj", StringComparison.OrdinalIgnoreCase));
            })
            .Select(f => Path.GetRelativePath(resolved, f).Replace('\\', '/'))
            .Order()
            .ToList();

        if (files.Count == 0)
        {
            return "No C# source files found in the directory.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {files.Count} C# files:");
        foreach (var file in files)
        {
            sb.AppendLine(file);
        }

        return sb.ToString();
    }

    [Description("Runs Roslyn static analysis on a C# file. Returns per-method metrics (name, lines, cyclomatic complexity, params), file stats, anti-patterns (.Result, .Wait(), async void, empty catch), and health grade A-F.")]
    public string AnalyzeFile(
        [Description("Path to the .cs file to analyze")] string filePath)
    {
        return _qualityTools.AnalyzeCSharpFile(filePath);
    }

    [Description("Reads a source file so you can review naming, design, and structure beyond what metrics capture. Use selectively — call AnalyzeFile first, then ReadFile only on files that need deeper review.")]
    public string ReadFile(
        [Description("Path to the file to read")] string filePath)
    {
        return _fileTools.ReadFileContent(filePath);
    }

    [Description("Writes the final quality report. This must be your last action.")]
    public string WriteReport(
        [Description("Output file path for the report")] string filePath,
        [Description("The complete markdown report content")] string content)
    {
        return _fileTools.WriteOutput(filePath, content);
    }
}
