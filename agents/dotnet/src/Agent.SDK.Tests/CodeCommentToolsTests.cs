using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

[Collection("FileTools")]
public sealed class CodeCommentToolsTests : IDisposable
{
    private readonly string _root;
    private readonly FileTools _fileTools;
    private readonly CodeCommentTools _tools;

    public CodeCommentToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cct-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _fileTools = new FileTools(_root);
        _tools = new CodeCommentTools(_fileTools);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── ListSourceFiles ──

    [Fact]
    public void ListSourceFiles_FindsCSharpFiles()
    {
        WriteFile("src/Program.cs", "Console.WriteLine();");
        WriteFile("src/Helper.cs", "// helper");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("src/Program.cs [csharp]", result);
        Assert.Contains("src/Helper.cs [csharp]", result);
        Assert.Contains("Found 2 source files", result);
    }

    [Fact]
    public void ListSourceFiles_FindsPythonFiles()
    {
        WriteFile("scripts/run.py", "print('hello')");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("scripts/run.py [python]", result);
    }

    [Fact]
    public void ListSourceFiles_FindsSqlFiles()
    {
        WriteFile("db/init.sql", "SELECT 1;");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("db/init.sql [sql]", result);
    }

    [Fact]
    public void ListSourceFiles_FindsMultipleLanguages()
    {
        WriteFile("app.cs", "class C {}");
        WriteFile("app.py", "pass");
        WriteFile("app.sql", "SELECT 1");
        WriteFile("app.ts", "const x = 1;");
        WriteFile("app.sh", "echo hi");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("Found 5 source files", result);
        Assert.Contains("[csharp]", result);
        Assert.Contains("[python]", result);
        Assert.Contains("[sql]", result);
        Assert.Contains("[typescript]", result);
        Assert.Contains("[shell]", result);
    }

    [Fact]
    public void ListSourceFiles_IgnoresNonSourceFiles()
    {
        WriteFile("readme.md", "# Readme");
        WriteFile("data.json", "{}");
        WriteFile("style.css", "body {}");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("No source files found", result);
    }

    [Fact]
    public void ListSourceFiles_FiltersByExtension()
    {
        WriteFile("app.cs", "class C {}");
        WriteFile("app.py", "pass");
        WriteFile("app.sql", "SELECT 1");

        var result = _tools.ListSourceFiles(_root, ".cs,.sql");

        Assert.Contains("app.cs", result);
        Assert.Contains("app.sql", result);
        Assert.DoesNotContain("app.py", result);
    }

    [Fact]
    public void ListSourceFiles_FilterExtensionWithoutDot()
    {
        WriteFile("app.cs", "class C {}");
        WriteFile("app.py", "pass");

        var result = _tools.ListSourceFiles(_root, "cs");

        Assert.Contains("app.cs", result);
        Assert.DoesNotContain("app.py", result);
    }

    [Fact]
    public void ListSourceFiles_EmptyDirectory_ReportsNone()
    {
        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("No source files found", result);
    }

    [Fact]
    public void ListSourceFiles_NonexistentDirectory_ReturnsError()
    {
        var result = _tools.ListSourceFiles(Path.Combine(_root, "nope"));

        Assert.Contains("does not exist", result);
    }

