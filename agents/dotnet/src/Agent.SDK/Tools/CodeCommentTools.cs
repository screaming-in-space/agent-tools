using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Agent.SDK.Tools;

/// <summary>
/// Tools for extracting comments and code patterns from source files.
/// Supports C#, SQL, Python, TypeScript/JavaScript, and shell scripts.
/// </summary>
public class CodeCommentTools
{
    private readonly FileTools _fileTools;

    public CodeCommentTools(FileTools fileTools)
    {
        _fileTools = fileTools;
    }

    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".csx"] = "csharp",
        [".fs"] = "fsharp",
        [".fsx"] = "fsharp",
        [".sql"] = "sql",
        [".py"] = "python",
        [".pyw"] = "python",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".sh"] = "shell",
        [".bash"] = "shell",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".go"] = "go",
        [".rs"] = "rust",
        [".java"] = "java",
        [".kt"] = "kotlin",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".json"] = "json",
        [".xml"] = "xml",
        [".csproj"] = "xml",
        [".fsproj"] = "xml",
        [".props"] = "xml",
        [".targets"] = "xml",
    };

    private static readonly HashSet<string> SourceExtensions =
    [
        ".cs", ".csx", ".fs", ".fsx", ".sql", ".py", ".pyw",
        ".ts", ".tsx", ".js", ".jsx", ".sh", ".bash", ".ps1", ".psm1",
        ".go", ".rs", ".java", ".kt",
    ];

    [Description("Discovers source files recursively in a directory. Auto-detects language from file extensions. Returns file paths with detected language.")]
    public string ListSourceFiles(
        [Description("Absolute path to the directory to scan")] string directoryPath,
        [Description("Comma-separated file extensions to include (e.g. '.cs,.sql'). If empty, discovers all known source types.")] string extensions = "")
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

        HashSet<string>? filterExts = null;
        if (extensions is { Length: > 0 })
        {
            filterExts = extensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var files = Directory.EnumerateFiles(resolved, "*.*", SearchOption.AllDirectories)
            .Where(f => !_fileTools.IsExcluded(f))
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                if (filterExts is not null)
                {
                    return filterExts.Contains(ext);
                }

                return SourceExtensions.Contains(ext);
            })
            .Select(f =>
            {
                var ext = Path.GetExtension(f);
                var lang = ExtensionToLanguage.GetValueOrDefault(ext, "unknown");
                var rel = Path.GetRelativePath(_fileTools.RootDirectory, f).Replace('\\', '/');
                return $"{rel} [{lang}]";
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return "No source files found.";
        }

        // Summarize by language
        var langCounts = files
            .Select(f => f[(f.LastIndexOf('[') + 1)..f.LastIndexOf(']')])
            .GroupBy(l => l)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {files.Count} source files ({string.Join(", ", langCounts)}):");
        foreach (var file in files)
        {
            sb.AppendLine(file);
        }

        return sb.ToString();
    }

    [Description("Extracts comments from a source file. Handles XML doc comments (///), C-style (//, /* */), SQL (--), Python (#, triple-quotes), and shell (#). Returns structured output with doc comments, inline comments, and TODO/FIXME markers.")]
    public string ExtractComments(
        [Description("Path to the source file, relative or absolute")] string filePath)
    {
        var resolved = _fileTools.ResolveSafePath(filePath);
        if (resolved is null)
        {
            return $"Error: path '{filePath}' is outside the allowed root directory.";
        }

        if (!File.Exists(resolved))
        {
            return $"Error: file '{resolved}' does not exist.";
        }

        var ext = Path.GetExtension(resolved);
        var lang = ExtensionToLanguage.GetValueOrDefault(ext, "unknown");
        var content = File.ReadAllText(resolved);

        // Truncate very large files
        if (content.Length > 200_000)
        {
            content = content[..200_000];
        }

        var lines = content.Split('\n');
        var docComments = new List<string>();
        var inlineComments = new List<string>();
        var todoMarkers = new List<string>();

        switch (lang)
        {
            case "csharp":
            case "fsharp":
            case "go":
            case "rust":
            case "java":
            case "kotlin":
            case "typescript":
            case "javascript":
                ExtractCStyleComments(lines, docComments, inlineComments, todoMarkers, lang);
                break;
            case "sql":
                ExtractSqlComments(lines, docComments, inlineComments, todoMarkers);
                break;
            case "python":
                ExtractPythonComments(lines, content, docComments, inlineComments, todoMarkers);
                break;
            case "shell":
            case "powershell":
                ExtractShellComments(lines, docComments, inlineComments, todoMarkers);
                break;
            default:
                return $"Language '{lang}' not supported for comment extraction.";
        }

        var rel = Path.GetRelativePath(_fileTools.RootDirectory, resolved).Replace('\\', '/');
        var sb = new StringBuilder();
        sb.AppendLine($"## {rel} [{lang}]");
        sb.AppendLine($"Lines: {lines.Length}");
        sb.AppendLine();

        if (docComments.Count > 0)
        {
            sb.AppendLine($"### Doc Comments ({docComments.Count})");
            foreach (var c in docComments.Take(50))
            {
                sb.AppendLine(c);
            }
            if (docComments.Count > 50)
            {
                sb.AppendLine($"... and {docComments.Count - 50} more");
            }
            sb.AppendLine();
        }

        if (inlineComments.Count > 0)
        {
            sb.AppendLine($"### Inline Comments ({inlineComments.Count})");
            foreach (var c in inlineComments.Take(30))
            {
                sb.AppendLine(c);
            }
            if (inlineComments.Count > 30)
            {
                sb.AppendLine($"... and {inlineComments.Count - 30} more");
            }
            sb.AppendLine();
        }

        if (todoMarkers.Count > 0)
        {
            sb.AppendLine($"### TODO/FIXME ({todoMarkers.Count})");
            foreach (var t in todoMarkers)
            {
                sb.AppendLine(t);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [Description("Scans a directory for architectural code patterns: DI registrations (builder.Services.Add*), base classes, interfaces, attributes, and naming conventions. Returns a pattern summary.")]
    public string ExtractCodePatterns(
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

        var csFiles = Directory.EnumerateFiles(resolved, "*.cs", SearchOption.AllDirectories)
            .Where(f => !_fileTools.IsExcluded(f))
            .ToList();
        if (csFiles.Count == 0)
        {
            return "No C# files found for pattern analysis.";
        }

        var diRegistrations = new List<string>();
        var baseClasses = new HashSet<string>();
        var interfaces = new HashSet<string>();
        var attributes = new HashSet<string>();
        var namingPatterns = new Dictionary<string, int>();

        foreach (var file in csFiles)
        {
            var content = ReadFileSafe(file);
            if (content is null) { continue; }

            // DI registrations
            foreach (Match m in Regex.Matches(content, @"(?:builder|services|Services)\s*\.\s*(Add\w+)\s*[<(]"))
            {
                diRegistrations.Add(m.Groups[1].Value);
            }

            // Base classes
            foreach (Match m in Regex.Matches(content, @"class\s+\w+\s*(?:<[^>]+>)?\s*:\s*(\w+)"))
            {
                var baseName = m.Groups[1].Value;
                if (!baseName.StartsWith('I') || baseName.Length < 2 || !char.IsUpper(baseName[1]))
                {
                    baseClasses.Add(baseName);
                }
            }

            // Interfaces implemented
            foreach (Match m in Regex.Matches(content, @":\s*(?:\w+\s*,\s*)*(I[A-Z]\w+)"))
            {
                interfaces.Add(m.Groups[1].Value);
            }

            // Attributes
            foreach (Match m in Regex.Matches(content, @"\[(\w+)(?:\(|])"))
            {
                var attr = m.Groups[1].Value;
                if (attr != "Description" && attr != "Obsolete" && attr != "Serializable")
                {
                    attributes.Add(attr);
                }
            }

            // Naming conventions: suffix patterns
            var fileName = Path.GetFileNameWithoutExtension(file);
            var suffixes = new[] { "Service", "Repository", "Repo", "Controller", "Handler", "Factory", "Provider", "Manager", "Helper", "Extensions", "Options", "Middleware", "Filter", "Validator" };
            foreach (var suffix in suffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    namingPatterns[suffix] = namingPatterns.GetValueOrDefault(suffix) + 1;
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Code Patterns ({csFiles.Count} C# files analyzed)");
        sb.AppendLine();

        if (diRegistrations.Count > 0)
        {
            var grouped = diRegistrations.GroupBy(r => r).OrderByDescending(g => g.Count()).Take(20);
            sb.AppendLine("### DI Registrations");
            foreach (var g in grouped)
            {
                sb.AppendLine($"- {g.Key} ({g.Count()}x)");
            }
            sb.AppendLine();
        }

        if (baseClasses.Count > 0)
        {
            sb.AppendLine("### Base Classes");
            foreach (var b in baseClasses.OrderBy(b => b).Take(20))
            {
                sb.AppendLine($"- {b}");
            }
            sb.AppendLine();
        }

        if (interfaces.Count > 0)
        {
            sb.AppendLine("### Interfaces");
            foreach (var i in interfaces.OrderBy(i => i).Take(30))
            {
                sb.AppendLine($"- {i}");
            }
            sb.AppendLine();
        }

        if (attributes.Count > 0)
        {
            sb.AppendLine("### Attributes");
            foreach (var a in attributes.OrderBy(a => a).Take(20))
            {
                sb.AppendLine($"- [{a}]");
            }
            sb.AppendLine();
        }

        if (namingPatterns.Count > 0)
        {
            sb.AppendLine("### Naming Conventions");
            foreach (var (suffix, count) in namingPatterns.OrderByDescending(p => p.Value))
            {
                sb.AppendLine($"- *{suffix}: {count} files");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void ExtractCStyleComments(string[] lines, List<string> docComments,
        List<string> inlineComments, List<string> todoMarkers, string lang)
    {
        var inBlockComment = false;
        var docBlock = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            var lineNum = i + 1;

            if (inBlockComment)
            {
                var endIdx = line.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    inBlockComment = false;
                    var text = line[..(endIdx)].Trim().TrimStart('*').Trim();
                    if (text.Length > 0)
                    {
                        docBlock.AppendLine(text);
                    }

                    if (docBlock.Length > 0)
                    {
                        docComments.Add($"L{lineNum}: {docBlock.ToString().Trim()}");
                        docBlock.Clear();
                    }
                }
                else
                {
                    var text = line.TrimStart('*').Trim();
                    if (text.Length > 0)
                    {
                        docBlock.AppendLine(text);
                    }
                }
                CheckTodoFixme(line, lineNum, todoMarkers);
                continue;
            }

            // XML doc comments (C#)
            if (lang == "csharp" && line.StartsWith("///", StringComparison.Ordinal))
            {
                var text = line[3..].Trim();
                docComments.Add($"L{lineNum}: {text}");
                CheckTodoFixme(text, lineNum, todoMarkers);
                continue;
            }

            // Block comment start
            var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
            if (blockStart >= 0)
            {
                var blockEnd = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                if (blockEnd >= 0)
                {
                    // Single-line block comment
                    var text = line[(blockStart + 2)..blockEnd].Trim();
                    if (text.Length > 0)
                    {
                        inlineComments.Add($"L{lineNum}: {text}");
                    }

                    CheckTodoFixme(text, lineNum, todoMarkers);
                }
                else
                {
                    inBlockComment = true;
                    var text = line[(blockStart + 2)..].Trim();
                    if (text.Length > 0)
                    {
                        docBlock.AppendLine(text);
                    }
                }
                continue;
            }

            // Line comments
            var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIdx >= 0)
            {
                var text = line[(commentIdx + 2)..].Trim();
                if (text.Length > 0)
                {
                    inlineComments.Add($"L{lineNum}: {text}");
                    CheckTodoFixme(text, lineNum, todoMarkers);
                }
            }
        }
    }

    private static void ExtractSqlComments(string[] lines, List<string> docComments,
        List<string> inlineComments, List<string> todoMarkers)
    {
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            var lineNum = i + 1;

            if (inBlockComment)
            {
                if (line.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }
                CheckTodoFixme(line, lineNum, todoMarkers);
                continue;
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                inBlockComment = !line.Contains("*/", StringComparison.Ordinal);
                var text = line.TrimStart('/').TrimStart('*').Trim();
                if (text.Length > 0) { docComments.Add($"L{lineNum}: {text}"); }
                CheckTodoFixme(line, lineNum, todoMarkers);
                continue;
            }

            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                var text = line[2..].Trim();
                if (text.Length > 0)
                {
                    inlineComments.Add($"L{lineNum}: {text}");
                    CheckTodoFixme(text, lineNum, todoMarkers);
                }
            }
        }
    }

    private static void ExtractPythonComments(string[] lines, string content,
        List<string> docComments, List<string> inlineComments, List<string> todoMarkers)
    {
        // Triple-quote docstrings
        foreach (Match m in Regex.Matches(content, @"\""\""\""\s*(.*?)\s*\""\""\""", RegexOptions.Singleline))
        {
            var text = m.Groups[1].Value.Trim();
            if (text.Length > 0 && text.Length < 500)
            {
                docComments.Add(text);
            }
        }

        // Hash comments
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            var lineNum = i + 1;

            if (line.StartsWith('#'))
            {
                var text = line[1..].Trim();
                if (text.Length > 0)
                {
                    inlineComments.Add($"L{lineNum}: {text}");
                    CheckTodoFixme(text, lineNum, todoMarkers);
                }
            }
            else
            {
                var hashIdx = line.IndexOf(" #", StringComparison.Ordinal);
                if (hashIdx >= 0)
                {
                    var text = line[(hashIdx + 2)..].Trim();
                    if (text.Length > 0)
                    {
                        inlineComments.Add($"L{lineNum}: {text}");
                        CheckTodoFixme(text, lineNum, todoMarkers);
                    }
                }
            }
        }
    }

    private static void ExtractShellComments(string[] lines, List<string> docComments,
        List<string> inlineComments, List<string> todoMarkers)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            var lineNum = i + 1;

            if (line.StartsWith('#') && !line.StartsWith("#!", StringComparison.Ordinal))
            {
                var text = line[1..].Trim();
                if (text.Length > 0)
                {
                    inlineComments.Add($"L{lineNum}: {text}");
                    CheckTodoFixme(text, lineNum, todoMarkers);
                }
            }
        }
    }

    private static void CheckTodoFixme(string text, int lineNum, List<string> todoMarkers)
    {
        if (Regex.IsMatch(text, @"\b(TODO|FIXME|HACK|XXX|BUG|WARN)\b", RegexOptions.IgnoreCase))
        {
            todoMarkers.Add($"L{lineNum}: {text.Trim()}");
        }
    }

    private static string? ReadFileSafe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 200_000) { return null; }
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}
