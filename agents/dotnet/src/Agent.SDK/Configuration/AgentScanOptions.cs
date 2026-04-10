using Microsoft.Extensions.Configuration;

namespace Agent.SDK.Configuration;

/// <summary>
/// Controls which scanners are enabled. Bound from <c>AgentInCommand</c> section
/// in <c>appsettings.json</c>. Each flag can be overridden at runtime via the
/// <c>--scan</c> CLI option.
/// </summary>
public sealed record AgentScanOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "AgentInCommand";

    /// <summary>Scan markdown files and produce MAP.md.</summary>
    public bool ScanMarkdown { get; init; } = true;

    /// <summary>Scan code comments/patterns and produce RULES.md.</summary>
    public bool ScanCodeComments { get; init; } = true;

    /// <summary>Scan project structure and produce STRUCTURE.md + QUALITY.md.</summary>
    public bool ScanCodePattern { get; init; } = true;

    /// <summary>Scan git history and produce JOURNAL.md + daily entries.</summary>
    public bool ScanGitHistory { get; init; } = true;

    /// <summary>
    /// Resolves scan options from the <c>AgentInCommand</c> section in configuration.
    /// Properties not specified in config retain their defaults (all true).
    /// </summary>
    public static AgentScanOptions Resolve(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return section.Get<AgentScanOptions>() ?? new AgentScanOptions();
    }

    /// <summary>
    /// Creates scan options from a comma-separated CLI override string.
    /// Valid tokens: <c>markdown</c>, <c>comments</c> (or <c>rules</c>), <c>structure</c>,
    /// <c>quality</c>, <c>journal</c>, <c>done</c>. Only listed scanners are enabled; all others disabled.
    /// Returns <c>null</c> if the override string is null or empty (use config defaults).
    /// </summary>
    public static AgentScanOptions? FromCliOverride(string? scanOverride)
    {
        if (string.IsNullOrWhiteSpace(scanOverride))
        {
            return null;
        }

        var tokens = scanOverride
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        return new AgentScanOptions
        {
            ScanMarkdown = tokens.Contains("markdown"),
            ScanCodeComments = tokens.Contains("comments") || tokens.Contains("rules"),
            ScanCodePattern = tokens.Contains("structure") || tokens.Contains("quality"),
            ScanGitHistory = tokens.Contains("journal"),
        };
    }
}