    [Fact]
    public void ListSourceFiles_OutsideRoot_ReturnsError()
    {
        var result = _tools.ListSourceFiles(Path.Combine(_root, "..", ".."));

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ListSourceFiles_RecursiveDiscovery()
    {
        WriteFile("a/b/c/deep.cs", "class D {}");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("a/b/c/deep.cs [csharp]", result);
    }

    [Fact]
    public void ListSourceFiles_IncludesLanguageSummary()
    {
        WriteFile("a.cs", "class A {}");
        WriteFile("b.cs", "class B {}");
        WriteFile("c.py", "pass");

        var result = _tools.ListSourceFiles(_root);

        Assert.Contains("csharp: 2", result);
        Assert.Contains("python: 1", result);
    }

    // ── ExtractComments: C# ──

    [Fact]
    public void ExtractComments_CSharp_SingleLineComment()
    {
        WriteFile("test.cs", "// This is a comment\nvar x = 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("Inline Comments", result);
        Assert.Contains("This is a comment", result);
    }

    [Fact]
    public void ExtractComments_CSharp_XmlDocComment()
    {
        WriteFile("test.cs", "/// <summary>My summary</summary>\npublic class Foo {}");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("Doc Comments", result);
        Assert.Contains("<summary>My summary</summary>", result);
    }

    [Fact]
    public void ExtractComments_CSharp_MultiLineBlockComment()
    {
        var code = """
            /* This is a
               multi-line comment */
            var x = 1;
            """;
        WriteFile("test.cs", code);

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("This is a", result);
        Assert.Contains("multi-line comment", result);
    }

    [Fact]
    public void ExtractComments_CSharp_SingleLineBlockComment()
    {
        WriteFile("test.cs", "/* inline block */ var x = 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("Inline Comments", result);
        Assert.Contains("inline block", result);
    }

    [Fact]
    public void ExtractComments_CSharp_TodoDetection()
    {
        WriteFile("test.cs", "// TODO: fix this later\nvar x = 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("TODO/FIXME", result);
        Assert.Contains("TODO: fix this later", result);
    }

    [Fact]
    public void ExtractComments_CSharp_FixmeDetection()
    {
        WriteFile("test.cs", "// FIXME: broken thing\nvar x = 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("TODO/FIXME", result);
        Assert.Contains("FIXME: broken thing", result);
    }

    [Fact]
    public void ExtractComments_CSharp_HackDetection()
    {
        WriteFile("test.cs", "// HACK: workaround\nvar x = 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("TODO/FIXME", result);
        Assert.Contains("HACK: workaround", result);
    }

    [Fact]
    public void ExtractComments_CSharp_NoComments_EmptySections()
    {
        WriteFile("test.cs", "var x = 1;\nvar y = 2;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.DoesNotContain("Doc Comments", result);
        Assert.DoesNotContain("Inline Comments", result);
        Assert.DoesNotContain("TODO/FIXME", result);
    }

    [Fact]
    public void ExtractComments_CSharp_ReportsLineNumbers()
    {
        WriteFile("test.cs", "var x = 1;\n// second line comment\nvar y = 2;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.cs"));

        Assert.Contains("L2:", result);
    }

    [Fact]
    public void ExtractComments_CSharp_IncludesFileHeader()
    {
        WriteFile("src/Foo.cs", "// comment");

        var result = _tools.ExtractComments(Path.Combine(_root, "src", "Foo.cs"));

        Assert.Contains("src/Foo.cs [csharp]", result);
        Assert.Contains("Lines:", result);
    }

    // ── ExtractComments: Python ──

    [Fact]
    public void ExtractComments_Python_HashComment()
    {
        WriteFile("test.py", "# This is a python comment\nx = 1");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.py"));

        Assert.Contains("Inline Comments", result);
        Assert.Contains("This is a python comment", result);
    }

    [Fact]
    public void ExtractComments_Python_InlineHashComment()
    {
        WriteFile("test.py", "x = 1 # inline note\n");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.py"));

        Assert.Contains("inline note", result);
    }

    [Fact]
    public void ExtractComments_Python_Docstring()
    {
        var code = "def foo():\n    \"\"\"This is a docstring\"\"\"\n    pass";
        WriteFile("test.py", code);

        var result = _tools.ExtractComments(Path.Combine(_root, "test.py"));

        Assert.Contains("Doc Comments", result);
        Assert.Contains("This is a docstring", result);
    }

    [Fact]
    public void ExtractComments_Python_TodoInComment()
    {
        WriteFile("test.py", "# TODO: implement this\ndef foo(): pass");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.py"));

        Assert.Contains("TODO/FIXME", result);
        Assert.Contains("TODO: implement this", result);
    }

    // ── ExtractComments: SQL ──

    [Fact]
    public void ExtractComments_Sql_DashDashComment()
    {
        WriteFile("test.sql", "-- Select all users\nSELECT * FROM users;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.sql"));

        Assert.Contains("Inline Comments", result);
        Assert.Contains("Select all users", result);
    }

    [Fact]
    public void ExtractComments_Sql_BlockComment()
    {
        var code = "/* Schema setup */\nCREATE TABLE foo (id INT);";
        WriteFile("test.sql", code);

        var result = _tools.ExtractComments(Path.Combine(_root, "test.sql"));

        Assert.Contains("Doc Comments", result);
        Assert.Contains("Schema setup", result);
    }

    [Fact]
    public void ExtractComments_Sql_TodoInDashComment()
    {
        WriteFile("test.sql", "-- TODO: add index\nSELECT 1;");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.sql"));

        Assert.Contains("TODO/FIXME", result);
        Assert.Contains("TODO: add index", result);
    }

    // ── ExtractComments: Edge cases ──

    [Fact]
    public void ExtractComments_NonexistentFile_ReturnsError()
    {
        var result = _tools.ExtractComments(Path.Combine(_root, "missing.cs"));

        Assert.Contains("does not exist", result);
    }

    [Fact]
    public void ExtractComments_OutsideRoot_ReturnsError()
    {
        var result = _tools.ExtractComments("../../etc/passwd");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ExtractComments_UnsupportedExtension_ReturnsError()
    {
        WriteFile("data.json", "{ \"key\": \"value\" }");

        var result = _tools.ExtractComments(Path.Combine(_root, "data.json"));

        Assert.Contains("not supported", result);
    }

    [Fact]
    public void ExtractComments_RelativePath_Works()
    {
        WriteFile("rel.cs", "// relative comment");

        var result = _tools.ExtractComments("rel.cs");

        Assert.Contains("relative comment", result);
    }

    [Theory]
    [InlineData(".ts", "typescript")]
    [InlineData(".js", "javascript")]
    [InlineData(".go", "go")]
    [InlineData(".rs", "rust")]
    [InlineData(".java", "java")]
    [InlineData(".kt", "kotlin")]
    public void ExtractComments_CStyleLanguages_ExtractsLineComments(string ext, string lang)
    {
        WriteFile($"test{ext}", "// a comment\ncode();");

        var result = _tools.ExtractComments(Path.Combine(_root, $"test{ext}"));

        Assert.Contains($"[{lang}]", result);
        Assert.Contains("a comment", result);
    }

    [Fact]
    public void ExtractComments_ShellScript_ExtractsHashComments()
    {
        WriteFile("test.sh", "#!/bin/bash\n# setup environment\necho hello");

        var result = _tools.ExtractComments(Path.Combine(_root, "test.sh"));

        Assert.Contains("[shell]", result);
        Assert.Contains("setup environment", result);
        // Shebang lines are excluded
        Assert.DoesNotContain("!/bin/bash", result);
    }

    // ── ExtractCodePatterns ──

    [Fact]
    public void ExtractCodePatterns_DetectsDiRegistrations()
    {
        var code = """
            builder.Services.AddSingleton<IFoo, Foo>();
            builder.Services.AddScoped<IBar, Bar>();
            services.AddTransient<IBaz, Baz>();
            """;
        WriteFile("Startup.cs", code);

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("DI Registrations", result);
        Assert.Contains("AddSingleton", result);
        Assert.Contains("AddScoped", result);
        Assert.Contains("AddTransient", result);
    }

    [Fact]
    public void ExtractCodePatterns_DetectsBaseClasses()
    {
        WriteFile("MyController.cs", "public class MyController : ControllerBase {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("Base Classes", result);
        Assert.Contains("ControllerBase", result);
    }

    [Fact]
    public void ExtractCodePatterns_DetectsInterfaces()
    {
        WriteFile("Foo.cs", "public class Foo : IDisposable {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("Interfaces", result);
        Assert.Contains("IDisposable", result);
    }

    [Fact]
    public void ExtractCodePatterns_DetectsAttributes()
    {
        WriteFile("Handler.cs", "[Authorize]\n[HttpGet]\npublic class Handler {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("Attributes", result);
        Assert.Contains("[Authorize]", result);
        Assert.Contains("[HttpGet]", result);
    }

    [Fact]
    public void ExtractCodePatterns_DetectsNamingConventions()
    {
        WriteFile("UserService.cs", "public class UserService {}");
        WriteFile("OrderRepository.cs", "public class OrderRepository {}");
        WriteFile("AuthHandler.cs", "public class AuthHandler {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("Naming Conventions", result);
        Assert.Contains("*Service: 1", result);
        Assert.Contains("*Repository: 1", result);
        Assert.Contains("*Handler: 1", result);
    }

    [Fact]
    public void ExtractCodePatterns_NoCSharpFiles_ReportsNone()
    {
        WriteFile("script.py", "print('hello')");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("No C# files found", result);
    }

    [Fact]
    public void ExtractCodePatterns_EmptyDirectory_ReportsNone()
    {
        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("No C# files found", result);
    }

    [Fact]
    public void ExtractCodePatterns_NonexistentDirectory_ReturnsError()
    {
        var result = _tools.ExtractCodePatterns(Path.Combine(_root, "nope"));

        Assert.Contains("does not exist", result);
    }

    [Fact]
    public void ExtractCodePatterns_OutsideRoot_ReturnsError()
    {
        var result = _tools.ExtractCodePatterns(Path.Combine(_root, "..", ".."));

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ExtractCodePatterns_DistinguishesBaseClassFromInterface()
    {
        WriteFile("Svc.cs", "public class Svc : BackgroundService, IHostedService {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("Base Classes", result);
        Assert.Contains("BackgroundService", result);
        Assert.Contains("Interfaces", result);
        Assert.Contains("IHostedService", result);
    }

    [Fact]
    public void ExtractCodePatterns_CountsDuplicateDiRegistrations()
    {
        var code = """
            builder.Services.AddSingleton<IA, A>();
            builder.Services.AddSingleton<IB, B>();
            """;
        WriteFile("Setup.cs", code);

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("AddSingleton (2x)", result);
    }

    [Fact]
    public void ExtractCodePatterns_ReportsFileCount()
    {
        WriteFile("A.cs", "class A {}");
        WriteFile("B.cs", "class B {}");
        WriteFile("C.cs", "class C {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("3 C# files analyzed", result);
    }

    [Fact]
    public void ExtractCodePatterns_ExcludesCommonAttributes()
    {
        WriteFile("Model.cs", "[Description(\"desc\")]\n[Obsolete]\n[Serializable]\npublic class Model {}");

        var result = _tools.ExtractCodePatterns(_root);

        // Description, Obsolete, and Serializable are filtered out
        Assert.DoesNotContain("[Description]", result);
        Assert.DoesNotContain("[Obsolete]", result);
        Assert.DoesNotContain("[Serializable]", result);
    }

    [Fact]
    public void ExtractCodePatterns_AsyncPattern_DetectsViaInterfaces()
    {
        WriteFile("Worker.cs", "public class Worker : IAsyncDisposable {}");

        var result = _tools.ExtractCodePatterns(_root);

        Assert.Contains("IAsyncDisposable", result);
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
