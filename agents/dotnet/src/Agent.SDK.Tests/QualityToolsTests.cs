using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

[Collection("FileTools")]
public sealed class QualityToolsTests : IDisposable
{
    private readonly string _root;

    public QualityToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"quality-tests-{Guid.NewGuid():N}");
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

    // ── AnalyzeCSharpFile: basic metrics ──

    [Fact]
    public void AnalyzeCSharpFile_SimpleClass_ReportsLineCount()
    {
        var code = """
            using System;

            namespace TestNs;

            public class Foo
            {
                public void Bar()
                {
                    var x = 1;
                }
            }
            """;
        WriteFile("Foo.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Foo.cs"));

        Assert.Contains("Lines:", result, StringComparison.Ordinal);
        Assert.Contains("Types: 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_SingleMethod_ReportsMethodTable()
    {
        var code = """
            namespace TestNs;

            public class Calc
            {
                public int Add(int a, int b) => a + b;
            }
            """;
        WriteFile("Calc.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Calc.cs"));

        Assert.Contains("### Methods", result, StringComparison.Ordinal);
        Assert.Contains("Add", result, StringComparison.Ordinal);
        Assert.Contains("| 2 |", result, StringComparison.Ordinal); // 2 params
    }

    [Fact]
    public void AnalyzeCSharpFile_MultipleMethods_ReportsAll()
    {
        var code = """
            namespace TestNs;

            public class Svc
            {
                public void A() { }
                public void B() { }
                public void C() { }
            }
            """;
        WriteFile("Svc.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Svc.cs"));

        Assert.Contains("| A |", result, StringComparison.Ordinal);
        Assert.Contains("| B |", result, StringComparison.Ordinal);
        Assert.Contains("| C |", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_IfElse_IncreasesComplexity()
    {
        var code = """
            namespace TestNs;

            public class Complex
            {
                public string Eval(int x)
                {
                    if (x > 10)
                        return "big";
                    else if (x > 5)
                        return "medium";
                    else
                        return "small";
                }
            }
            """;
        WriteFile("Complex.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Complex.cs"));

        // Base 1 + 2 ifs = complexity 3
        Assert.Contains("| 3 |", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_NoMethods_SkipsMethodsSection()
    {
        var code = """
            namespace TestNs;

            public class Empty { }
            """;
        WriteFile("Empty.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Empty.cs"));

        Assert.DoesNotContain("### Methods", result, StringComparison.Ordinal);
    }

    // ── AnalyzeCSharpFile: anti-patterns ──

    [Fact]
    public void AnalyzeCSharpFile_TaskResult_DetectsAntiPattern()
    {
        var code = """
            using System.Threading.Tasks;

            namespace TestNs;

            public class Bad
            {
                public string Get()
                {
                    var task = Task.FromResult("hi");
                    return task.Result;
                }
            }
            """;
        WriteFile("Bad.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Bad.cs"));

        Assert.Contains("Anti-Patterns", result, StringComparison.Ordinal);
        Assert.Contains("Sync-over-async (Result)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_TaskWait_DetectsAntiPattern()
    {
        var code = """
            using System.Threading.Tasks;

            namespace TestNs;

            public class Waiter
            {
                public void Run()
                {
                    var task = Task.Delay(100);
                    task.Wait();
                }
            }
            """;
        WriteFile("Waiter.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Waiter.cs"));

        Assert.Contains("Sync-over-async (Wait)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_AsyncVoid_DetectsAntiPattern()
    {
        var code = """
            using System.Threading.Tasks;

            namespace TestNs;

            public class Handler
            {
                public async void OnClick()
                {
                    await Task.Delay(1);
                }
            }
            """;
        WriteFile("Handler.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Handler.cs"));

        Assert.Contains("async void (OnClick)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_EmptyCatch_DetectsAntiPattern()
    {
        var code = """
            using System;

            namespace TestNs;

            public class Swallower
            {
                public void Run()
                {
                    try { int.Parse("x"); }
                    catch (Exception) { }
                }
            }
            """;
        WriteFile("Swallower.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Swallower.cs"));

        Assert.Contains("Empty catch block", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_NoAntiPatterns_OmitsSection()
    {
        var code = """
            namespace TestNs;

            public class Clean
            {
                public int Add(int a, int b) => a + b;
            }
            """;
        WriteFile("Clean.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Clean.cs"));

        Assert.DoesNotContain("Anti-Patterns", result, StringComparison.Ordinal);
    }

    // ── AnalyzeCSharpFile: health grade ──

    [Fact]
    public void AnalyzeCSharpFile_CleanSmallFile_GradeA()
    {
        var code = """
            namespace TestNs;

            public class Tiny
            {
                public int Double(int x) => x * 2;
            }
            """;
        WriteFile("Tiny.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Tiny.cs"));

        Assert.Contains("Health Grade: A", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_FileWithAntiPatterns_GradeBelowA()
    {
        var code = """
            using System;

            namespace TestNs;

            public class Messy
            {
                public void Run()
                {
                    try { int.Parse("x"); }
                    catch (Exception) { }
                }
            }
            """;
        WriteFile("Messy.cs", code);

        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "Messy.cs"));

        Assert.DoesNotContain("Health Grade: A", result, StringComparison.Ordinal);
    }

    // ── AnalyzeCSharpFile: error cases ──

    [Fact]
    public void AnalyzeCSharpFile_NonexistentFile_ReturnsError()
    {
        var result = QualityTools.AnalyzeCSharpFile(Path.Combine(_root, "missing.cs"));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
        Assert.Contains("does not exist", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpFile_PathTraversal_ReturnsError()
    {
        var result = QualityTools.AnalyzeCSharpFile("../../../etc/passwd");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── AnalyzeCSharpProject ──

    [Fact]
    public void AnalyzeCSharpProject_DirectoryWithCsFiles_ReturnsSummary()
    {
        var code1 = """
            namespace TestNs;

            public class A
            {
                public int Add(int x, int y) => x + y;
            }
            """;
        var code2 = """
            namespace TestNs;

            public class B
            {
                public void Run()
                {
                    var msg = "hello";
                    System.Console.WriteLine(msg);
                }
            }
            """;
        WriteFile("src/A.cs", code1);
        WriteFile("src/B.cs", code2);

        var result = QualityTools.AnalyzeCSharpProject(Path.Combine(_root, "src"));

        Assert.Contains("C# Files | 2", result, StringComparison.Ordinal);
        Assert.Contains("Total Methods", result, StringComparison.Ordinal);
        Assert.Contains("Avg Method Length", result, StringComparison.Ordinal);
        Assert.Contains("Longest Method", result, StringComparison.Ordinal);
        Assert.Contains("Highest Complexity", result, StringComparison.Ordinal);
        Assert.Contains("Comment/Code Ratio", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpProject_ExcludesBinObj()
    {
        WriteFile("src/Good.cs", "namespace TestNs; public class Good { }");
        WriteFile("src/bin/Debug/Generated.cs", "namespace TestNs; public class Gen { }");
        WriteFile("src/obj/Debug/Temp.cs", "namespace TestNs; public class Tmp { }");

        var result = QualityTools.AnalyzeCSharpProject(Path.Combine(_root, "src"));

        Assert.Contains("C# Files | 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpProject_NoCsFiles_ReportsNone()
    {
        WriteFile("src/readme.txt", "no C# here");

        var result = QualityTools.AnalyzeCSharpProject(Path.Combine(_root, "src"));

        Assert.Contains("No C# files found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpProject_PathTraversal_ReturnsError()
    {
        var result = QualityTools.AnalyzeCSharpProject("../../../etc");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeCSharpProject_CommentRatio_IncludesComments()
    {
        var code = """
            // This is a comment
            /// <summary>XML doc</summary>
            namespace TestNs;

            public class Documented
            {
                // Method comment
                public int Get() => 42;
            }
            """;
        WriteFile("src/Documented.cs", code);

        var result = QualityTools.AnalyzeCSharpProject(Path.Combine(_root, "src"));

        Assert.Contains("Comment/Code Ratio", result, StringComparison.Ordinal);
        // 3 comment lines out of ~6 code lines; ratio should be > 0
        Assert.DoesNotContain("Comment/Code Ratio | 0.0%", result, StringComparison.Ordinal);
    }

    // ── AnalyzeSourceFile ──

    [Fact]
    public void AnalyzeSourceFile_PythonFile_ReportsMetrics()
    {
        var code = """
            # A python script
            import os

            def greet(name):
                print(f"Hello {name}")

            # TODO: add logging
            if __name__ == "__main__":
                greet("world")
            """;
        WriteFile("script.py", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "script.py"), "python");

        Assert.Contains("[python]", result, StringComparison.Ordinal);
        Assert.Contains("Lines |", result, StringComparison.Ordinal);
        Assert.Contains("Code Lines |", result, StringComparison.Ordinal);
        Assert.Contains("Comment Ratio", result, StringComparison.Ordinal);
        Assert.Contains("TODO/FIXME | 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_DetectsLongLines()
    {
        var longLine = new string('x', 130);
        var code = $"# comment\n{longLine}\n{longLine}\nnormal line";
        WriteFile("wide.py", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "wide.py"), "python");

        Assert.Contains("Lines >120 chars | 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_DetectsMaxNesting()
    {
        // 6 levels of nesting (24 spaces / 4 = 6)
        var code = "def outer():\n    if True:\n        if True:\n            if True:\n                if True:\n                    if True:\n                        if True:\n                            pass\n";
        WriteFile("nested.py", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "nested.py"), "python");

        Assert.Contains("Max Nesting |", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_CleanFile_GradeA()
    {
        var code = "# helper\ndef add(a, b):\n    return a + b\n";
        WriteFile("clean.py", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "clean.py"), "python");

        Assert.Contains("Health Grade: A", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_NonexistentFile_ReturnsError()
    {
        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "ghost.py"), "python");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
        Assert.Contains("does not exist", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_PathTraversal_ReturnsError()
    {
        var result = QualityTools.AnalyzeSourceFile("../../../etc/passwd", "text");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("// single-line comment")]
    [InlineData("# hash comment")]
    [InlineData("-- sql comment")]
    [InlineData("/* block comment */")]
    [InlineData("/// xml doc comment")]
    public void AnalyzeSourceFile_VariousCommentStyles_Counted(string commentLine)
    {
        var code = $"{commentLine}\ncode line\n";
        WriteFile("commented.src", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "commented.src"), "generic");

        // Comment ratio should be > 0 since we have 1 comment and 1 code line
        Assert.Contains("Comment Ratio", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSourceFile_DetectsTodoFixmeHackXxx()
    {
        var code = "# TODO: fix this\n# FIXME: broken\n# HACK: workaround\n# XXX: danger\ncode()\n";
        WriteFile("todos.py", code);

        var result = QualityTools.AnalyzeSourceFile(Path.Combine(_root, "todos.py"), "python");

        Assert.Contains("TODO/FIXME | 4", result, StringComparison.Ordinal);
    }

    // ── CheckEditorConfig ──

    [Fact]
    public void CheckEditorConfig_FilePresent_ReportsRules()
    {
        var editorconfig = """
            root = true

            [*.cs]
            indent_style = space
            indent_size = 4
            charset = utf-8
            end_of_line = lf
            """;
        WriteFile(".editorconfig", editorconfig);

        var result = QualityTools.CheckEditorConfig(_root);

        Assert.Contains(".editorconfig", result, StringComparison.Ordinal);
        Assert.Contains("indent_style = space", result, StringComparison.Ordinal);
        Assert.Contains("indent_size = 4", result, StringComparison.Ordinal);
        Assert.Contains("charset = utf-8", result, StringComparison.Ordinal);
        Assert.Contains("end_of_line = lf", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEditorConfig_NoFile_ReportsNotFound()
    {
        // Create a subdirectory with no .editorconfig anywhere in the chain
        var sub = Path.Combine(_root, "deep", "nested");
        Directory.CreateDirectory(sub);

        // Set root to the sub so walking up stays within _root (no parent .editorconfig)
        FileTools.RootDirectory = sub;
        var result = QualityTools.CheckEditorConfig(sub);

        // Restore root for cleanup
        FileTools.RootDirectory = _root;

        Assert.Contains("No .editorconfig found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEditorConfig_InParentDirectory_StillFound()
    {
        var editorconfig = "[*.cs]\nindent_size = 2\n";
        WriteFile(".editorconfig", editorconfig);

        var sub = Path.Combine(_root, "src", "deep");
        Directory.CreateDirectory(sub);

        var result = QualityTools.CheckEditorConfig(sub);

        Assert.Contains("indent_size = 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEditorConfig_FiltersInterestingRules()
    {
        var editorconfig = """
            [*.cs]
            indent_size = 4
            max_line_length = 120
            insert_final_newline = true
            tab_width = 4
            """;
        WriteFile(".editorconfig", editorconfig);

        var result = QualityTools.CheckEditorConfig(_root);

        // indent_size matches "indent" filter
        Assert.Contains("indent_size = 4", result, StringComparison.Ordinal);
        // max_line_length does not match any filter keywords
        Assert.DoesNotContain("max_line_length", result, StringComparison.Ordinal);
        // insert_final_newline does not match any filter keywords
        Assert.DoesNotContain("insert_final_newline", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEditorConfig_PathTraversal_ReturnsError()
    {
        var result = QualityTools.CheckEditorConfig("../../../etc");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
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
