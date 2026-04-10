using System.ComponentModel;
using System.Text;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Agent.SDK.Tools;

/// <summary>
/// Shared file system tools available to any agent. Each public method
/// becomes an AIFunction via <c>AIFunctionFactory.Create</c>.
/// <para>
/// All path-accepting methods are sandboxed to <see cref="RootDirectory"/>.
/// Create an instance with the target directory before calling any tool method.
/// </para>
/// </summary>
public sealed class FileTools
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    /// <summary>Root directory the agent is allowed to operate within.</summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Directory to exclude from listing operations (e.g., the scanner output directory).
    /// Prevents scanners from reading their own stale output as input.
    /// </summary>
    public string ExcludeDirectory { get; }

    private readonly HashSet<string> _listedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _readFiles = new(StringComparer.OrdinalIgnoreCase);

    public FileTools(string rootDirectory, string excludeDirectory = "")
    {
        RootDirectory = rootDirectory;
        ExcludeDirectory = excludeDirectory;
    }

    /// <summary>
    /// Clears read-tracking state so tools can be reused across scanner runs
    /// without returning stale "already read" messages.
    /// </summary>
    public void ResetReadTracking()
    {
        _listedDirectories.Clear();
        _readFiles.Clear();
    }

    /// <summary>
    /// Walks up from <paramref name="startPath"/> to find the nearest directory
    /// containing a <c>.git</c> folder. Returns <c>null</c> if no repo root is found.
    /// </summary>
    public static string? FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [Description("Lists all markdown (.md) files in the specified directory, recursively. Returns one relative path per line.")]
    public string ListMarkdownFiles(
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

        if (!_listedDirectories.Add(resolved))
        {
            return "Already listed. Use the file paths from the first call. Proceed to read each file, then compose and call WriteOutput.";
        }

        var files = Directory.EnumerateFiles(resolved, "*.md", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
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
    public string ReadFileContent(
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

        if (!_readFiles.Add(resolved))
        {
            return $"Already read '{filePath}'. Use the content from the first read. Do not re-read files — proceed with your next step or call WriteOutput.";
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
    public string WriteOutput(
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
    /// Directory segments that are always skipped during recursive enumeration.
    /// Only VCS/IDE internals — keep this minimal so LLM context files
    /// (e.g. <c>copilot-instructions.md</c>, <c>CLAUDE.md</c>) are never missed.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs",
    };

    /// <summary>
    /// Returns <c>true</c> if the file should be skipped — either because it
    /// falls within <see cref="ExcludeDirectory"/> or resides under a
    /// VCS/IDE internal directory (e.g. <c>.git</c>, <c>.vs</c>).
    /// </summary>
    public bool IsExcluded(string absolutePath)
    {
        var fileFull = Path.GetFullPath(absolutePath);

        if (!string.IsNullOrEmpty(ExcludeDirectory))
        {
            var excludeFull = Path.GetFullPath(ExcludeDirectory);
            if (fileFull.StartsWith(excludeFull, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Walk the path segments looking for excluded directory names.
        var relative = Path.GetRelativePath(RootDirectory, fileFull);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a path (absolute or relative) and validates it falls within <see cref="RootDirectory"/>.
    /// Returns the full resolved path, or <c>null</c> if the path escapes the root.
    /// </summary>
    public string? ResolveSafePath(string path)
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
