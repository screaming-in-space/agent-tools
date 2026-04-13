namespace Sterling.Tests;

public class SystemPromptTests
{
    [Fact]
    public void Build_ContainsTargetPath()
    {
        var result = SystemPrompt.Build("/code/myapp", "/code/myapp/QUALITY.md");

        Assert.Contains("/code/myapp", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsOutputPath()
    {
        var result = SystemPrompt.Build("/code/myapp", "/output/QUALITY.md");

        Assert.Contains("/output/QUALITY.md", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsStaffEngineerRole()
    {
        var result = SystemPrompt.Build("/code", "/out/QUALITY.md");

        Assert.Contains("staff engineer", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsJudgmentCategories()
    {
        var result = SystemPrompt.Build("/code", "/out/QUALITY.md");

        Assert.Contains("Naming", result, StringComparison.Ordinal);
        Assert.Contains("Single responsibility", result, StringComparison.Ordinal);
        Assert.Contains("Hidden coupling", result, StringComparison.Ordinal);
    }
}
