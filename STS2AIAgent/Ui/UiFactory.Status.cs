using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// The factory's status vocabulary: tones, and the pills, dots and callouts drawn in them.
/// </summary>
/// <remarks>
/// Split out of <c>UiFactory.cs</c> when the 2026-10-27 visual pass took that file past the size
/// budget. A tone is one meaning -- positive, caution, danger, neutral -- drawn the same way
/// wherever it appears, and <see cref="ToneStyle"/> is the one place the mapping lives.
/// </remarks>
internal static partial class UiFactory
{
    /// <summary>
    /// Draws a label in a status tone and records it, so a theme switch keeps the tone. Null clears
    /// the tone and returns the label to its text or muted colour.
    /// </summary>
    /// <remarks>
    /// The status lines used to carry their tone in an emoji ("✅ 已验证", "⚠️ 测试失败"), which the
    /// game draws from a fallback font at a different size and baseline from the text beside it.
    /// The colour carries the same meaning without the glyph.
    /// </remarks>
    public static void SetTone(Label label, ButtonKind? tone)
    {
        if (tone is { } value) label.SetMeta(MetaTone, (int)value);
        else label.RemoveMeta(MetaTone);
        ApplyLabelColor(label);
    }

    /// <summary>The text colour of a tone, the same one its pill and dot use.</summary>
    private static Color ToneText(ButtonKind tone) => ToneStyle(tone).Foreground;

    /// <summary>A short status word in a tone colour, without a chip behind it.</summary>
    public static Label Badge(string text, ButtonKind tone = ButtonKind.Normal)
    {
        var label = Label(text, FontCaption);
        SetTone(label, tone);
        return label;
    }

    /// <summary>
    /// A structured badge pill with background tint and contrast text, e.g. for status indicators.
    /// </summary>
    /// <remarks>
    /// Used where a status has to be readable as a status rather than as a sentence: a tinted chip at
    /// the top of a card lands before any of the words do. The tone carries the meaning -- positive,
    /// caution, danger, or neutral -- and <see cref="ToneStyle"/> is the one place that mapping lives,
    /// so a control that restyles itself in place agrees with the pill it replaced.
    /// </remarks>
    public static Control PillBadge(string text, ButtonKind tone = ButtonKind.Normal)
    {
        var pill = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        pill.SetMeta(MetaSurface, SurfaceTinted);
        pill.SetMeta(MetaTint, (int)tone);
        ReapplySurfaceTheme(pill);
        var label = Label(text, FontCaption);
        SetTone(label, tone);
        pill.AddChild(label);
        return pill;
    }

    /// <summary>
    /// A pill with a status dot before its text: the title bar's "playing / idle" tag. The dot is
    /// a drawn circle rather than a "●" glyph, which the fallback font drew off the text's baseline.
    /// </summary>
    public static PanelContainer StatusPill(Label label, ButtonKind tone)
    {
        var pill = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        pill.SetMeta(MetaSurface, SurfaceTinted);
        pill.SetMeta(MetaTint, (int)tone);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Dot(tone));
        row.AddChild(label);
        pill.AddChild(row);
        Retone(pill, tone);
        return pill;
    }

    /// <summary>A 7 px status dot in a tone colour.</summary>
    public static Panel Dot(ButtonKind tone)
    {
        var dot = new Panel
        {
            CustomMinimumSize = new Vector2(7, 7),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        dot.SetMeta(MetaPanel, PanelDot);
        dot.SetMeta(MetaTint, (int)tone);
        ReapplyPanelTheme(dot);
        return dot;
    }

    /// <summary>
    /// Moves a pill, and the labels and dots inside it, to another tone, recording it so a theme
    /// switch repaints the new tone rather than the one the pill was built with.
    /// </summary>
    public static void Retone(PanelContainer pill, ButtonKind tone)
    {
        pill.SetMeta(MetaTint, (int)tone);
        ReapplySurfaceTheme(pill);
        RetoneChildren(pill, tone);
    }

    private static void RetoneChildren(Node node, ButtonKind tone)
    {
        foreach (var child in node.GetChildren())
        {
            switch (child)
            {
                case Label label:
                    SetTone(label, tone);
                    break;
                case Panel dot when dot.GetMeta(MetaPanel, "").AsString() == PanelDot:
                    dot.SetMeta(MetaTint, (int)tone);
                    ReapplyPanelTheme(dot);
                    break;
            }

            RetoneChildren(child, tone);
        }
    }

    /// <summary>
    /// A tinted notice across the page: what is missing before the AI can play, and the button that
    /// fixes it. The tone is the meaning; there is no border to compete with the cards around it.
    /// </summary>
    public static PanelContainer Callout(ButtonKind tone, params Control[] children)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        panel.SetMeta(MetaTint, (int)tone);
        TagSurface(panel, SurfaceRole.Callout);
        var column = Column();
        foreach (var child in children)
        {
            column.AddChild(child);
        }

        panel.AddChild(column);
        return panel;
    }

    /// <summary>
    /// The background and foreground a tone is drawn with, so a control that changes tone at runtime
    /// repaints with exactly the pair a fresh <see cref="PillBadge"/> of that tone would use.
    /// </summary>
    /// <remarks>
    /// The background is the foreground at low opacity rather than a darkened copy of it: a darkened
    /// green on a dark card is a muddy olive, a translucent one is a tint of the card.
    /// </remarks>
    public static (Color Background, Color Foreground) ToneStyle(ButtonKind tone)
    {
        var foreground = tone switch
        {
            ButtonKind.Primary => ToGodot(_palette.Positive),
            ButtonKind.Danger => ToGodot(_palette.Danger),
            ButtonKind.Ghost => ToGodot(_palette.Muted),
            ButtonKind.Floating => ToGodot(_palette.Accent),
            _ => ToGodot(_palette.Warning)
        };
        return (foreground with { A = tone == ButtonKind.Ghost ? 0.10f : 0.14f }, foreground);
    }

    /// <summary>
    /// The pill shape itself: the same rounded chip <see cref="PillBadge"/> draws, exposed so a
    /// control whose tone changes can rebuild just the box and keep the label it already has.
    /// </summary>
    public static StyleBoxFlat ChipStyle(Color background, Color border)
    {
        return Box(background, border, RadiusPill, borderWidth: border.A > 0 ? 1 : 0, padX: SpaceSm + 1, padY: 3);
    }
}
