using System;
using System.Collections.Generic;

namespace STS2AIAgent.Ui;

/// <summary>
/// One colour, without a reference to Godot.
/// </summary>
/// <remarks>
/// The palette lives outside Godot so the executable test project can link it and pin the thing that
/// actually matters about a colour scheme: that every theme's body text stays readable on every
/// surface it is drawn on. A contrast floor checked here is worth more than a screenshot nobody
/// re-takes.
/// </remarks>
internal readonly record struct OverlayColor(float R, float G, float B, float A = 1f)
{
    /// <summary>
    /// WCAG relative luminance: the sRGB channels linearised, then weighted for how the eye responds.
    /// Alpha is ignored on purpose -- the palette's translucent surface is composited by the game
    /// over a mostly-dark background, and the floor below is set conservatively enough to hold there.
    /// </summary>
    public float RelativeLuminance()
    {
        static float Linear(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        return (0.2126f * Linear(R)) + (0.7152f * Linear(G)) + (0.0722f * Linear(B));
    }

    /// <summary>WCAG contrast ratio against another colour, 1 (identical) to 21 (black on white).</summary>
    public float ContrastRatio(OverlayColor other)
    {
        var first = RelativeLuminance();
        var second = other.RelativeLuminance();
        var lighter = MathF.Max(first, second);
        var darker = MathF.Min(first, second);
        return (lighter + 0.05f) / (darker + 0.05f);
    }
}

/// <summary>
/// Every colour the overlay draws with. A theme is this record and nothing else, so adding one is a
/// data change rather than a new branch in the drawing code.
/// </summary>
internal sealed record OverlayPalette(
    string Id,
    string Label,
    OverlayColor Backdrop,
    OverlayColor Surface,
    OverlayColor SurfaceRaised,
    OverlayColor Border,
    OverlayColor Accent,
    OverlayColor AccentText,
    OverlayColor Text,
    OverlayColor Muted,
    OverlayColor Positive,
    OverlayColor Warning,
    OverlayColor Danger);

/// <summary>
/// The overlay's colour themes.
/// </summary>
/// <remarks>
/// Four presets rather than a colour picker: the point is that the default stops looking unfinished,
/// and a small set of reviewed schemes keeps every one of them legible. The reference mod that
/// prompted this ships adjustable theming; four good ones cover the same need without asking the
/// player to become a designer, and the contract test below can only hold a floor over a fixed set.
/// </remarks>
internal static class OverlayThemeCatalog
{
    internal const string DefaultId = "slate";

    private static readonly OverlayPalette[] Presets =
    {
        // The default. Near-black slate with a brass accent, which is the palette the mod already
        // leaned towards; the difference is that the surfaces and the border are now distinguishable
        // from each other, so sections read as sections.
        new(
            Id: DefaultId,
            Label: "暮色",
            Backdrop: new(0.055f, 0.063f, 0.086f, 0.97f),
            Surface: new(0.086f, 0.098f, 0.129f, 1f),
            SurfaceRaised: new(0.137f, 0.157f, 0.200f, 1f),
            Border: new(0.243f, 0.275f, 0.337f, 1f),
            Accent: new(0.855f, 0.663f, 0.318f, 1f),
            AccentText: new(0.078f, 0.067f, 0.043f, 1f),
            Text: new(0.925f, 0.918f, 0.894f, 1f),
            Muted: new(0.667f, 0.682f, 0.718f, 1f),
            Positive: new(0.478f, 0.749f, 0.518f, 1f),
            Warning: new(0.902f, 0.729f, 0.353f, 1f),
            Danger: new(0.878f, 0.451f, 0.435f, 1f)),

        // Deeper blue, for players who keep the overlay open over a bright map.
        new(
            Id: "midnight",
            Label: "午夜",
            Backdrop: new(0.043f, 0.055f, 0.098f, 0.97f),
            Surface: new(0.071f, 0.090f, 0.145f, 1f),
            SurfaceRaised: new(0.114f, 0.141f, 0.216f, 1f),
            Border: new(0.216f, 0.263f, 0.376f, 1f),
            Accent: new(0.478f, 0.678f, 0.945f, 1f),
            AccentText: new(0.043f, 0.063f, 0.110f, 1f),
            Text: new(0.910f, 0.925f, 0.957f, 1f),
            Muted: new(0.639f, 0.686f, 0.780f, 1f),
            Positive: new(0.478f, 0.780f, 0.616f, 1f),
            Warning: new(0.910f, 0.749f, 0.400f, 1f),
            Danger: new(0.898f, 0.478f, 0.478f, 1f)),

        // Warm parchment, closest to the game's own card frames.
        new(
            Id: "parchment",
            Label: "羊皮纸",
            Backdrop: new(0.129f, 0.106f, 0.086f, 0.97f),
            Surface: new(0.180f, 0.149f, 0.118f, 1f),
            SurfaceRaised: new(0.243f, 0.204f, 0.161f, 1f),
            Border: new(0.396f, 0.333f, 0.259f, 1f),
            Accent: new(0.898f, 0.706f, 0.361f, 1f),
            AccentText: new(0.129f, 0.098f, 0.055f, 1f),
            Text: new(0.949f, 0.925f, 0.878f, 1f),
            Muted: new(0.729f, 0.678f, 0.600f, 1f),
            Positive: new(0.573f, 0.780f, 0.502f, 1f),
            Warning: new(0.949f, 0.780f, 0.412f, 1f),
            Danger: new(0.894f, 0.502f, 0.427f, 1f)),

        // Maximum legibility: pure black surfaces, white text, a saturated accent. For a player who
        // reads the panel on a small window or a bright screen.
        new(
            Id: "contrast",
            Label: "高对比",
            Backdrop: new(0.000f, 0.000f, 0.000f, 0.98f),
            Surface: new(0.055f, 0.055f, 0.055f, 1f),
            SurfaceRaised: new(0.121f, 0.121f, 0.121f, 1f),
            Border: new(0.400f, 0.400f, 0.400f, 1f),
            Accent: new(1.000f, 0.831f, 0.263f, 1f),
            AccentText: new(0.000f, 0.000f, 0.000f, 1f),
            Text: new(1.000f, 1.000f, 1.000f, 1f),
            Muted: new(0.784f, 0.784f, 0.784f, 1f),
            Positive: new(0.400f, 0.902f, 0.400f, 1f),
            Warning: new(1.000f, 0.855f, 0.200f, 1f),
            Danger: new(1.000f, 0.451f, 0.451f, 1f)),

        // Ironclad Crimson: molten ember red and forged charcoal iron.
        new(
            Id: "crimson",
            Label: "猩红",
            Backdrop: new(0.065f, 0.045f, 0.050f, 0.97f),
            Surface: new(0.105f, 0.070f, 0.075f, 1f),
            SurfaceRaised: new(0.155f, 0.105f, 0.115f, 1f),
            Border: new(0.350f, 0.220f, 0.240f, 1f),
            Accent: new(0.920f, 0.380f, 0.340f, 1f),
            AccentText: new(0.080f, 0.030f, 0.030f, 1f),
            Text: new(0.950f, 0.920f, 0.900f, 1f),
            Muted: new(0.720f, 0.640f, 0.650f, 1f),
            Positive: new(0.480f, 0.780f, 0.520f, 1f),
            Warning: new(0.920f, 0.750f, 0.350f, 1f),
            Danger: new(1.000f, 0.450f, 0.450f, 1f)),

        // Silent Emerald: poisonous shadowed green and bright venom highlights.
        new(
            Id: "emerald",
            Label: "翡翠",
            Backdrop: new(0.045f, 0.065f, 0.055f, 0.97f),
            Surface: new(0.070f, 0.105f, 0.085f, 1f),
            SurfaceRaised: new(0.105f, 0.155f, 0.125f, 1f),
            Border: new(0.220f, 0.340f, 0.270f, 1f),
            Accent: new(0.380f, 0.850f, 0.620f, 1f),
            AccentText: new(0.030f, 0.080f, 0.050f, 1f),
            Text: new(0.910f, 0.950f, 0.920f, 1f),
            Muted: new(0.650f, 0.720f, 0.680f, 1f),
            Positive: new(0.450f, 0.820f, 0.550f, 1f),
            Warning: new(0.920f, 0.750f, 0.350f, 1f),
            Danger: new(0.920f, 0.450f, 0.450f, 1f)),

        // Watcher / Defect Amethyst: ethereal celestial violet and cosmic glow.
        new(
            Id: "amethyst",
            Label: "紫晶",
            Backdrop: new(0.055f, 0.045f, 0.075f, 0.97f),
            Surface: new(0.085f, 0.070f, 0.120f, 1f),
            SurfaceRaised: new(0.130f, 0.110f, 0.180f, 1f),
            Border: new(0.280f, 0.230f, 0.380f, 1f),
            Accent: new(0.750f, 0.550f, 0.950f, 1f),
            AccentText: new(0.060f, 0.030f, 0.090f, 1f),
            Text: new(0.940f, 0.920f, 0.960f, 1f),
            Muted: new(0.700f, 0.650f, 0.750f, 1f),
            Positive: new(0.480f, 0.780f, 0.550f, 1f),
            Warning: new(0.920f, 0.760f, 0.380f, 1f),
            Danger: new(0.920f, 0.450f, 0.480f, 1f)),

        // The light one. Every other preset is dark, which is the right default for a game that is
        // itself dark -- but a player reading the panel in a bright room, or on a laptop in daylight,
        // has no option here at all, and dark-on-dark is the one thing the overlay cannot fix by
        // re-arranging. Ink on paper: a warm ivory surface, near-black text, and a deep bronze accent
        // rather than a bright one, because a bright accent cannot carry readable label text on a
        // light surface (amber at 0.72 luminance gives 3.5:1 against its own label; this one gives
        // 7.3:1). Checked against the same floors OverlayThemeTests enforces.
        new(
            Id: "ivory",
            Label: "象牙",
            Backdrop: new(0.925f, 0.910f, 0.875f, 0.97f),
            Surface: new(0.968f, 0.957f, 0.933f, 1f),
            SurfaceRaised: new(0.888f, 0.868f, 0.828f, 1f),
            Border: new(0.640f, 0.600f, 0.540f, 1f),
            Accent: new(0.470f, 0.290f, 0.040f, 1f),
            AccentText: new(1.000f, 0.980f, 0.950f, 1f),
            Text: new(0.110f, 0.098f, 0.085f, 1f),
            Muted: new(0.372f, 0.348f, 0.318f, 1f),
            Positive: new(0.129f, 0.420f, 0.200f, 1f),
            Warning: new(0.520f, 0.340f, 0.020f, 1f),
            Danger: new(0.700f, 0.140f, 0.120f, 1f))
    };

    public static IReadOnlyList<OverlayPalette> All => Presets;

    /// <summary>Every preset id, in the order a selector should offer them.</summary>
    public static IReadOnlyList<string> Ids
    {
        get
        {
            var ids = new string[Presets.Length];
            for (var index = 0; index < Presets.Length; index++)
            {
                ids[index] = Presets[index].Id;
            }

            return ids;
        }
    }

    /// <summary>
    /// The palette for an id, falling back to the default for an unknown or empty one.
    /// </summary>
    /// <remarks>
    /// A fallback rather than an error: the id is stored in the player's settings file, so a value
    /// written by a newer build (or hand-edited into nonsense) must not leave the overlay drawing in
    /// an undefined palette. <see cref="Normalize"/> is what the settings writer uses to keep the
    /// stored value honest.
    /// </remarks>
    public static OverlayPalette Resolve(string? id)
    {
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Id, id, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return Presets[0];
    }

    /// <summary>The id to persist for <paramref name="id"/>: a known one, or the default.</summary>
    public static string Normalize(string? id)
    {
        return Resolve(id).Id;
    }

    public static string Label(string? id)
    {
        return Resolve(id).Label;
    }
}
