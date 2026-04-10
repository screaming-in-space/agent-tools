using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Agent.SDK.Tools;

/// <summary>
/// Tools for analyzing .NET project structure: .csproj/.fsproj/.sln/.slnx parsing,
/// dependency graphs, and architecture pattern detection.
/// </summary>
public class StructureTools
{
    private readonly FileTools _fileTools;

    public StructureTools(FileTools fileTools)
    {
        _fileTools = fileTools;
    }

    [Description("Finds all .csproj, .fsproj, .sln, and .slnx files in a directory. For each project returns: name, output type, target framework, project references, and package references.")]
    public string ListProjects(
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

        var sb = new StringBuilder();

        // Find solution files
        var slnFiles = Directory.EnumerateFiles(resolved, "*.sln", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(resolved, "*.slnx", SearchOption.AllDirectories))
            .Where(f => !_fileTools.IsExcluded(f))
            .ToList();

        if (slnFiles.Count > 0)
        {
            sb.AppendLine("## Solutions");
            foreach (var sln in slnFiles)
            {
                sb.AppendLine($"- {Path.GetRelativePath(_fileTools.RootDirectory, sln).Replace('\\', '/')}");
            }
            sb.AppendLine();
        }

        // Find project files
        var projFiles = Directory.EnumerateFiles(resolved, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(resolved, "*.fsproj", SearchOption.AllDirectories))
            .Where(f => !_fileTools.IsExcluded(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projFiles.Count == 0 && slnFiles.Count == 0)
        {
            return "No .NET project or solution files found.";
        }

        sb.AppendLine($"## Projects ({projFiles.Count})");
        sb.AppendLine();
        sb.AppendLine("| Project | Type | TFM | References | Packages |");
        sb.AppendLine("|---------|------|-----|------------|----------|");

        foreach (var proj in projFiles)
        {
            var info = ParseProjectFile(proj);
            var relPath = Path.GetRelativePath(_fileTools.RootDirectory, proj).Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(proj);
            sb.AppendLine($"| {name} | {info.OutputType} | {info.TargetFramework} | {info.ProjectRefCount} | {info.PackageRefCount} |");
        }

        return sb.ToString();
    }

    [Description("Parses a .csproj or .fsproj XML file and returns structured info: output type, target framework, project references, package references, and key properties.")]
    public string ReadProjectFile(
        [Description("Path to the .csproj or .fsproj file")] string filePath)
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

        try
        {
            var doc = XDocument.Load(resolved);
            var root = doc.Root;
            if (root is null) return "Error: empty project file.";

            var sb = new StringBuilder();
            var name = Path.GetFileNameWithoutExtension(resolved);
            sb.AppendLine($"## {name}");
            sb.AppendLine();

            // Properties
            var tfm = GetProperty(root, "TargetFramework") ?? GetProperty(root, "TargetFrameworks") ?? "unknown";
            var outputType = GetProperty(root, "OutputType") ?? "Library";
            var nullable = GetProperty(root, "Nullable") ?? "disable";
            var implicitUsings = GetProperty(root, "ImplicitUsings") ?? "disable";

            sb.AppendLine($"- **Output Type:** {outputType}");
            sb.AppendLine($"- **Target Framework:** {tfm}");
            sb.AppendLine($"- **Nullable:** {nullable}");
            sb.AppendLine($"- **Implicit Usings:** {implicitUsings}");

            // Special properties
            var publishSingleFile = GetProperty(root, "PublishSingleFile");
            if (publishSingleFile is not null)
                sb.AppendLine($"- **PublishSingleFile:** {publishSingleFile}");

            var selfContained = GetProperty(root, "SelfContained");
            if (selfContained is not null)
                sb.AppendLine($"- **SelfContained:** {selfContained}");

            sb.AppendLine();

            // Project references
            var projRefs = root.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => v is not null)
                .ToList();

            if (projRefs.Count > 0)
            {
                sb.AppendLine("### Project References");
                foreach (var pr in projRefs)
                {
                    sb.AppendLine($"- {Path.GetFileNameWithoutExtension(pr!)}");
                }
                sb.AppendLine();
            }

            // Package references
            var pkgRefs = root.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e =>
                {
                    var include = e.Attribute("Include")?.Value ?? "unknown";
                    var version = e.Attribute("Version")?.Value ?? "(central)";
                    return (Name: include, Version: version);
                })
                .ToList();

            if (pkgRefs.Count > 0)
            {
                sb.AppendLine("### Package References");
                foreach (var (pkgName, version) in pkgRefs)
                {
                    sb.AppendLine($"- {pkgName} {version}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error parsing project file: {ex.Message}";
        }
    }

    [Description("Builds a dependency graph from project references in a directory. Returns a text-based graph showing which projects depend on which.")]
    public string MapDependencyGraph(
        [Description("Absolute path to the directory to scan")] string directoryPath)
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        var projFiles = Directory.EnumerateFiles(resolved, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(resolved, "*.fsproj", SearchOption.AllDirectories))
            .ToList();

        if (projFiles.Count == 0)
        {
            return "No project files found.";
        }

        var graph = new Dictionary<string, List<string>>();

        foreach (var proj in projFiles)
        {
            var name = Path.GetFileNameWithoutExtension(proj);
            try
            {
                var doc = XDocument.Load(proj);
                var refs = doc.Root?.Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")?.Value ?? ""))
                    .Where(r => r.Length > 0)
                    .ToList() ?? [];

                graph[name] = refs;
            }
            catch
            {
                graph[name] = [];
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Dependency Graph");
        sb.AppendLine();
        sb.AppendLine("```");

        // Find root projects (not referenced by anyone)
        var allReferenced = graph.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = graph.Keys.Where(k => !allReferenced.Contains(k)).OrderBy(k => k).ToList();

        if (roots.Count == 0) roots = graph.Keys.OrderBy(k => k).ToList();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            PrintDependencyTree(sb, graph, root, visited, 0);
        }

        sb.AppendLine("```");
        sb.AppendLine();

        // Leaf projects (no dependencies)
        var leaves = graph.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).OrderBy(k => k).ToList();
        if (leaves.Count > 0)
        {
            sb.AppendLine($"**Leaf projects (no dependencies):** {string.Join(", ", leaves)}");
        }

        return sb.ToString();
    }

