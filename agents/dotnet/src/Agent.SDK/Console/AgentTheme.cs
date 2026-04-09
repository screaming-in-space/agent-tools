using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agent.SDK.Console;

/// <summary>
/// Shared Sci-Fi Kurzgesagt color palette and reusable Spectre.Console helpers.
/// Matches the thread-unsafe website design system. Every agent utility uses
/// these constants instead of hardcoding colors.
/// </summary>
public static class AgentTheme
{
    // ── Thread-Unsafe Kurzgesagt Palette ────────────────────────────────

    /// <summary>Primary cyan - interactive elements, borders, headers. #6ea8e0</summary>
    public static readonly Color Cyan = new(110, 168, 224);

    /// <summary>Active/highlight light blue - current state, selections. #8fc4f0</summary>
    public static readonly Color Sky = new(143, 196, 240);

    /// <summary>Warm accent orange - spinners, active operations, emphasis. #e8a040</summary>
    public static readonly Color Orange = new(232, 160, 64);

    /// <summary>Error/glitch red - failures, warnings. #ff6b6b</summary>
    public static readonly Color Red = new(255, 107, 107);

    /// <summary>Terminal green - success states, checkmarks, completion. #33ff33</summary>
    public static readonly Color Green = new(51, 255, 51);

    /// <summary>Muted space gray - dim text, historical entries. #4a5568</summary>
    public static readonly Color Dim = new(74, 85, 104);

    /// <summary>Secondary text - slightly brighter than Dim. #a0aec0</summary>
    public static readonly Color DimLight = new(160, 174, 192);

    // ── Semantic Styles ────────────────────────────────────────────────

    public static readonly Style Header = new(Cyan, decoration: Decoration.Bold);
    public static readonly Style Label = new(Sky);
    public static readonly Style Value = new(Color.White);
    public static readonly Style Success = new(Green, decoration: Decoration.Bold);
    public static readonly Style Error = new(Red, decoration: Decoration.Bold);
    public static readonly Style Muted = new(Dim);
    public static readonly Style Accent = new(Orange);

    // ── Reusable Component Helpers ─────────────────────────────────────

    /// <summary>Cyan horizontal rule. Pass a title for a labeled divider.</summary>
    public static Rule Divider(string? title = null)
    {
        var rule = title is not null
            ? new Rule($"[{CyanHex}]{Markup.Escape(title)}[/]")
            : new Rule();
        rule.Style = new Style(Cyan);
        return rule;
    }

    /// <summary>Double-bordered panel with a cyan header. Standard summary container.</summary>
    public static Panel SummaryPanel(string header, IRenderable content)
    {
        var panel = new Panel(content)
            .Header($"[{CyanHex} bold]{Markup.Escape(header)}[/]")
            .Border(BoxBorder.Double)
            .BorderStyle(new Style(Cyan))
            .Expand();
        return panel;
    }

    /// <summary>Rounded-bordered panel with a cyan header. Standard live display container.</summary>
    public static Panel LivePanel(string header, IRenderable content)
    {
        var panel = new Panel(content)
            .Header($"[{CyanHex} bold]{Markup.Escape(header)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Dim))
            .Expand();
        return panel;
    }

    /// <summary>Renders label:value pairs in a clean two-column layout.</summary>
    public static Table InfoTable(params (string Label, string Value)[] rows)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Label").PadRight(2))
            .AddColumn(new TableColumn("Value"));

        foreach (var (label, value) in rows)
        {
            table.AddRow(
                new Markup($"[{SkyHex}]{Markup.Escape(label)}[/]"),
                new Markup($"[white]{Markup.Escape(value)}[/]"));
        }

        return table;
    }

    /// <summary>Styled checkmark for completed items.</summary>
    public static string Check => $"[{GreenHex}]✓[/]";

    /// <summary>Styled cross for failed items.</summary>
    public static string Cross => $"[{RedHex}]✗[/]";

    // ── Markup color shorthand (hex strings for inline Spectre markup) ──

    internal const string CyanHex = "rgb(110,168,224)";
    internal const string SkyHex = "rgb(143,196,240)";
    internal const string OrangeHex = "rgb(232,160,64)";
    internal const string RedHex = "rgb(255,107,107)";
    internal const string GreenHex = "rgb(51,255,51)";
    internal const string DimHex = "rgb(74,85,104)";
    internal const string DimLightHex = "rgb(160,174,192)";
}
