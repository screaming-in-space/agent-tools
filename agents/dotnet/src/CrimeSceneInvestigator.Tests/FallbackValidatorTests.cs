namespace CrimeSceneInvestigator.Tests;

public class FallbackValidatorTests
{
    [Fact]
    public void IsSubstantiveMarkdown_WithMarkdownHeading_ReturnsTrue()
    {
        var content = "# Context Map\n\n> Source: agents/dotnet\n> Files: 3 markdown files\n\n## Overview\n\nThis is a valid scanner output.";
        Assert.True(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Fact]
    public void IsSubstantiveMarkdown_WithMarkdownList_ReturnsTrue()
    {
        var content = "Found the following patterns:\n- DI registration via AddScoped\n- Base class AgentBase\n- Interface IAgentOutput";
        Assert.True(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Fact]
    public void IsSubstantiveMarkdown_WithMarkdownTable_ReturnsTrue()
    {
        var content = "| Project | Purpose | Type |\n|---------|---------|------|\n| Agent.SDK | Core library | Library |";
        Assert.True(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Theory]
    [InlineData("I'm ready to help you analyze your source code files!")]
    [InlineData("I am an AI assistant. How can I help you today?")]
    [InlineData("Sure, I can help with that! Let me analyze the files.")]
    [InlineData("Hello! I'm ready to help you with markdown files.")]
    [InlineData("Let me help you with your code analysis needs.")]
    [InlineData("Here's what I can do for you:\n- List files\n- Read content")]
    [InlineData("How can I assist you today? I have access to tools.")]
    [InlineData("Of course! I'd be happy to help with that.\n# Heading")]
    public void IsSubstantiveMarkdown_WithChatbotPreamble_ReturnsFalse(string content)
    {
        Assert.False(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short text")]
    [InlineData("This is under fifty characters.")]
    public void IsSubstantiveMarkdown_WithShortContent_ReturnsFalse(string content)
    {
        Assert.False(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Fact]
    public void IsSubstantiveMarkdown_WithNoMarkdownStructure_ReturnsFalse()
    {
        var content = "This is a long text that has no markdown structural elements at all. It just keeps going and going without any headings, lists, or tables to be found anywhere in the text.";
        Assert.False(AgentInCommand.IsSubstantiveMarkdown(content));
    }

    [Fact]
    public void IsSubstantiveMarkdown_WithRealRulesOutput_ReturnsTrue()
    {
        var content = """
            # Coding Rules - CrimeSceneInvestigator

            Shared rules for all AI agents and tools.

            ---

            ## Design Principles

            - **Async all the way.** No .Result, .Wait().
            - **KISS.** Simplest correct solution.
            """;
        Assert.True(AgentInCommand.IsSubstantiveMarkdown(content));
    }
}
