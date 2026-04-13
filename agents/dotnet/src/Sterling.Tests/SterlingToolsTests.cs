using Agent.SDK.Tools;
using Sterling.Tools;

namespace Sterling.Tests;

public sealed class SterlingToolsTests : IDisposable
{
    private readonly string _root;
    private readonly SterlingTools _tools;

    public SterlingToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"sterling-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var fileTools = new FileTools(_root);
        var qualityTools = new QualityTools(fileTools);
        _tools = new SterlingTools(fileTools, qualityTools);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ── ListSourceFiles ────────────────────────────────────────────

    [Fact]
    public void ListSourceFiles_FindsCsFiles()
    {
        WriteFile("Foo.cs", "class Foo {}");
        WriteFile("Bar.cs", "class Bar {}");
        WriteFile("Sub/Baz.cs", "class Baz {}");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("Found 3 C# files:", result, StringComparison.Ordinal);
        Assert.Contains("Foo.cs", result, StringComparison.Ordinal);
        Assert.Contains("Bar.cs", result, StringComparison.Ordinal);
        Assert.Contains("Sub/Baz.cs", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListSourceFiles_ExcludesBinAndObj()
    {
        WriteFile("Foo.cs", "class Foo {}");
        WriteFile("bin/Debug/Auto.cs", "class Auto {}");
        WriteFile("obj/Release/Gen.cs", "class Gen {}");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("Found 1 C# files:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("bin/", result, StringComparison.Ordinal);
        Assert.DoesNotContain("obj/", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListSourceFiles_EmptyDirectory_ReturnsMessage()
    {
        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("No C# source files found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListSourceFiles_PathOutsideRoot_ReturnsError()
    {
        var result = _tools.ListSourceFiles("C:\\Windows\\System32");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── AnalyzeFile ────────────────────────────────────────────────

    [Fact]
    public void AnalyzeFile_ValidFile_ReturnsMetrics()
    {
        var code = """
            namespace TestNs;

            public class Calc
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;
        WriteFile("Calc.cs", code);

        var result = _tools.AnalyzeFile(Path.Combine(_root, "Calc.cs"));

        Assert.Contains("Health Grade:", result, StringComparison.Ordinal);
        Assert.Contains("Add", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFile_NonexistentFile_ReturnsError()
    {
        var result = _tools.AnalyzeFile(Path.Combine(_root, "Missing.cs"));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── ReadFile ───────────────────────────────────────────────────

    [Fact]
    public void ReadFile_ValidFile_ReturnsContent()
    {
        WriteFile("Hello.cs", "// hello world");

        var result = _tools.ReadFile(Path.Combine(_root, "Hello.cs"));

        Assert.Contains("// hello world", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFile_PathTraversal_ReturnsError()
    {
        var result = _tools.ReadFile(Path.Combine(_root, "../../etc/passwd"));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── WriteReport ────────────────────────────────────────────────

    [Fact]
    public void WriteReport_CreatesFile()
    {
        var reportPath = Path.Combine(_root, "QUALITY.md");
        var content = "# Quality Report\n\nAll clear.";

        var result = _tools.WriteReport(reportPath, content);

        Assert.True(File.Exists(reportPath));
        Assert.Equal(content, File.ReadAllText(reportPath));
        Assert.Contains("Wrote", result, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_PathOutsideRoot_ReturnsError()
    {
        var result = _tools.WriteReport("C:\\Windows\\evil.md", "bad");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }
}