    [Description("Given a list of project names and their dependencies, classifies the architecture pattern (N-Tier, Hexagonal, Vertical Slice, Monolith, Microservices, etc.) based on naming conventions and dependency direction. Pass the output from ListProjects and MapDependencyGraph.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method required for AIFunctionFactory.Create")]
    public string DetectArchitecturePattern(
        [Description("The output from ListProjects (project names, types, references)")] string projectList = "",
        [Description("The output from MapDependencyGraph (dependency tree)")] string dependencyGraph = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Architecture Classification");
        sb.AppendLine();

        var indicators = new Dictionary<string, int>
        {
            ["N-Tier"] = 0,
            ["Hexagonal / Clean"] = 0,
            ["Vertical Slice"] = 0,
            ["Monolith"] = 0,
            ["Microservices"] = 0,
            ["Modular Monolith"] = 0,
        };

        var combined = projectList + "\n" + dependencyGraph;
        var lower = combined.ToLowerInvariant();

        // N-Tier indicators
        if (Regex.IsMatch(lower, @"\b(core|domain|services|repos|repositories|data|infrastructure|hosts?|api|web)\b"))
            indicators["N-Tier"] += 3;
        if (Regex.IsMatch(lower, @"\b(controller|handler|middleware)\b"))
            indicators["N-Tier"]++;

        // Hexagonal indicators
        if (Regex.IsMatch(lower, @"\b(ports?|adapters?|application|domain\.core)\b"))
            indicators["Hexagonal / Clean"] += 3;
        if (Regex.IsMatch(lower, @"\b(usecase|interactor)\b"))
            indicators["Hexagonal / Clean"] += 2;

        // Vertical Slice indicators
        if (Regex.IsMatch(lower, @"\b(features?|slices?|mediatr|mediator)\b"))
            indicators["Vertical Slice"] += 3;

        // Microservices indicators
        var projectCount = Regex.Count(lower, @"\.csproj|\.fsproj");
        if (Regex.IsMatch(lower, @"\b(gateway|worker|relay|service\d|micro)\b"))
            indicators["Microservices"] += 2;
        if (projectCount > 10)
            indicators["Microservices"]++;

        // Modular Monolith indicators
        if (Regex.IsMatch(lower, @"\b(modules?|apphost|aspire)\b"))
            indicators["Modular Monolith"] += 2;
        if (projectCount > 5 && Regex.IsMatch(lower, @"\b(host|apphost)\b"))
            indicators["Modular Monolith"]++;

        // Monolith
        if (projectCount <= 3)
            indicators["Monolith"] += 3;

        var ranked = indicators.OrderByDescending(kv => kv.Value).Where(kv => kv.Value > 0).ToList();

        if (ranked.Count == 0)
        {
            sb.AppendLine("**Pattern:** Unknown (insufficient project structure to classify)");
        }
        else
        {
            sb.AppendLine($"**Primary Pattern:** {ranked[0].Key} (confidence: {ranked[0].Value} indicators)");
            if (ranked.Count > 1 && ranked[1].Value > 1)
            {
                sb.AppendLine($"**Secondary Pattern:** {ranked[1].Key} (confidence: {ranked[1].Value} indicators)");
            }
            sb.AppendLine();
            sb.AppendLine("**Indicators found:**");
            foreach (var (pattern, score) in ranked)
            {
                sb.AppendLine($"- {pattern}: {score}");
            }
        }

        return sb.ToString();
    }

    private static ProjectInfo ParseProjectFile(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root!;

            return new ProjectInfo(
                OutputType: GetProperty(root, "OutputType") ?? "Library",
                TargetFramework: GetProperty(root, "TargetFramework") ?? GetProperty(root, "TargetFrameworks") ?? "?",
                ProjectRefCount: root.Descendants().Count(e => e.Name.LocalName == "ProjectReference"),
                PackageRefCount: root.Descendants().Count(e => e.Name.LocalName == "PackageReference")
            );
        }
        catch
        {
            return new ProjectInfo("?", "?", 0, 0);
        }
    }

    private static string? GetProperty(XElement root, string name)
    {
        return root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == name)?
            .Value;
    }

    private static void PrintDependencyTree(StringBuilder sb, Dictionary<string, List<string>> graph,
        string node, HashSet<string> visited, int depth)
    {
        var indent = new string(' ', depth * 2);
        var prefix = depth == 0 ? "" : "└── ";

        if (!visited.Add(node))
        {
            sb.AppendLine($"{indent}{prefix}{node} (circular)");
            return;
        }

        sb.AppendLine($"{indent}{prefix}{node}");

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps.OrderBy(d => d))
            {
                PrintDependencyTree(sb, graph, dep, visited, depth + 1);
            }
        }

        visited.Remove(node);
    }

    private sealed record ProjectInfo(string OutputType, string TargetFramework, int ProjectRefCount, int PackageRefCount);
}
