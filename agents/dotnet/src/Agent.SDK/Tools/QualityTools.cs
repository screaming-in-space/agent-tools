using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Agent.SDK.Tools;

/// <summary>
/// Code quality analysis tools. Uses Roslyn for C# files and regex heuristics for others.
/// </summary>
public static class QualityTools
{
    [Description("Analyzes a C# file using Roslyn. Returns per-method metrics (name, lines, complexity, params), file stats (classes, usings, namespace), and anti-patterns (.Result, .Wait(), async void, bare catch). Health grade: A=0 issues, B=1, C=2, D=3-4, F=5+. Issue triggers: >500 lines, >1000 lines, complexity>10, complexity>20, any anti-patterns, >3 anti-patterns, >20 methods.")]
    public static string AnalyzeCSharpFile(
        [Description("Path to the .cs file to analyze")] string filePath)
    {
        var resolved = FileTools.ResolveSafePath(filePath);
        if (resolved is null)
        {
            return $"Error: path '{filePath}' is outside the allowed root directory.";
        }

        if (!File.Exists(resolved))
        {
            return $"Error: file '{resolved}' does not exist.";
        }

        try
        {
            var content = File.ReadAllText(resolved);
            if (content.Length > 500_000)
            {
                return "Error: file too large for Roslyn analysis (>500KB).";
            }

            var tree = CSharpSyntaxTree.ParseText(content);
            var root = tree.GetCompilationUnitRoot();
            var rel = Path.GetRelativePath(FileTools.RootDirectory, resolved).Replace('\\', '/');

            var sb = new StringBuilder();
            sb.AppendLine($"## {rel}");
            sb.AppendLine();

            // File-level stats
            var usings = root.Usings.Count;
            var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Count();
            var classes = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count();
            var lines = content.Split('\n').Length;

            sb.AppendLine($"- Lines: {lines}");
            sb.AppendLine($"- Usings: {usings}");
            sb.AppendLine($"- Namespaces: {namespaces}");
            sb.AppendLine($"- Types: {classes}");
            sb.AppendLine();

            // Method metrics
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                sb.AppendLine("### Methods");
                sb.AppendLine();
                sb.AppendLine("| Method | Lines | Complexity | Params |");
                sb.AppendLine("|--------|-------|------------|--------|");

                foreach (var method in methods)
                {
                    var methodName = method.Identifier.Text;
                    var methodLines = method.GetLocation().GetLineSpan().EndLinePosition.Line
                        - method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var complexity = CalculateCyclomaticComplexity(method);
                    var paramCount = method.ParameterList.Parameters.Count;

                    sb.AppendLine($"| {methodName} | {methodLines} | {complexity} | {paramCount} |");
                }
                sb.AppendLine();
            }

            // Anti-patterns
            var antiPatterns = new List<string>();

            // .Result / .Wait()
            foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                var name = access.Name.Identifier.Text;
                if (name == "Result" || name == "Wait")
                {
                    var line = access.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    antiPatterns.Add($"L{line}: Sync-over-async ({name})");
                }
            }

