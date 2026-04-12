using System.Reflection;
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

    // ── Markup Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="Style"/> to a Spectre markup format string.
    /// Example: <c>FormatStyle(Header)</c> → <c>"rgb(110,168,224) bold"</c>.
    /// </summary>
    public static string FormatStyle(Style style)
    {
        var parts = new List<string>();

        if (style.Foreground != Color.Default)
        {
            parts.Add($"rgb({style.Foreground.R},{style.Foreground.G},{style.Foreground.B})");
        }

        if (style.Decoration.HasFlag(Decoration.Bold))
        {
            parts.Add("bold");
        }

        if (style.Decoration.HasFlag(Decoration.Dim))
        {
            parts.Add("dim");
        }

        if (style.Decoration.HasFlag(Decoration.Italic))
        {
            parts.Add("italic");
        }

        return parts.Count > 0 ? string.Join(' ', parts) : "default";
    }

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
            .BorderStyle(new Style(Cyan));
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

    /// <summary>
    /// Renders the screaming-in-space agent logo as a Spectre Canvas.
    /// Two 3:4:5 right triangles, each missing the medium-length horizontal leg.
    /// Top: hypotenuse ╲ (top-right to center-left) + short vertical leg (going down from top-right).
    /// Bottom: mirror — short vertical leg (going up from bottom-left) + hypotenuse ╱ (to center-right).
    /// Ringed planet centered between the two triangle points.
    /// Compact 8x14 for header display.
    /// </summary>
    public static Canvas Logo()
    {
        var c = new Canvas(8, 14);

        // ── Top triangle ──────────────────────────────
        // Hypotenuse ╲: from (7,0) down-left to (2,5)
        c.SetPixel(7, 0, Cyan);  c.SetPixel(6, 1, Cyan);
        c.SetPixel(5, 2, Cyan);  c.SetPixel(4, 3, Cyan);
        c.SetPixel(3, 4, Cyan);  c.SetPixel(2, 5, Cyan);

        // Short vertical leg: down from (7,0) to (7,2)
        c.SetPixel(7, 1, Cyan);  c.SetPixel(7, 2, Cyan);

        // ── Planet + accretion ring ───────────────────
        c.SetPixel(3, 6, Orange);  c.SetPixel(4, 6, Orange);
        c.SetPixel(3, 7, Orange);  c.SetPixel(4, 7, Orange);
        // Ring extends wider
        c.SetPixel(1, 6, Orange);  c.SetPixel(2, 6, Orange);
        c.SetPixel(5, 6, Orange);  c.SetPixel(6, 6, Orange);
        c.SetPixel(1, 7, Orange);  c.SetPixel(2, 7, Orange);
        c.SetPixel(5, 7, Orange);  c.SetPixel(6, 7, Orange);

        // ── Bottom triangle (mirrored) ────────────────
        // Short vertical leg: up from (0,13) to (0,11)
        c.SetPixel(0, 11, Cyan);  c.SetPixel(0, 12, Cyan);

        // Hypotenuse ╱: from (0,13) up-right to (5,8)
        c.SetPixel(0, 13, Cyan);  c.SetPixel(1, 12, Cyan);
        c.SetPixel(2, 11, Cyan);  c.SetPixel(3, 10, Cyan);
        c.SetPixel(4, 9, Cyan);   c.SetPixel(5, 8, Cyan);

        return c;
    }

    /// <summary>
    /// Reads version and commit SHA from the entry assembly's InformationalVersion.
    /// Format: "0.1.0+abcdef1234" → version "0.1.0", commit "abcdef1".
    /// Falls back to AssemblyVersion if InformationalVersion is unavailable.
    /// </summary>
    public static (string Version, string? Commit) GetVersionInfo()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        var infoVersion = asm?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (infoVersion is not null && infoVersion.Contains('+'))
        {
            var parts = infoVersion.Split('+', 2);
            var commit = parts[1].Length > 7 ? parts[1][..7] : parts[1];
            return (parts[0], commit);
        }

        var version = asm?.GetName().Version?.ToString(3) ?? "0.0.0";
        return (version, null);
    }

    // ── Markup color shorthand (hex strings for inline Spectre markup) ──

    internal const string CyanHex = "rgb(110,168,224)";
    internal const string SkyHex = "rgb(143,196,240)";
    internal const string OrangeHex = "rgb(232,160,64)";
    internal const string RedHex = "rgb(255,107,107)";
    internal const string GreenHex = "rgb(51,255,51)";
    internal const string DimHex = "rgb(74,85,104)";
    internal const string DimLightHex = "rgb(160,174,192)";
}
