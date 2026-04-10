using System.ComponentModel;
using System.Text;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Agent.SDK.Tools;

/// <summary>
/// Shared file system tools available to any agent. Each public static method
/// becomes an AIFunction via <c>AIFunctionFactory.Create</c>.
/// <para>
/// All path-accepting methods are sandboxed to <see cref="RootDirectory"/>.
/// Set it before calling any tool method.
/// </para>
/// </summary>
public static class FileTools
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

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

        const int maxChars = 100 * 1024;
        var fileInfo = new FileInfo(resolved);

        // Fast path: small files are read entirely.
        if (fileInfo.Length <= maxChars)
        {
            return File.ReadAllText(resolved, Encoding.UTF8);
        }

        // Large files: read only what we need to avoid excessive allocation.
        var buffer = new char[maxChars];
        using var reader = new StreamReader(resolved, Encoding.UTF8);
        var charsRead = reader.ReadBlock(buffer, 0, maxChars);

        return new string(buffer, 0, charsRead) +
            $"\n\n[Truncated at {charsRead:N0} of {fileInfo.Length:N0} chars — {(double)charsRead / fileInfo.Length:P0} of file shown]";
    }

    [Description("Extracts the structural elements from markdown content: YAML frontmatter fields, heading hierarchy, and markdown links. Uses a proper markdown parser so headings inside code blocks and links inside code spans are correctly ignored.")]
    public static string ExtractStructure(
        [Description("The raw markdown content to analyze")] string content)
    {
        var document = Markdown.Parse(content, Pipeline);
        var sb = new StringBuilder();

        // Frontmatter
        var frontmatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (frontmatter is not null)
        {
            sb.AppendLine("## Frontmatter");
            foreach (var line in frontmatter.Lines)
            {
                var text = line.ToString()?.Trim() ?? string.Empty;
                if (text.Length > 0 && text != "---")
                {
                    sb.AppendLine($"  {text}");
                }
            }
            sb.AppendLine();
        }

        // Headings
        sb.AppendLine("## Headings");
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var level = heading.Level;
            var indent = new string(' ', (level - 1) * 2);
            var prefix = new string('#', level);
            var headingText = GetInlineText(heading.Inline);
            sb.AppendLine($"  {indent}{prefix} {headingText}");
        }
        sb.AppendLine();

        // Links
        var links = document.Descendants<LinkInline>()
            .Where(link => link.Url is not null)
            .Select(link => (Text: GetInlineText(link), Url: link.Url!))
            .ToList();

        if (links.Count > 0)
        {
            sb.AppendLine("## Links");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (text, url) in links)
            {
                if (seen.Add(url))
                {
                    sb.AppendLine($"  [{text}]({url})");
                }
            }
            sb.AppendLine();
        }

        // Stats
        var lineCount = content.Split('\n').Length;
        var wordCount = content.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        sb.AppendLine("## Stats");
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
    public static string? ResolveSafePath(string path)
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

    /// <summary>
    /// Extracts plain text from a Markdig inline container (heading content, link content, etc.).
    /// </summary>
    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
            {
                sb.Append(literal.Content);
            }
            else if (inline is CodeInline code)
            {
                sb.Append('`');
                sb.Append(code.Content);
                sb.Append('`');
            }
            else if (inline is ContainerInline nested)
            {
                sb.Append(GetInlineText(nested));
            }
        }

        return sb.ToString();
    }
}
