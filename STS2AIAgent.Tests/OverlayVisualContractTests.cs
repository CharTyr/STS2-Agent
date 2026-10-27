namespace STS2AIAgent.Tests;

/// <summary>
/// The overlay's visual rules, pinned where a screenshot cannot pin them.
/// </summary>
/// <remarks>
/// Written for the 2026-10-27 pass on "the UI looks cheap". The causes were in the source, not in
/// any one colour: Godot's own grey scroll bars, popups and check boxes inside a themed panel, a
/// window-sized drop shadow on the title bar and every help panel, emoji status marks drawn from a
/// fallback font, and the accent on every heading and tab. Each rule below is one of those, so a
/// change that brings one back fails here instead of in the next live pass.
/// </remarks>
internal static class OverlayVisualContractTests
{
    private const string Host = "STS2AIAgent/Ui/AgentOverlayHost.cs";
    private const string Theme = "STS2AIAgent/Ui/OverlayGodotTheme.cs";

    /// <summary>
    /// The host carries a Godot theme built from the palette, and a theme switch rebuilds it. The
    /// theme covers the parts the factory never styled.
    /// </summary>
    public static void HostCarriesAPaletteTheme()
    {
        var build = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Host), "Build");
        Assert.Contains("host.Theme = OverlayGodotTheme.Build();", build);
        var useTheme = build.IndexOf("UiFactory.UseTheme(startupSettings.OverlayTheme)", StringComparison.Ordinal);
        Assert.True(useTheme >= 0 && useTheme < build.IndexOf("host.Theme = OverlayGodotTheme.Build();", StringComparison.Ordinal),
            "The Godot theme is built from the palette, so the palette has to be selected first.");

        var settings = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Settings.cs");
        var apply = AgentSourceFixture.MethodBody(settings, "ApplyThemeChoice");
        Assert.True(
            apply.IndexOf("UiFactory.UseTheme(SelectedTheme);", StringComparison.Ordinal)
            < apply.IndexOf("_host.Theme = OverlayGodotTheme.Build();", StringComparison.Ordinal),
            "A theme switch has to rebuild the host's Godot theme after switching the palette.");

        var theme = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Theme), "Build");
        foreach (var item in new[]
                 {
                     "theme.SetStylebox(\"grabber\", type,",
                     "\"PopupMenu\",",
                     "\"TooltipPanel\",",
                     "theme.SetIcon(\"checked\", \"CheckBox\",",
                     "theme.SetIcon(\"unchecked\", \"CheckBox\",",
                     "theme.SetFont(\"font\", type, Regular);",
                     "DefaultFont = Regular"
                 })
        {
            Assert.Contains(item, theme);
        }
    }

    /// <summary>
    /// Nothing inside the window casts a shadow. Cards and help panels are separated by fill, and the
    /// title bar is part of the window rather than a second framed panel inside it.
    /// </summary>
    public static void NothingInsideTheWindowCastsAShadow()
    {
        var factory = AgentSourceFixture.ReadUiFactory();
        Assert.False(AgentSourceFixture.MethodBody(factory, "CardStyle").Contains("shadowSize", StringComparison.Ordinal),
            "Cards cast a shadow again.");
        Assert.False(AgentSourceFixture.MethodBody(factory, "InsetStyle").Contains("shadowSize", StringComparison.Ordinal),
            "Wells cast a shadow again.");

        var help = AgentSourceFixture.Read("STS2AIAgent/Ui/HelpCard.cs");
        Assert.Contains("UiFactory.SurfaceRole.Inset", help);
        Assert.False(help.Contains("SurfaceRole.ChromeRaised", StringComparison.Ordinal),
            "The help panel uses the window's style again, shadow included.");

        var header = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Host), "BuildHeader");
        Assert.Contains("UiFactory.SurfaceRole.Bare", header);
    }

    /// <summary>
    /// Status lines carry their tone in colour, not in emoji or shape glyphs, which the game draws
    /// from a fallback font at another size and baseline.
    /// </summary>
    public static void StatusTextCarriesNoGlyphs()
    {
        foreach (var path in new[]
                 {
                     "STS2AIAgent/Ui/AgentOverlayHost.Setup.cs",
                     "STS2AIAgent/Ui/AgentOverlayHost.Pages.cs",
                     "STS2AIAgent/Ui/AgentOverlayHost.Session.cs",
                     Host,
                     "STS2AIAgent/Localization/Loc.Strings.Ui.cs"
                 })
        {
            var source = AgentSourceFixture.Read(path);
            foreach (var glyph in new[] { "✅", "⚪", "⚠️", "● ", "○ ", "▶" })
            {
                Assert.False(source.Contains(glyph, StringComparison.Ordinal), $"{path} draws a status with \"{glyph}\" again.");
            }
        }

        var setup = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Setup.cs");
        var badge = AgentSourceFixture.MethodBody(setup, "ModelTestBadge");
        Assert.Contains("UiFactory.ButtonKind.Primary)", badge);
        Assert.Contains("UiFactory.ButtonKind.Danger)", badge);
        Assert.Contains("UiFactory.SetTone(badge, verdict.Tone);", AgentSourceFixture.MethodBody(setup, "ShowBadge"));
    }

    /// <summary>
    /// A toned label and a pill keep their tone through a theme switch, and a rebuilt pill is
    /// repainted on the next refresh rather than skipped by the tone guard.
    /// </summary>
    public static void TonesSurviveRepaintAndRebuild()
    {
        var factory = AgentSourceFixture.ReadUiFactory();
        var color = AgentSourceFixture.MethodBody(factory, "ApplyLabelColor");
        Assert.True(color.IndexOf("MetaTone", StringComparison.Ordinal) < color.IndexOf("MetaAccent", StringComparison.Ordinal),
            "The repaint resets a toned label to plain text before checking its tone.");
        Assert.Contains("pill.SetMeta(MetaTint, (int)tone);", AgentSourceFixture.MethodBody(factory, "Retone"));

        var pages = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Pages.cs");
        Assert.Contains("UiFactory.Retone(container, tone);", AgentSourceFixture.MethodBody(pages, "ApplyTone"));
        Assert.Contains("_teammateLiveTone = null;", AgentSourceFixture.MethodBody(pages, "BuildCoopSection"));
        Assert.Contains("_headerStatusTone = null;", AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Host), "BuildHeader"));
    }

    /// <summary>
    /// A read-only editor draws the field style, not the engine's grey box.
    /// </summary>
    public static void ReadOnlyEditorsUseTheFieldStyle()
    {
        var factory = AgentSourceFixture.ReadUiFactory();
        Assert.Contains("edit.AddThemeStyleboxOverride(\"read_only\", FieldStyle(focused: false));", AgentSourceFixture.MethodBody(factory, "ApplyMultilineTheme"));
        Assert.Contains("edit.AddThemeStyleboxOverride(\"read_only\", FieldStyle(focused: false));", AgentSourceFixture.MethodBody(factory, "ApplyLineTheme"));
    }
}
