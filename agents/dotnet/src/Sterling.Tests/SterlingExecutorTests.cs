namespace Sterling.Tests;

public class SterlingExecutorTests
{
    [Fact]
    public void SterlingRequest_RoundTrips()
    {
        var request = new SterlingRequest("/code/myapp", "/code/myapp/QUALITY.md");

        Assert.Equal("/code/myapp", request.TargetPath);
        Assert.Equal("/code/myapp/QUALITY.md", request.OutputPath);
    }

    [Fact]
    public void SterlingRequest_SupportsDeconstruction()
    {
        var request = new SterlingRequest("/src", "/out/QUALITY.md");

        var (targetPath, outputPath) = request;

        Assert.Equal("/src", targetPath);
        Assert.Equal("/out/QUALITY.md", outputPath);
    }
}
