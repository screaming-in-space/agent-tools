namespace ContextCartographer.Tests;

public sealed class SystemPromptTests
{
    // ── Build ──

    [Fact]
    public void Build_ContainsTargetPath()
    {
        var prompt = SystemPrompt.Build("/some/directory", "/some/directory/CONTEXT.md");

        Assert.Contains("/some/directory", prompt);
    }

    [Fact]
    public void Build_ContainsOutputPath()
    {
        var prompt = SystemPrompt.Build("/root", "/root/OUT.md");

        Assert.Contains("/root/OUT.md", prompt);
    }

    [Fact]
    public void Build_ContainsToolNames()
    {
        var prompt = SystemPrompt.Build("/root", "/root/CONTEXT.md");

        Assert.Contains("ListMarkdownFiles", prompt);
        Assert.Contains("ReadFileContent", prompt);
        Assert.Contains("ExtractStructure", prompt);
        Assert.Contains("WriteOutput", prompt);
    }

    [Fact]
    public void Build_ContainsOutputFormatSpec()
    {
        var prompt = SystemPrompt.Build("/root", "/root/CONTEXT.md");

        Assert.Contains("# Context Map", prompt);
        Assert.Contains("## Overview", prompt);
        Assert.Contains("## Themes", prompt);
        Assert.Contains("## Reading Order", prompt);
    }
}
