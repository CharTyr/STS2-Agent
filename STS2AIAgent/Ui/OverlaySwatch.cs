using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// A palette drawn as the thing it is: the accent it leads with, and the colours it puts on its own
/// surface.
/// </summary>
/// <remarks>
/// Split out of <c>UiFactory.cs</c> when the preview pushed that file past its size budget. The cut is
/// the same one the overlay made for its pages: this is the only thing the factory does that paints an
/// *inactive* palette rather than the active one, so it is a self-contained job rather than another
/// style helper.
///
/// Built from <see cref="ColorRect"/> children rather than a <c>_Draw</c> override. The first version
/// drew itself and was never called in the live build -- the diagnostic printed nothing at all and
/// every tile rendered as an empty rectangle, reported on 2026-09-21 as "the swatch tiles have nothing
/// in them". Plain child nodes have no such question: they are in the scene tree and the renderer walks
/// them.
///
/// The layout answers the other half of that report, "all the previews are black": the accent gets a
/// full-height stripe of its own, and the content bars are drawn from the palette's *text* colours,
/// which every theme guarantees are readable on its own surface. Two dark themes are told apart by
/// their surface and frame rather than by a band that disappears into them.
/// </remarks>
internal static class OverlaySwatch
{
    /// <summary>One palette as a framed tile: accent stripe, surface field, then four content bars.</summary>
    public static Control Build(OverlayPalette palette)
    {
        var frame = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 52),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        frame.AddThemeStyleboxOverride("panel", UiFactory.Box(Bare(palette.Border), Bare(palette.Backdrop), 4, padX: 2, padY: 2));

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);

        // The accent, full height: this is the colour the theme is named for and the one a player picks
        // it by.
        row.AddChild(Fill(Bare(palette.Accent), 14));

        var stack = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 3);

        // The surface itself, as a flat field, then four bars whose colours are guaranteed to contrast
        // with it. Descending lengths so the stack reads as lines of text rather than as a chart.
        stack.AddChild(Fill(Bare(palette.Surface), 9, 1f, expand: true));
        foreach (var (color, share) in new[]
                 {
                     (palette.Text, 1.0f),
                     (palette.Muted, 0.78f),
                     (palette.Positive, 0.56f),
                     (palette.Danger, 0.34f)
                 })
        {
            stack.AddChild(Fill(Bare(color), 4, share));
        }

        row.AddChild(stack);
        frame.AddChild(row);
        return frame;
    }

    /// <summary>One colour block: a fixed-height rect occupying <paramref name="share"/> of the width.</summary>
    private static Control Fill(Color color, int height, float share = 1f, bool expand = false)
    {
        var rect = new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(0, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkBegin
        };

        if (share >= 1f)
        {
            return rect;
        }

        // A share below 1 is a shorter bar beside a spacer rather than a squeezed one: the widths have
        // to stay proportional inside an expanding column, which a minimum size cannot express.
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 0);
        row.AddChild(rect);
        var spacer = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, height)
        };
        row.AddChild(spacer);
        rect.SizeFlagsStretchRatio = share;
        spacer.SizeFlagsStretchRatio = MathF.Max(0.01f, 1f - share);
        return row;
    }

    /// <summary>The opaque form of a palette colour: a swatch must not show what is behind it.</summary>
    private static Color Bare(OverlayColor color)
    {
        return new Color(color.R, color.G, color.B);
    }
}
