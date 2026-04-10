using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

/// <summary>
/// Unit tests for <see cref="StructureTools"/> using isolated temp directories.
/// </summary>
[Collection("FileTools")]
public sealed class StructureToolsTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousRoot;

    public StructureToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"structure-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _previousRoot = FileTools.RootDirectory;
        FileTools.RootDirectory = _root;
    }

    public void Dispose()
    {
        FileTools.RootDirectory = _previousRoot;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── ListProjects ──

    [Fact]
    public void ListProjects_FindsCsprojFiles_ReturnsFormattedTable()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0");
        WriteCsproj("src/Lib/Lib.csproj", outputType: "Library", tfm: "net10.0");

        var result = StructureTools.ListProjects(_root);

        Assert.Contains("Projects (2)", result, StringComparison.Ordinal);
        Assert.Contains("App", result, StringComparison.Ordinal);
        Assert.Contains("Lib", result, StringComparison.Ordinal);
        Assert.Contains("Exe", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListProjects_EmptyDirectory_ReturnsNoneMessage()
    {
        var result = StructureTools.ListProjects(_root);

        Assert.Contains("No .NET project or solution files found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListProjects_NonexistentDirectory_ReturnsError()
    {
        var result = StructureTools.ListProjects(Path.Combine(_root, "nope"));

        Assert.Contains("does not exist", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListProjects_OutsideRoot_ReturnsError()
    {
        var result = StructureTools.ListProjects(Path.Combine(_root, "..", ".."));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListProjects_FindsSolutionFiles_ListsThem()
    {
        File.WriteAllText(Path.Combine(_root, "Test.sln"), "");
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0");

        var result = StructureTools.ListProjects(_root);

        Assert.Contains("Solutions", result, StringComparison.Ordinal);
        Assert.Contains("Test.sln", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListProjects_CountsReferences_ShowsInTable()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0",
            projectRefs: ["../Lib/Lib.csproj"],
            packageRefs: ["Newtonsoft.Json"]);
        WriteCsproj("src/Lib/Lib.csproj", outputType: "Library", tfm: "net10.0");

        var result = StructureTools.ListProjects(_root);

        // App should show 1 project ref, 1 package ref
        Assert.Contains("| App | Exe | net10.0 | 1 | 1 |", result, StringComparison.Ordinal);
        Assert.Contains("| Lib | Library | net10.0 | 0 | 0 |", result, StringComparison.Ordinal);
    }

    // ── ReadProjectFile ──

    [Fact]
    public void ReadProjectFile_ParsesProperties_ReturnsFormattedOutput()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0");

        var result = StructureTools.ReadProjectFile(Path.Combine(_root, "src", "App", "App.csproj"));

        Assert.Contains("## App", result, StringComparison.Ordinal);
        Assert.Contains("**Output Type:** Exe", result, StringComparison.Ordinal);
        Assert.Contains("**Target Framework:** net10.0", result, StringComparison.Ordinal);
        Assert.Contains("**Nullable:** enable", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProjectFile_WithProjectRefs_ListsThem()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0",
            projectRefs: ["../Lib/Lib.csproj"]);

        var result = StructureTools.ReadProjectFile(Path.Combine(_root, "src", "App", "App.csproj"));

        Assert.Contains("Project References", result, StringComparison.Ordinal);
        Assert.Contains("Lib", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProjectFile_WithPackageRefs_ListsThem()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Library", tfm: "net10.0",
            packageRefs: ["Serilog", "Markdig"]);

        var result = StructureTools.ReadProjectFile(Path.Combine(_root, "src", "App", "App.csproj"));

        Assert.Contains("Package References", result, StringComparison.Ordinal);
        Assert.Contains("Serilog", result, StringComparison.Ordinal);
        Assert.Contains("Markdig", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProjectFile_NonexistentFile_ReturnsError()
    {
        var result = StructureTools.ReadProjectFile(Path.Combine(_root, "missing.csproj"));

        Assert.Contains("does not exist", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProjectFile_OutsideRoot_ReturnsError()
    {
        var result = StructureTools.ReadProjectFile(Path.Combine(_root, "..", "..", "evil.csproj"));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── MapDependencyGraph ──

    [Fact]
    public void MapDependencyGraph_DetectsReferences_ReturnsGraph()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0",
            projectRefs: ["../Core/Core.csproj"]);
        WriteCsproj("src/Core/Core.csproj", outputType: "Library", tfm: "net10.0");

        var result = StructureTools.MapDependencyGraph(_root);

        Assert.Contains("Dependency Graph", result, StringComparison.Ordinal);
        Assert.Contains("App", result, StringComparison.Ordinal);
        Assert.Contains("Core", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MapDependencyGraph_NoProjects_ReturnsMessage()
    {
        var result = StructureTools.MapDependencyGraph(_root);

        Assert.Contains("No project files found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MapDependencyGraph_LeafProjects_IdentifiesThem()
    {
        WriteCsproj("src/App/App.csproj", outputType: "Exe", tfm: "net10.0",
            projectRefs: ["../Lib/Lib.csproj"]);
        WriteCsproj("src/Lib/Lib.csproj", outputType: "Library", tfm: "net10.0");

        var result = StructureTools.MapDependencyGraph(_root);

        Assert.Contains("Leaf projects", result, StringComparison.Ordinal);
        Assert.Contains("Lib", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MapDependencyGraph_OutsideRoot_ReturnsError()
    {
        var result = StructureTools.MapDependencyGraph(Path.Combine(_root, "..", ".."));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── DetectArchitecturePattern ──

    [Fact]
    public void DetectArchitecturePattern_FewProjects_DetectsMonolith()
    {
        var projectList = "| MyApp | Exe | net10.0 | 0 | 3 |";

        var result = StructureTools.DetectArchitecturePattern(projectList, "");

        Assert.Contains("Monolith", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectArchitecturePattern_NTierNames_DetectsNTier()
    {
        var projectList = """
            | MyApp.Core | Library | net10.0 | 0 | 2 |
            | MyApp.Services | Library | net10.0 | 1 | 0 |
            | MyApp.Infrastructure | Library | net10.0 | 1 | 5 |
            | MyApp.Api | Exe | net10.0 | 2 | 3 |
            """;

        var result = StructureTools.DetectArchitecturePattern(projectList, "");

        Assert.Contains("N-Tier", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectArchitecturePattern_HexagonalNames_DetectsCleanArch()
    {
        var projectList = """
            | MyApp.Domain.Core | Library | net10.0 | 0 | 0 |
            | MyApp.Ports | Library | net10.0 | 1 | 0 |
            | MyApp.Adapters | Library | net10.0 | 1 | 5 |
            | MyApp.Application | Exe | net10.0 | 2 | 3 |
            """;

        var result = StructureTools.DetectArchitecturePattern(projectList, "");

        Assert.Contains("Hexagonal / Clean", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectArchitecturePattern_EmptyInput_DetectsMonolith()
    {
        // Empty input has zero .csproj matches, which satisfies projectCount <= 3 => Monolith
        var result = StructureTools.DetectArchitecturePattern("", "");

        Assert.Contains("Architecture Classification", result, StringComparison.Ordinal);
        Assert.Contains("Monolith", result, StringComparison.Ordinal);
    }

    // ── Integration: ReadProjectFile against real Agent.SDK.csproj ──

    [Fact]
    public void ReadProjectFile_RealAgentSdkCsproj_ParsesSuccessfully()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return; // Skip gracefully if repo root not found
        }

        FileTools.RootDirectory = repoRoot;
        try
        {
            var csprojPath = Path.Combine(repoRoot, "src", "Agent.SDK", "Agent.SDK.csproj");
            if (!File.Exists(csprojPath))
            {
                return; // Skip if file not found
            }

            var result = StructureTools.ReadProjectFile(csprojPath);

            Assert.Contains("## Agent.SDK", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Error:", result, StringComparison.Ordinal);
            Assert.Contains("Package References", result, StringComparison.Ordinal);
            Assert.Contains("LibGit2Sharp", result, StringComparison.Ordinal);
            Assert.Contains("Markdig", result, StringComparison.Ordinal);
        }
        finally
        {
            FileTools.RootDirectory = _root;
        }
    }

    // ── Helpers ──

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    private void WriteCsproj(
        string relativePath,
        string outputType = "Library",
        string tfm = "net10.0",
        string[]? projectRefs = null,
        string[]? packageRefs = null)
    {
        var fullPath = Path.Combine(_root, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var projRefXml = string.Empty;
        if (projectRefs is { Length: > 0 })
        {
            var items = string.Join(Environment.NewLine,
                projectRefs.Select(r => $"    <ProjectReference Include=\"{r}\" />"));
            projRefXml = $"""

              <ItemGroup>
            {items}
              </ItemGroup>
            """;
        }

        var pkgRefXml = string.Empty;
        if (packageRefs is { Length: > 0 })
        {
            var items = string.Join(Environment.NewLine,
                packageRefs.Select(p => $"    <PackageReference Include=\"{p}\" />"));
            pkgRefXml = $"""

              <ItemGroup>
            {items}
              </ItemGroup>
            """;
        }

        var xml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>{tfm}</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            {projRefXml}{pkgRefXml}
            </Project>
            """;

        File.WriteAllText(fullPath, xml);
    }
}
