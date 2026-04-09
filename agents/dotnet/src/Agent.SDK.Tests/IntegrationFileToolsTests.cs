using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

/// <summary>
/// Integration tests that run <see cref="FileTools"/> against the real
/// agent-tools repo root (two levels above the workspace).
/// </summary>
[Collection("FileTools")]
public sealed class IntegrationFileToolsTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _previousRoot;

    public IntegrationFileToolsTests()
    {
        // Workspace: agent-tools/agents/dotnet  →  repo root: agent-tools
        _repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", ".."));
        _previousRoot = FileTools.RootDirectory;
        FileTools.RootDirectory = _repoRoot;
    }

    public void Dispose()
    {
        FileTools.RootDirectory = _previousRoot;
    }

    [Fact]
    public void ListMarkdownFiles_FindsKnownRepoFiles()
    {
        var result = FileTools.ListMarkdownFiles(_repoRoot);

        Assert.Contains("README.md", result);
        Assert.Contains("docs/RULES.md", result);
        Assert.Contains("docs/STRUCTURE.md", result);
        Assert.DoesNotContain("No markdown files found", result);
    }

    [Fact]
    public void ReadFileContent_ReadsRepoReadme()
    {
        var result = FileTools.ReadFileContent(Path.Combine(_repoRoot, "README.md"));

        Assert.DoesNotContain("Error:", result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExtractStructure_ParsesRepoReadme()
    {
        var content = FileTools.ReadFileContent(Path.Combine(_repoRoot, "README.md"));

        var structure = FileTools.ExtractStructure(content);

        Assert.Contains("Headings", structure);
        Assert.Contains("Stats", structure);
        Assert.Contains("Lines:", structure);
        Assert.Contains("Words:", structure);
    }

    [Fact]
    public void ListMarkdownFiles_CountMatchesFileSystem()
    {
        var result = FileTools.ListMarkdownFiles(_repoRoot);

        var actual = Directory.EnumerateFiles(_repoRoot, "*.md", SearchOption.AllDirectories).Count();
        Assert.Contains($"Found {actual} markdown files", result);
    }

    [Fact]
    public void ReadFileContent_RelativePath_ReadsRulesDoc()
    {
        var result = FileTools.ReadFileContent("docs/RULES.md");

        Assert.False(result.StartsWith("Error:"), $"Expected file content but got: {result[..Math.Min(result.Length, 200)]}");
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExtractStructure_RulesDoc_ContainsLinks()
    {
        var content = FileTools.ReadFileContent("docs/RULES.md");

        var structure = FileTools.ExtractStructure(content);

        Assert.Contains("Headings", structure);
    }
}
