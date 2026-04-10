using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

/// <summary>
/// Integration tests that run <see cref="FileTools"/> against the real
/// agent-tools repo root (two levels above the workspace).
/// </summary>
[Collection("FileTools")]
public sealed class IntegrationFileToolsTests
{
    private readonly string _repoRoot;
    private readonly FileTools _fileTools;

    public IntegrationFileToolsTests()
    {
        // Walk up from bin output to the repo root (find .git directory)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        _repoRoot = dir?.FullName ?? throw new DirectoryNotFoundException(
            $"Could not find git repo root from {AppContext.BaseDirectory}");
        _fileTools = new FileTools(_repoRoot);
    }

    [Fact]
    public void ListMarkdownFiles_FindsKnownRepoFiles()
    {
        var result = _fileTools.ListMarkdownFiles(_repoRoot);

        Assert.Contains("README.md", result);
        Assert.Contains("context/RULES.md", result);
        Assert.Contains("context/STRUCTURE.md", result);
        Assert.DoesNotContain("No markdown files found", result);
    }

    [Fact]
    public void ReadFileContent_ReadsRepoReadme()
    {
        var result = _fileTools.ReadFileContent(Path.Combine(_repoRoot, "README.md"));

        Assert.DoesNotContain("Error:", result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExtractStructure_ParsesRepoReadme()
    {
        var content = _fileTools.ReadFileContent(Path.Combine(_repoRoot, "README.md"));

        var structure = FileTools.ExtractStructure(content);

        Assert.Contains("Headings", structure);
        Assert.Contains("Stats", structure);
        Assert.Contains("Lines:", structure);
        Assert.Contains("Words:", structure);
    }

    [Fact]
    public void ListMarkdownFiles_CountMatchesFileSystem()
    {
        var result = _fileTools.ListMarkdownFiles(_repoRoot);

        var actual = Directory.EnumerateFiles(_repoRoot, "*.md", SearchOption.AllDirectories).Count();
        Assert.Contains($"Found {actual} markdown files", result);
    }

    [Fact]
    public void ReadFileContent_RelativePath_ReadsRulesDoc()
    {
        var result = _fileTools.ReadFileContent("context/RULES.md");

        Assert.False(result.StartsWith("Error:", StringComparison.Ordinal), $"Expected file content but got: {result[..Math.Min(result.Length, 200)]}");
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void ExtractStructure_RulesDoc_ContainsLinks()
    {
        var content = _fileTools.ReadFileContent("context/RULES.md");

        var structure = FileTools.ExtractStructure(content);

        Assert.Contains("Headings", structure);
    }
}
