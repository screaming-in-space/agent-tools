using CrimeSceneInvestigator.Tools;

namespace CrimeSceneInvestigator.Tests;

public sealed class FileToolsTests : IDisposable
{
    private readonly string _root;

    public FileToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"csi-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        FileTools.RootDirectory = _root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── ListMarkdownFiles ──

    [Fact]
    public void ListMarkdownFiles_FindsRecursive()
    {
        WriteFile("README.md", "# Root");
        WriteFile("docs/DESIGN.md", "# Design");
        WriteFile("docs/sub/NOTES.md", "# Notes");
        WriteFile("src/Program.cs", "// not markdown");

        var result = FileTools.ListMarkdownFiles(_root);

        Assert.Contains("README.md", result);
        Assert.Contains("docs/DESIGN.md", result);
        Assert.Contains("docs/sub/NOTES.md", result);
        Assert.DoesNotContain("Program.cs", result);
        Assert.Contains("Found 3 markdown files", result);
    }

    [Fact]
    public void ListMarkdownFiles_EmptyDirectory_ReportsNone()
    {
        var result = FileTools.ListMarkdownFiles(_root);

        Assert.Contains("No markdown files found", result);
    }

    [Fact]
    public void ListMarkdownFiles_OutsideRoot_ReturnsError()
    {
        var result = FileTools.ListMarkdownFiles(Path.Combine(_root, "..", ".."));

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ListMarkdownFiles_NonexistentDirectory_ReturnsError()
    {
        var result = FileTools.ListMarkdownFiles(Path.Combine(_root, "nope"));

        Assert.Contains("does not exist", result);
    }

    // ── ReadFileContent ──

    [Fact]
    public void ReadFileContent_ReturnsContent()
    {
        WriteFile("test.md", "# Hello\n\nWorld");

        var result = FileTools.ReadFileContent("test.md");

        Assert.Contains("# Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void ReadFileContent_AbsolutePath_Works()
    {
        WriteFile("abs.md", "absolute content");

        var result = FileTools.ReadFileContent(Path.Combine(_root, "abs.md"));

        Assert.Contains("absolute content", result);
    }

    [Fact]
    public void ReadFileContent_PathTraversal_ReturnsError()
    {
        var result = FileTools.ReadFileContent("../../etc/passwd");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ReadFileContent_NonexistentFile_ReturnsError()
    {
        var result = FileTools.ReadFileContent("missing.md");

        Assert.Contains("does not exist", result);
    }

    [Fact]
    public void ReadFileContent_LargeFile_Truncates()
    {
        var content = new string('x', 200 * 1024);
        WriteFile("big.md", content);

        var result = FileTools.ReadFileContent("big.md");

        Assert.Contains("Truncated", result);
        Assert.True(result.Length < content.Length);
    }

    // ── ExtractStructure ──

    [Fact]
    public void ExtractStructure_ParsesFrontmatter()
    {
        var content = "---\ntitle: Test\ntags: [a, b]\n---\n# Heading";

        var result = FileTools.ExtractStructure(content);

        Assert.Contains("Frontmatter", result);
        Assert.Contains("title: Test", result);
    }

    [Fact]
    public void ExtractStructure_ParsesHeadings()
    {
        var content = "# H1\n## H2\n### H3\nNot a heading";

        var result = FileTools.ExtractStructure(content);

        Assert.Contains("# H1", result);
        Assert.Contains("## H2", result);
        Assert.Contains("### H3", result);
        Assert.DoesNotContain("Not a heading", result);
    }

    [Fact]
    public void ExtractStructure_ParsesLinks()
    {
        var content = "See [Google](https://google.com) and [Docs](./docs/README.md)";

        var result = FileTools.ExtractStructure(content);

        Assert.Contains("Links", result);
        Assert.Contains("https://google.com", result);
        Assert.Contains("./docs/README.md", result);
    }

    [Fact]
    public void ExtractStructure_DeduplicatesLinks()
    {
        var content = "[A](https://example.com) and [B](https://example.com)";

        var result = FileTools.ExtractStructure(content);

        var count = result.Split("https://example.com").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void ExtractStructure_IncludesStats()
    {
        var content = "one two three\nfour five";

        var result = FileTools.ExtractStructure(content);

        Assert.Contains("Lines: 2", result);
        Assert.Contains("Words: 5", result);
    }

    [Fact]
    public void ExtractStructure_NoFrontmatter_SkipsSection()
    {
        var content = "# Just a heading";

        var result = FileTools.ExtractStructure(content);

        Assert.DoesNotContain("Frontmatter", result);
    }

    // ── WriteOutput ──

    [Fact]
    public void WriteOutput_CreatesFile()
    {
        var result = FileTools.WriteOutput("output.md", "# Output");

        Assert.Contains("Wrote", result);
        Assert.True(File.Exists(Path.Combine(_root, "output.md")));
        Assert.Equal("# Output", File.ReadAllText(Path.Combine(_root, "output.md")));
    }

    [Fact]
    public void WriteOutput_CreatesParentDirectories()
    {
        var result = FileTools.WriteOutput("deep/nested/output.md", "content");

        Assert.Contains("Wrote", result);
        Assert.True(File.Exists(Path.Combine(_root, "deep", "nested", "output.md")));
    }

    [Fact]
    public void WriteOutput_PathTraversal_ReturnsError()
    {
        var result = FileTools.WriteOutput("../../evil.md", "bad");

        Assert.StartsWith("Error:", result);
    }

    // ── Helpers ──

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, content);
    }
}
