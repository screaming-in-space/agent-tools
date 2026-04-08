using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace CrimeSceneInvestigator.Tools;

/// <summary>
/// File system tools available to the agent. Each public static method becomes
/// an AIFunction via <c>AIFunctionFactory.Create</c>.
/// </summary>
public static partial class FileTools
{
    /// <summary>Root directory the agent is allowed to operate within.</summary>
    public static string RootDirectory { get; set; } = string.Empty;

    [Description("Lists all markdown (.md) files in the specified directory, recursively. Returns one relative path per line.")]
    public static string ListMarkdownFiles(
        [Description("Absolute path to the directory to scan")] string directoryPath)
    {
        var resolved = ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        if (!Directory.Exists(resolved))
        {
            return $"Error: directory '{resolved}' does not exist.";
        }

        var files = Directory.EnumerateFiles(resolved, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RootDirectory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return "No markdown files found.";
        }

        return $"Found {files.Count} markdown files:\n{string.Join('\n', files)}";
    }

    [Description("Reads the full text content of a file. Returns the content as a string, truncated at 100 KB if the file is very large.")]
    public static string ReadFileContent(
        [Description("Path to the file to read, relative to the root directory or absolute")] string filePath)
    {
        var resolved = ResolveSafePath(filePath);
        if (resolved is null)
        {
            return $"Error: path '{filePath}' is outside the allowed root directory.";
        }

        if (!File.Exists(resolved))
        {
            return $"Error: file '{resolved}' does not exist.";
        }

        const int maxBytes = 100 * 1024;
        var content = File.ReadAllText(resolved, Encoding.UTF8);

        if (content.Length > maxBytes)
        {
            return content[..maxBytes] + $"\n\n[Truncated — file exceeds {maxBytes / 1024} KB]";
        }

        return content;
    }

    [Description("Extracts the structural elements from markdown content: YAML frontmatter fields, heading hierarchy, and markdown links. This is a deterministic parse — no LLM call required.")]
    public static string ExtractStructure(
        [Description("The raw markdown content to analyze")] string content)
    {
        var sb = new StringBuilder();

        // Frontmatter
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIndex > 3)
            {
                var frontmatter = content[3..endIndex].Trim();
                sb.AppendLine("## Frontmatter");
                foreach (var line in frontmatter.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        sb.AppendLine($"  {trimmed}");
                    }
                }
                sb.AppendLine();
            }
        }

        // Headings
        sb.AppendLine("## Headings");
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                {
                    level++;
                }

                if (level <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                {
                    var indent = new string(' ', (level - 1) * 2);
                    sb.AppendLine($"  {indent}{trimmed[..(level + 1)]}{trimmed[(level + 1)..].Trim()}");
                }
            }
        }
        sb.AppendLine();

        // Links
        var links = LinkPattern().Matches(content);
        if (links.Count > 0)
        {
            sb.AppendLine("## Links");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match link in links)
            {
                var url = link.Groups[2].Value;
                if (seen.Add(url))
                {
                    sb.AppendLine($"  [{link.Groups[1].Value}]({url})");
                }
            }
            sb.AppendLine();
        }

        // Stats
        var lineCount = content.Split('\n').Length;
        var wordCount = content.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        sb.AppendLine($"## Stats");
        sb.AppendLine($"  Lines: {lineCount}");
        sb.AppendLine($"  Words: {wordCount}");

        return sb.ToString();
    }

    [Description("Writes text content to a file. Creates parent directories if needed. Returns a confirmation message.")]
    public static string WriteOutput(
        [Description("Absolute or root-relative path for the output file")] string filePath,
        [Description("The text content to write")] string content)
    {
        var resolved = ResolveSafePath(filePath);
        if (resolved is null)
        {
            return $"Error: path '{filePath}' is outside the allowed root directory.";
        }

        var directory = Path.GetDirectoryName(resolved);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolved, content, Encoding.UTF8);
        return $"Wrote {content.Length:N0} characters to {Path.GetRelativePath(RootDirectory, resolved).Replace('\\', '/')}";
    }

    /// <summary>
    /// Resolves a path (absolute or relative) and validates it falls within <see cref="RootDirectory"/>.
    /// Returns the full resolved path, or <c>null</c> if the path escapes the root.
    /// </summary>
    private static string? ResolveSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        if (Path.IsPathRooted(path))
        {
            full = Path.GetFullPath(path);
        }
        else
        {
            full = Path.GetFullPath(Path.Combine(RootDirectory, path));
        }

        var root = Path.GetFullPath(RootDirectory);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    [GeneratedRegex(@"\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex LinkPattern();
}
