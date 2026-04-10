using Agent.SDK.Tools;

namespace Agent.SDK.Tests;

/// <summary>
/// Integration tests for <see cref="GitTools"/> that run against the real git repository.
/// Tests skip gracefully if the repo root cannot be found.
/// </summary>
[Collection("FileTools")]
public sealed class GitToolsTests
{
    private readonly string? _repoRoot;
    private readonly GitTools? _tools;

    public GitToolsTests()
    {
        // Walk up from bin output to find the repo root (.git directory)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        _repoRoot = dir?.FullName;
        if (_repoRoot is not null)
        {
            var fileTools = new FileTools(_repoRoot);
            _tools = new GitTools(fileTools);
        }
    }

    // ── GetGitLog ──

    [Fact]
    public void GetGitLog_ReturnsFormattedLog_WithCommitHashes()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitLog(_repoRoot, maxCount: 10);

        Assert.Contains("Git Log", result, StringComparison.Ordinal);
        Assert.Contains("commits", result, StringComparison.Ordinal);
        // Commit lines have the format: - `sha` date [author] message (N files)
        Assert.Contains("`", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitLog_WithMaxCount_LimitsResults()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitLog(_repoRoot, maxCount: 3);

        Assert.Contains("Git Log", result, StringComparison.Ordinal);
        // Count the commit lines (each starts with "- `")
        var commitLines = result.Split('\n')
            .Count(line => line.TrimStart().StartsWith("- `", StringComparison.Ordinal));
        Assert.True(commitLines <= 3, $"Expected at most 3 commit lines but got {commitLines}");
        Assert.True(commitLines > 0, "Expected at least 1 commit line");
    }

    [Fact]
    public void GetGitLog_WithFutureSince_ReturnsNoCommits()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitLog(_repoRoot, since: "2099-01-01");

        Assert.Contains("No commits found", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitLog_OutsideRoot_ReturnsError()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitLog(Path.Combine(_repoRoot, "..", ".."));

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── GetGitStats ──

    [Fact]
    public void GetGitStats_ReturnsStatistics_WithAuthorsAndActivity()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitStats(_repoRoot);

        Assert.Contains("Git Stats", result, StringComparison.Ordinal);
        Assert.Contains("commits analyzed", result, StringComparison.Ordinal);
        Assert.Contains("Authors", result, StringComparison.Ordinal);
        Assert.Contains("Recent Activity", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitStats_ContainsDateRange()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitStats(_repoRoot);

        Assert.Contains("Date range:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitStats_ContainsChurnHotspots()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitStats(_repoRoot);

        Assert.Contains("Churn Hotspots", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitStats_WithFutureSince_ReturnsNoCommits()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitStats(_repoRoot, since: "2099-01-01");

        Assert.Contains("No commits found", result, StringComparison.Ordinal);
    }

    // ── GetGitDiff ──

    [Fact]
    public void GetGitDiff_ValidCommit_ReturnsDiffInfo()
    {
        if (_repoRoot is null) return;

        // Get the latest commit SHA from the log
        var log = _tools!.GetGitLog(_repoRoot, maxCount: 5);
        var sha = ExtractFirstSha(log);
        if (sha is null) return;

        var result = _tools!.GetGitDiff(_repoRoot, sha);

        Assert.Contains("Diff for", result, StringComparison.Ordinal);
        Assert.Contains("Author:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitDiff_InvalidSha_ReturnsError()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitDiff(_repoRoot, "0000000000000000000000000000000000000000");

        Assert.Contains("Error", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGitDiff_OutsideRoot_ReturnsError()
    {
        if (_repoRoot is null) return;

        var result = _tools!.GetGitDiff(Path.Combine(_repoRoot, "..", ".."), "abc123");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── CheckJournalExists ──

    [Fact]
    public void CheckJournalExists_NonexistentDirectory_ReturnsFalse()
    {
        if (_repoRoot is null) return;

        var result = _tools!.CheckJournalExists(
            Path.Combine(_repoRoot, "nonexistent-journal-dir"),
            "2026-01-01");

        Assert.Contains("false", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckJournalExists_DirectoryWithNoMatch_ReturnsFalse()
    {
        if (_repoRoot is null) return;

        // Use the repo root itself -- unlikely to have journal files matching this date
        var result = _tools!.CheckJournalExists(_repoRoot, "1999-12-31");

        Assert.Equal("false", result);
    }

    [Fact]
    public void CheckJournalExists_DirectoryWithMatch_ReturnsTrue()
    {
        if (_repoRoot is null) return;

        // Create a temp journal directory with a matching file
        var journalDir = Path.Combine(_repoRoot, $"_test-journal-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(journalDir);
            File.WriteAllText(Path.Combine(journalDir, "2026-04-10_summary.md"), "# Journal");

            var result = _tools!.CheckJournalExists(journalDir, "2026-04-10");

            Assert.StartsWith("true", result, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(journalDir))
            {
                Directory.Delete(journalDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CheckJournalExists_OutsideRoot_ReturnsError()
    {
        if (_repoRoot is null) return;

        var result = _tools!.CheckJournalExists(
            Path.Combine(_repoRoot, "..", ".."),
            "2026-01-01");

        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
    }

    // ── Helpers ──

    /// <summary>
    /// Extracts the first 7-char SHA from a git log output line like "- `abc1234` ...".
    /// </summary>
    private static string? ExtractFirstSha(string logOutput)
    {
        foreach (var line in logOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- `", StringComparison.Ordinal))
            {
                continue;
            }

            var backtickEnd = trimmed.IndexOf('`', 3);
            if (backtickEnd > 3)
            {
                return trimmed[3..backtickEnd];
            }
        }

        return null;
    }
}
