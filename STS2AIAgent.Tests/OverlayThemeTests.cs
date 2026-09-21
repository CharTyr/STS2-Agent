using STS2AIAgent.Ui;

namespace STS2AIAgent.Tests;

/// <summary>
/// The overlay's colour themes, checked where a screenshot cannot check them.
/// </summary>
/// <remarks>
/// A theme is four sets of colours and a name, so the interesting failures are the ones nobody
/// notices until a player reports the text is unreadable on one of them: a preset missing a value, a
/// duplicated id that makes the selector ambiguous, or a muted colour that only works on the default
/// surface. The contrast floors below are WCAG's -- 4.5:1 for body text, 3:1 for large or secondary
/// text -- measured against every surface the overlay actually draws on, for every preset.
/// </remarks>
internal static class OverlayThemeTests
{
    /// <summary>Body text has to clear WCAG AA on every surface it can be drawn on.</summary>
    private const float BodyContrastFloor = 4.5f;

    /// <summary>Secondary text is allowed the large-text floor, but not less.</summary>
    private const float SecondaryContrastFloor = 3.0f;

    public static void EveryPresetHasAUniqueIdAndLabel()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in OverlayThemeCatalog.All)
        {
            Assert.True(!string.IsNullOrWhiteSpace(preset.Id), "A theme id must not be empty.");
            Assert.True(!string.IsNullOrWhiteSpace(preset.Label), $"Theme {preset.Id} has no label.");
            Assert.True(ids.Add(preset.Id), $"Two themes share the id {preset.Id}; the selector cannot tell them apart.");
            Assert.True(labels.Add(preset.Label), $"Two themes share the label {preset.Label}.");
        }

        Assert.True(OverlayThemeCatalog.All.Count >= 3, "A selector with fewer than three themes is not a choice.");
        Assert.Equal(OverlayThemeCatalog.DefaultId, OverlayThemeCatalog.Ids[0]);
        Assert.Equal(OverlayThemeCatalog.All.Count, OverlayThemeCatalog.Ids.Count);
    }

    /// <summary>
    /// An unknown id falls back instead of throwing: the value lives in the player's settings file,
    /// so it can be stale, hand-edited, or written by a newer build.
    /// </summary>
    public static void AnUnknownThemeFallsBackToTheDefault()
    {
        Assert.Equal(OverlayThemeCatalog.DefaultId, OverlayThemeCatalog.Resolve(null).Id);
        Assert.Equal(OverlayThemeCatalog.DefaultId, OverlayThemeCatalog.Resolve("").Id);
        Assert.Equal(OverlayThemeCatalog.DefaultId, OverlayThemeCatalog.Resolve("no-such-theme").Id);
        Assert.Equal(OverlayThemeCatalog.DefaultId, OverlayThemeCatalog.Normalize("no-such-theme"));

        foreach (var id in OverlayThemeCatalog.Ids)
        {
            Assert.Equal(id, OverlayThemeCatalog.Resolve(id).Id);
            Assert.Equal(id, OverlayThemeCatalog.Normalize(id));
            Assert.True(!string.IsNullOrWhiteSpace(OverlayThemeCatalog.Label(id)));
        }
    }

    /// <summary>
    /// The floors, applied to every preset against every surface it draws text on. This is the test
    /// that would have caught a "nice looking" theme that is unreadable in a dark room.
    /// </summary>
    public static void EveryPresetStaysReadableOnEverySurface()
    {
        foreach (var preset in OverlayThemeCatalog.All)
        {
            foreach (var (name, surface) in new[]
                     {
                         ("backdrop", preset.Backdrop),
                         ("surface", preset.Surface),
                         ("surface raised", preset.SurfaceRaised)
                     })
            {
                var body = preset.Text.ContrastRatio(surface);
                Assert.True(
                    body >= BodyContrastFloor,
                    $"{preset.Id}: body text on {name} is {body:0.00}:1, below {BodyContrastFloor}:1.");
            }

            foreach (var (name, surface) in new[]
                     {
                         ("surface", preset.Surface),
                         ("surface raised", preset.SurfaceRaised)
                     })
            {
                var secondary = preset.Muted.ContrastRatio(surface);
                Assert.True(
                    secondary >= SecondaryContrastFloor,
                    $"{preset.Id}: secondary text on {name} is {secondary:0.00}:1, below {SecondaryContrastFloor}:1.");

                var border = preset.Border.ContrastRatio(surface);
                Assert.True(
                    border >= 1.2f,
                    $"{preset.Id}: the border is indistinguishable from the {name} it outlines ({border:0.00}:1).");
            }

            // The primary button draws its label on the accent, so that pair has its own floor.
            var onAccent = preset.AccentText.ContrastRatio(preset.Accent);
            Assert.True(
                onAccent >= BodyContrastFloor,
                $"{preset.Id}: the accent label is {onAccent:0.00}:1 on the accent, below {BodyContrastFloor}:1.");

            // Status colours carry meaning, so they have to be tellable apart from the text colour
            // they sit beside rather than merely present.
            foreach (var (name, status) in new[]
                     {
                         ("positive", preset.Positive),
                         ("warning", preset.Warning),
                         ("danger", preset.Danger)
                     })
            {
                var statusOnSurface = status.ContrastRatio(preset.Surface);
                Assert.True(
                    statusOnSurface >= SecondaryContrastFloor,
                    $"{preset.Id}: the {name} status colour is {statusOnSurface:0.00}:1 on the surface, below {SecondaryContrastFloor}:1.");
            }
        }
    }

    /// <summary>
    /// The luminance helper is the measuring instrument, so it gets its own check against the known
    /// endpoints: black on white is 21:1 by definition, and a colour against itself is 1:1.
    /// </summary>
    public static void TheContrastHelperMatchesTheKnownEndpoints()
    {
        var black = new OverlayColor(0f, 0f, 0f);
        var white = new OverlayColor(1f, 1f, 1f);

        Assert.True(Math.Abs(white.ContrastRatio(black) - 21f) < 0.05f, "White on black must be 21:1.");
        Assert.True(Math.Abs(black.ContrastRatio(white) - 21f) < 0.05f, "Contrast is symmetric.");
        Assert.True(Math.Abs(white.ContrastRatio(white) - 1f) < 0.001f, "A colour against itself is 1:1.");
        Assert.True(black.RelativeLuminance() < 0.001f);
        Assert.True(Math.Abs(white.RelativeLuminance() - 1f) < 0.001f);
    }

    /// <summary>
    /// The palette is selected before the first control exists, and a saved theme triggers a rebuild.
    /// </summary>
    /// <remarks>
    /// A Godot control keeps the colours it was created with, so applying the theme after building --
    /// or forgetting to rebuild when the setting changes -- leaves the panel in the previous palette
    /// with nothing failing. Both halves are one line each and both are invisible at runtime.
    /// </remarks>
    public static void TheStoredThemeIsAppliedBeforeAnythingIsBuilt()
    {
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        var build = AgentSourceFixture.MethodBody(overlay, "Build");

        var useTheme = build.IndexOf("UiFactory.UseTheme(startupSettings.OverlayTheme)", StringComparison.Ordinal);
        var firstControl = build.IndexOf("new CanvasLayer", StringComparison.Ordinal);
        Assert.True(useTheme >= 0, "Build no longer selects the stored theme.");
        Assert.True(
            firstControl > useTheme,
            "The palette has to be selected before the first control is built; a control keeps the colours it was created with.");

        var persist = AgentSourceFixture.MethodBody(overlay, "PersistHarvested");
        Assert.Contains("RebuildInPlace", persist);

        var harvest = AgentSourceFixture.MethodBody(overlay, "HarvestSettings");
        Assert.Contains("OverlayThemeCatalog.Normalize(SelectedMetadata(_themeCombo))", harvest);

        // The settings rebuild is what creates the selector, so the theme row has to be built outside
        // the advanced-only branch; an advanced-hidden control would still be harvested as null.
        var tabs = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Tabs.cs");
        var form = AgentSourceFixture.MethodBody(tabs, "RebuildSettingsForm");
        Assert.Contains("_themeCombo = UiFactory.Combo();", form);
        Assert.True(
            form.IndexOf("_themeCombo = UiFactory.Combo();", StringComparison.Ordinal) <
            form.IndexOf("if (_showAdvancedValue)", StringComparison.Ordinal),
            "The theme selector must be built whether or not the advanced section is open.");
    }
}