            // async void
            foreach (var method in methods)
            {
                if (method.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
                    method.ReturnType.ToString() == "void")
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    antiPatterns.Add($"L{line}: async void ({method.Identifier.Text})");
                }
            }

            // Bare catch blocks
            foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (catchClause.Block.Statements.Count == 0)
                {
                    var line = catchClause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    antiPatterns.Add($"L{line}: Empty catch block");
                }
            }

            if (antiPatterns.Count > 0)
            {
                sb.AppendLine("### Anti-Patterns");
                foreach (var ap in antiPatterns)
                {
                    sb.AppendLine($"- {ap}");
                }
                sb.AppendLine();
            }

            // Health grade
            var grade = CalculateFileGrade(lines, methods.Count,
                methods.Count > 0 ? methods.Max(m => CalculateCyclomaticComplexity(m)) : 0,
                antiPatterns.Count);
            sb.AppendLine($"**Health Grade: {grade}**");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing C# file: {ex.Message}";
        }
    }

    [Description("Aggregates C# quality metrics for all .cs files in a directory. Returns total files, average method length, longest method, highest complexity, comment-to-code ratio, and anti-pattern counts.")]
    public static string AnalyzeCSharpProject(
        [Description("Absolute path to the project directory")] string directoryPath)
    {
        var resolved = FileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        var csFiles = Directory.EnumerateFiles(resolved, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        if (csFiles.Count == 0)
        {
            return "No C# files found (excluding bin/obj).";
        }

        var totalLines = 0;
        var totalMethods = 0;
        var totalAntiPatterns = 0;
        var maxComplexity = 0;
        var maxMethodLines = 0;
        var longestMethodName = "";
        var highestComplexityName = "";
        var methodLengths = new List<int>();
        var commentLines = 0;
        var codeLines = 0;

        foreach (var file in csFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (content.Length > 500_000) continue;

                var lines = content.Split('\n');
                totalLines += lines.Length;

                foreach (var line in lines)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                        commentLines++;
                    else if (trimmed.Length > 0)
                        codeLines++;
                }

                var tree = CSharpSyntaxTree.ParseText(content);
                var root = tree.GetCompilationUnitRoot();

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    totalMethods++;
                    var methodLines = method.GetLocation().GetLineSpan().EndLinePosition.Line
                        - method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    methodLengths.Add(methodLines);

                    if (methodLines > maxMethodLines)
                    {
                        maxMethodLines = methodLines;
                        longestMethodName = $"{Path.GetFileNameWithoutExtension(file)}.{method.Identifier.Text}";
                    }

                    var complexity = CalculateCyclomaticComplexity(method);
                    if (complexity > maxComplexity)
                    {
                        maxComplexity = complexity;
                        highestComplexityName = $"{Path.GetFileNameWithoutExtension(file)}.{method.Identifier.Text}";
                    }
                }

                // Count anti-patterns
                totalAntiPatterns += Regex.Count(content, @"\.(Result|Wait)\s*[(\s;]");
                totalAntiPatterns += root.DescendantNodes().OfType<CatchClauseSyntax>()
                    .Count(c => c.Block.Statements.Count == 0);
            }
            catch
            {
                // Skip files that fail to parse
            }
        }

        var avgMethodLength = methodLengths.Count > 0 ? methodLengths.Average() : 0;
        var commentRatio = codeLines > 0 ? (double)commentLines / codeLines * 100 : 0;

        var sb = new StringBuilder();
        var dirName = Path.GetFileName(resolved);
        sb.AppendLine($"## {dirName} Project Quality Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| C# Files | {csFiles.Count} |");
        sb.AppendLine($"| Total Lines | {totalLines:N0} |");
        sb.AppendLine($"| Total Methods | {totalMethods} |");
        sb.AppendLine($"| Avg Method Length | {avgMethodLength:F1} lines |");
        sb.AppendLine($"| Longest Method | {maxMethodLines} lines ({longestMethodName}) |");
        sb.AppendLine($"| Highest Complexity | {maxComplexity} ({highestComplexityName}) |");
        sb.AppendLine($"| Comment/Code Ratio | {commentRatio:F1}% |");
        sb.AppendLine($"| Anti-Patterns | {totalAntiPatterns} |");

        return sb.ToString();
    }

    [Description("Analyzes a non-C# source file using regex heuristics. Returns line count, comment ratio, TODO/FIXME count, magic number detection, long lines, and nesting depth.")]
    public static string AnalyzeSourceFile(
        [Description("Path to the source file")] string filePath,
        [Description("Language of the file (python, typescript, sql, etc.)")] string language)
    {
        var resolved = FileTools.ResolveSafePath(filePath);
        if (resolved is null)
        {
            return $"Error: path '{filePath}' is outside the allowed root directory.";
        }

        if (!File.Exists(resolved))
        {
            return $"Error: file '{resolved}' does not exist.";
        }

        var content = File.ReadAllText(resolved);
        if (content.Length > 200_000) content = content[..200_000];

        var lines = content.Split('\n');
        var rel = Path.GetRelativePath(FileTools.RootDirectory, resolved).Replace('\\', '/');

        var sb = new StringBuilder();
        sb.AppendLine($"## {rel} [{language}]");
        sb.AppendLine();

        var totalLines = lines.Length;
        var blankLines = lines.Count(l => l.Trim().Length == 0);
        var commentCount = 0;
        var todoCount = 0;
        var longLineCount = 0;
        var maxNesting = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Comment detection
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('#') || trimmed.StartsWith("--", StringComparison.Ordinal) ||
                trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("///", StringComparison.Ordinal))
                commentCount++;

            // TODO/FIXME
            if (Regex.IsMatch(line, @"\b(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase))
                todoCount++;

            // Long lines
            if (line.TrimEnd().Length > 120)
                longLineCount++;

            // Nesting (rough: count leading indent)
            var indent = line.Length - line.TrimStart().Length;
            var nestLevel = indent / 4; // assume 4-space indent
            if (nestLevel > maxNesting) maxNesting = nestLevel;
        }

        var codeLines = totalLines - blankLines - commentCount;
        var commentRatio = codeLines > 0 ? (double)commentCount / codeLines * 100 : 0;

        // Magic numbers (standalone numbers > 1 that aren't 0, 1, 2, -1, 100, 1000)
        var magicNumbers = Regex.Matches(content, @"(?<!\w)(\d{3,})(?!\w)")
            .Where(m =>
            {
                var val = m.Value;
                return val != "100" && val != "1000" && val != "1024" && val != "2048";
            })
            .Count();

        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Lines | {totalLines} |");
        sb.AppendLine($"| Code Lines | {codeLines} |");
        sb.AppendLine($"| Comment Ratio | {commentRatio:F1}% |");
        sb.AppendLine($"| TODO/FIXME | {todoCount} |");
        sb.AppendLine($"| Lines >120 chars | {longLineCount} |");
        sb.AppendLine($"| Max Nesting | {maxNesting} levels |");
        sb.AppendLine($"| Magic Numbers | {magicNumbers} |");

        // Grade
        var issues = 0;
        if (longLineCount > 5) issues++;
        if (maxNesting > 6) issues++;
        if (magicNumbers > 10) issues++;
        if (todoCount > 5) issues++;
        if (commentRatio < 2 && codeLines > 100) issues++;

        var grade = issues switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            _ => "F",
        };

        sb.AppendLine();
        sb.AppendLine($"**Health Grade: {grade}**");

        return sb.ToString();
    }

    [Description("Reads .editorconfig if present and returns active rules so quality checks can respect project conventions.")]
    public static string CheckEditorConfig(
        [Description("Absolute path to the directory to check")] string directoryPath)
    {
        var resolved = FileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        // Walk up looking for .editorconfig
        var dir = resolved;
        string? editorConfigPath = null;

        while (dir is not null)
        {
            var candidate = Path.Combine(dir, ".editorconfig");
            if (File.Exists(candidate))
            {
                editorConfigPath = candidate;
                break;
            }

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }

        if (editorConfigPath is null)
        {
            return "No .editorconfig found.";
        }

        var content = File.ReadAllText(editorConfigPath);
        var rel = Path.GetRelativePath(FileTools.RootDirectory, editorConfigPath).Replace('\\', '/');

        var sb = new StringBuilder();
        sb.AppendLine($"## .editorconfig ({rel})");
        sb.AppendLine();

        // Extract key rules
        var lines = content.Split('\n');
        var currentSection = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                currentSection = trimmed;
                continue;
            }

            if (trimmed.Contains('=') && !trimmed.StartsWith('#'))
            {
                var parts = trimmed.Split('=', 2);
                var key = parts[0].Trim();
                var value = parts[1].Trim();

                // Only report interesting rules
                if (key.Contains("indent") || key.Contains("namespace") || key.Contains("braces") ||
                    key.Contains("var_") || key.Contains("naming") || key.Contains("severity") ||
                    key.Contains("end_of_line") || key.Contains("charset"))
                {
                    sb.AppendLine($"- {key} = {value}");
                }
            }
        }

        return sb.ToString();
    }

    private static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1; // Base complexity

        foreach (var node in method.DescendantNodes())
        {
            complexity += node switch
            {
                IfStatementSyntax => 1,
                ConditionalExpressionSyntax => 1,
                SwitchSectionSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                WhileStatementSyntax => 1,
                DoStatementSyntax => 1,
                CatchClauseSyntax => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0,
            };
        }

        return complexity;
    }

    private static string CalculateFileGrade(int lines, int methodCount, int maxComplexity, int antiPatternCount)
    {
        var issues = 0;

        if (lines > 500) issues++;
        if (lines > 1000) issues++;
        if (maxComplexity > 10) issues++;
        if (maxComplexity > 20) issues++;
        if (antiPatternCount > 0) issues++;
        if (antiPatternCount > 3) issues++;
        if (methodCount > 20) issues++;

        return issues switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 or 4 => "D",
            _ => "F",
        };
    }
}
