using System.ComponentModel;
using System.Text;
using LibGit2Sharp;

namespace Agent.SDK.Tools;

/// <summary>
/// Git history analysis tools using LibGit2Sharp. Reads commit logs, diffs, and statistics.
/// </summary>
public class GitTools
{
    private readonly FileTools _fileTools;

    public GitTools(FileTools fileTools)
    {
        _fileTools = fileTools;
    }

    [Description("Reads git commit log for a repository. Returns SHA, author, date, message, and files changed count. Optionally filters by date range.")]
    public string GetGitLog(
        [Description("Absolute path to the git repository root")] string directoryPath,
        [Description("Only include commits after this date (ISO format, e.g. 2026-01-01). Empty string for no filter.")] string since = "",
        [Description("Only include commits before this date (ISO format, e.g. 2026-12-31). Empty string for no filter.")] string until = "",
        [Description("Maximum number of commits to return. Default 100.")] int maxCount = 100)
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        try
        {
            var repoPath = Repository.Discover(resolved);
            if (repoPath is null)
            {
                return $"Error: no git repository found at or above '{resolved}'.";
            }

            using var repo = new Repository(repoPath);

            DateTimeOffset? sinceDate = null;
            DateTimeOffset? untilDate = null;

            if (since is { Length: > 0 } && DateTimeOffset.TryParse(since, out var s))
            {
                sinceDate = s;
            }

            if (until is { Length: > 0 } && DateTimeOffset.TryParse(until, out var u))
            {
                untilDate = u;
            }

            var commits = repo.Commits
                .Where(c =>
                {
                    if (sinceDate.HasValue && c.Author.When < sinceDate.Value) { return false; }
                    if (untilDate.HasValue && c.Author.When > untilDate.Value) { return false; }
                    return true;
                })
                .Take(maxCount)
                .ToList();

            if (commits.Count == 0)
            {
                return "No commits found in the specified range.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## Git Log ({commits.Count} commits)");
            sb.AppendLine();

            foreach (var commit in commits)
            {
                var sha = commit.Sha[..7];
                var author = commit.Author.Name;
                var date = commit.Author.When.ToString("yyyy-MM-dd HH:mm");
                var message = commit.MessageShort;

                // Count changed files
                var filesChanged = 0;
                if (commit.Parents.Any())
                {
                    var parent = commit.Parents.First();
                    var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                    filesChanged = diff.Count;
                }

                sb.AppendLine($"- `{sha}` {date} [{author}] {message} ({filesChanged} files)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading git log: {ex.Message}";
        }
    }

    [Description("Gets the diff for a specific commit. Returns files added, modified, and deleted with line change counts.")]
    public string GetGitDiff(
        [Description("Absolute path to the git repository root")] string directoryPath,
        [Description("SHA of the commit to diff (short or full)")] string commitSha)
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        try
        {
            var repoPath = Repository.Discover(resolved);
            if (repoPath is null)
            {
                return $"Error: no git repository found at or above '{resolved}'.";
            }

            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(commitSha);

            if (commit is null)
            {
                return $"Error: commit '{commitSha}' not found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## Diff for {commit.Sha[..7]}: {commit.MessageShort}");
            sb.AppendLine($"Author: {commit.Author.Name} ({commit.Author.When:yyyy-MM-dd})");
            sb.AppendLine();

            if (!commit.Parents.Any())
            {
                sb.AppendLine("(Initial commit - no parent to diff against)");
                return sb.ToString();
            }

            var parent = commit.Parents.First();
            var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

            var added = diff.Added.ToList();
            var modified = diff.Modified.ToList();
            var deleted = diff.Deleted.ToList();
            var renamed = diff.Renamed.ToList();

            if (added.Count > 0)
            {
                sb.AppendLine($"### Added ({added.Count})");
                foreach (var f in added.Take(30))
                {
                    sb.AppendLine($"- {f.Path}");
                }
                if (added.Count > 30) { sb.AppendLine($"... and {added.Count - 30} more"); }
                sb.AppendLine();
            }

            if (modified.Count > 0)
            {
                sb.AppendLine($"### Modified ({modified.Count})");
                foreach (var f in modified.Take(30))
                {
                    sb.AppendLine($"- {f.Path}");
                }
                if (modified.Count > 30) { sb.AppendLine($"... and {modified.Count - 30} more"); }
                sb.AppendLine();
            }

            if (deleted.Count > 0)
            {
                sb.AppendLine($"### Deleted ({deleted.Count})");
                foreach (var f in deleted.Take(20))
                {
                    sb.AppendLine($"- {f.Path}");
                }
                if (deleted.Count > 20) { sb.AppendLine($"... and {deleted.Count - 20} more"); }
                sb.AppendLine();
            }

            if (renamed.Count > 0)
            {
                sb.AppendLine($"### Renamed ({renamed.Count})");
                foreach (var f in renamed.Take(10))
                {
                    sb.AppendLine($"- {f.OldPath} → {f.Path}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading git diff: {ex.Message}";
        }
    }

    [Description("Aggregates git stats: commits per day, files most frequently changed, authors, and churn hotspots. Optionally filters by date range.")]
    public string GetGitStats(
        [Description("Absolute path to the git repository root")] string directoryPath,
        [Description("Only include commits after this date (ISO format). Empty string for no filter.")] string since = "",
        [Description("Only include commits before this date (ISO format). Empty string for no filter.")] string until = "")
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        try
        {
            var repoPath = Repository.Discover(resolved);
            if (repoPath is null)
            {
                return $"Error: no git repository found at or above '{resolved}'.";
            }

            using var repo = new Repository(repoPath);

            DateTimeOffset? sinceDate = null;
            DateTimeOffset? untilDate = null;

            if (since is { Length: > 0 } && DateTimeOffset.TryParse(since, out var s))
            {
                sinceDate = s;
            }

            if (until is { Length: > 0 } && DateTimeOffset.TryParse(until, out var u))
            {
                untilDate = u;
            }

            var commits = repo.Commits
                .Where(c =>
                {
                    if (sinceDate.HasValue && c.Author.When < sinceDate.Value) { return false; }
                    if (untilDate.HasValue && c.Author.When > untilDate.Value) { return false; }
                    return true;
                })
                .Take(500) // Limit for performance
                .ToList();

            if (commits.Count == 0)
            {
                return "No commits found in the specified range.";
            }

            // Commits per day
            var perDay = commits
                .GroupBy(c => c.Author.When.Date)
                .OrderByDescending(g => g.Key)
                .Take(30)
                .ToList();

            // Authors
            var authors = commits
                .GroupBy(c => c.Author.Name)
                .OrderByDescending(g => g.Count())
                .ToList();

            // File churn
            var fileChurn = new Dictionary<string, int>();
            foreach (var commit in commits.Take(200))
            {
                if (!commit.Parents.Any()) { continue; }
                var parent = commit.Parents.First();
                var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                foreach (var change in diff)
                {
                    fileChurn[change.Path] = fileChurn.GetValueOrDefault(change.Path) + 1;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## Git Stats ({commits.Count} commits analyzed)");
            sb.AppendLine();

            // Date range
            var earliest = commits.Min(c => c.Author.When);
            var latest = commits.Max(c => c.Author.When);
            sb.AppendLine($"Date range: {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}");
            sb.AppendLine();

            // Authors
            sb.AppendLine("### Authors");
            foreach (var a in authors)
            {
                sb.AppendLine($"- {a.Key}: {a.Count()} commits");
            }
            sb.AppendLine();

            // Commits per day (recent)
            sb.AppendLine("### Recent Activity");
            foreach (var day in perDay.Take(14))
            {
                sb.AppendLine($"- {day.Key:yyyy-MM-dd}: {day.Count()} commits");
            }
            sb.AppendLine();

            // Churn hotspots
            var hotspots = fileChurn.OrderByDescending(kv => kv.Value).Take(15).ToList();
            if (hotspots.Count > 0)
            {
                sb.AppendLine("### Churn Hotspots (most frequently changed files)");
                foreach (var (file, count) in hotspots)
                {
                    sb.AppendLine($"- {file}: {count} changes");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error computing git stats: {ex.Message}";
        }
    }

    [Description("Checks if a journal entry already exists for a given date. Returns true/false and the path if it exists.")]
    public string CheckJournalExists(
        [Description("Absolute path to the journal output directory")] string directoryPath,
        [Description("Date to check in YYYY-MM-DD format")] string date)
    {
        var resolved = _fileTools.ResolveSafePath(directoryPath);
        if (resolved is null)
        {
            return $"Error: path '{directoryPath}' is outside the allowed root directory.";
        }

        if (!Directory.Exists(resolved))
        {
            return $"false (directory does not exist yet)";
        }

        // Check for files matching the date pattern
        var pattern = $"{date}_*.md";
        var matches = Directory.EnumerateFiles(resolved, pattern).ToList();

        if (matches.Count > 0)
        {
            var paths = matches.Select(m => Path.GetRelativePath(_fileTools.RootDirectory, m).Replace('\\', '/'));
            return $"true ({string.Join(", ", paths)})";
        }

        return "false";
    }
}
