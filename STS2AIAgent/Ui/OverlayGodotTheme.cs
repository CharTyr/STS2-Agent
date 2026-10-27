using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// The Godot <see cref="Theme"/> the overlay host carries, for everything the factory does not style
/// control by control.
/// </summary>
/// <remarks>
/// The factory styles the controls it builds. What it never reached were the parts Godot builds by
/// itself: the scroll bars inside every scroll container and log, the list an
/// <see cref="OptionButton"/> opens, the tooltip panel, the check box icons, and the caret and
/// selection colours. Those fell through to the engine's default theme -- grey bars, a grey popup
/// with a blue highlight, white square check boxes, a read-only text box in default grey -- inside a
/// panel drawn in the player's palette, which is a large part of why the window looked unfinished. A
/// theme on the host reaches all of them, popups and tooltips included, because both are children of
/// the control that opens them and inherit its theme.
///
/// It also sets the font. Without one, Latin text drew in the engine's default face and Chinese fell
/// back to whatever the system offered, so a line like "Token 消耗" mixed two typefaces at two
/// weights. A named system UI face covers both scripts, and its bold face gives headings weight
/// instead of only colour. The list falls through to other CJK faces and finally to the engine's own
/// fallback, so a machine without the first choice still draws text.
///
/// Rebuilt from the active palette on every theme switch; see
/// <see cref="AgentOverlayHost"/>'s <c>ApplyThemeChoice</c>.
/// </remarks>
internal static class OverlayGodotTheme
{
    private static readonly string[] UiFontNames =
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "PingFang SC",
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans SC",
        "WenQuanYi Micro Hei",
        "Segoe UI",
        "sans-serif"
    };

    private static Font? _regular;
    private static Font? _bold;

    /// <summary>The overlay's body face.</summary>
    public static Font Regular => _regular ??= SystemFace(400);

    /// <summary>The same face at bold weight, for titles and card headings.</summary>
    public static Font Bold => _bold ??= SystemFace(700);

    private static Font SystemFace(int weight)
    {
        return new SystemFont
        {
            FontNames = UiFontNames,
            FontWeight = weight,
            AllowSystemFallback = true,
            SubpixelPositioning = TextServer.SubpixelPositioning.Auto
        };
    }

    /// <summary>The theme for the active palette.</summary>
    public static Theme Build()
    {
        var palette = UiFactory.Palette;
        var text = UiFactory.ToGodot(palette.Text);
        var muted = UiFactory.ToGodot(palette.Muted);
        var accent = UiFactory.ToGodot(palette.Accent);
        var surface = UiFactory.ToGodot(palette.Surface);
        var inset = UiFactory.ToGodot(palette.Backdrop) with { A = 1f };
        var selection = accent with { A = 0.32f };

        var theme = new Theme
        {
            DefaultFont = Regular,
            DefaultFontSize = UiFactory.FontBody
        };

        // Named per type as well as by default: a theme's default font only applies where no theme
        // in the chain names one, and the game's own project theme is in that chain.
        foreach (var type in new[] { "Label", "Button", "CheckBox", "OptionButton", "LineEdit", "TextEdit", "PopupMenu", "TooltipLabel" })
        {
            theme.SetFont("font", type, Regular);
        }

        theme.SetFont("normal_font", "RichTextLabel", Regular);
        theme.SetFont("bold_font", "RichTextLabel", Bold);

        // Scroll bars: a thin rounded thumb and no track. The default is a grey gutter with a grey
        // block, as wide as a button's padding, beside every log.
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            var track = new StyleBoxEmpty();
            if (type == "VScrollBar")
            {
                track.ContentMarginLeft = 3;
                track.ContentMarginRight = 3;
            }
            else
            {
                track.ContentMarginTop = 3;
                track.ContentMarginBottom = 3;
            }

            theme.SetStylebox("scroll", type, track);
            theme.SetStylebox("scroll_focus", type, track);
            theme.SetStylebox("grabber", type, Thumb(0.20f));
            theme.SetStylebox("grabber_highlight", type, Thumb(0.34f));
            theme.SetStylebox("grabber_pressed", type, Thumb(0.44f));
        }

        theme.SetConstant("scrollbar_v_separation", "ScrollContainer", UiFactory.SpaceXs);
        theme.SetConstant("scrollbar_h_separation", "ScrollContainer", UiFactory.SpaceXs);

        // The list a drop-down opens: the card surface, a hairline frame, and a soft row highlight.
        theme.SetStylebox(
            "panel",
            "PopupMenu",
            UiFactory.Box(surface, UiFactory.Veil(0.14f), UiFactory.RadiusControl, padX: UiFactory.SpaceXs, padY: UiFactory.SpaceXs,
                shadowSize: 12, shadowColor: new Color(0f, 0f, 0f, 0.35f), shadowOffset: new Vector2(0, 4)));
        theme.SetStylebox("hover", "PopupMenu", UiFactory.Box(UiFactory.Veil(0.08f), new Color(0, 0, 0, 0), 6, borderWidth: 0));
        theme.SetStylebox("separator", "PopupMenu", UiFactory.Box(UiFactory.Veil(0.10f), new Color(0, 0, 0, 0), 0, borderWidth: 0, padX: 0, padY: 0));
        theme.SetColor("font_color", "PopupMenu", text);
        theme.SetColor("font_hover_color", "PopupMenu", text);
        theme.SetColor("font_disabled_color", "PopupMenu", muted);
        theme.SetColor("font_separator_color", "PopupMenu", muted);
        theme.SetFontSize("font_size", "PopupMenu", UiFactory.FontBody);
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetConstant("item_start_padding", "PopupMenu", UiFactory.SpaceSm);
        theme.SetConstant("item_end_padding", "PopupMenu", UiFactory.SpaceMd);

        // Tooltips: the jump bar and the Hide button name things in theirs.
        theme.SetStylebox(
            "panel",
            "TooltipPanel",
            UiFactory.Box(surface, UiFactory.Veil(0.14f), 6, padX: UiFactory.SpaceSm, padY: 5,
                shadowSize: 8, shadowColor: new Color(0f, 0f, 0f, 0.30f), shadowOffset: new Vector2(0, 2)));
        theme.SetColor("font_color", "TooltipLabel", text);
        theme.SetFontSize("font_size", "TooltipLabel", UiFactory.FontCaption);

        // Check boxes draw as switches. The label beside them stays the text colour on hover; the
        // switch is what shows the state.
        theme.SetIcon("checked", "CheckBox", SwitchIcon(on: true, disabled: false));
        theme.SetIcon("unchecked", "CheckBox", SwitchIcon(on: false, disabled: false));
        theme.SetIcon("checked_disabled", "CheckBox", SwitchIcon(on: true, disabled: true));
        theme.SetIcon("unchecked_disabled", "CheckBox", SwitchIcon(on: false, disabled: true));
        var bare = new StyleBoxEmpty { ContentMarginTop = 4, ContentMarginBottom = 4 };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            theme.SetStylebox(state, "CheckBox", bare);
        }

        theme.SetConstant("h_separation", "CheckBox", UiFactory.SpaceSm);

        // Text editing: an accent caret and a translucent accent selection, on every editor.
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetColor("caret_color", type, accent);
            theme.SetColor("selection_color", type, selection);
            theme.SetColor("font_selected_color", type, text);
        }

        theme.SetColor("font_readonly_color", "TextEdit", text);
        theme.SetColor("font_uneditable_color", "LineEdit", muted);
        theme.SetColor("selection_color", "RichTextLabel", selection);
        theme.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());

        // The drop-down's arrow takes the label colour instead of the engine's fixed grey.
        theme.SetConstant("modulate_arrow", "OptionButton", 1);
        theme.SetColor("font_color", "Label", text);
        return theme;
    }

    /// <summary>The scroll thumb: a pill in the text colour at <paramref name="alpha"/>.</summary>
    private static StyleBoxFlat Thumb(float alpha)
    {
        return UiFactory.Box(UiFactory.Veil(alpha), new Color(0, 0, 0, 0), 3, borderWidth: 0, padX: 0, padY: 0);
    }

    /// <summary>
    /// A check box state drawn as a switch: a pill track and a round knob, accent when on.
    /// </summary>
    /// <remarks>
    /// Rasterised here rather than shipped as an image so it follows the palette, and drawn from the
    /// signed distance to each shape so its edges are anti-aliased: a hard-edged circle at this size
    /// is a staircase. The knob is the lighter of the text and surface colours, so it is a light knob
    /// on every theme -- on the dark themes the text colour, on ivory the surface.
    /// </remarks>
    private static Texture2D SwitchIcon(bool on, bool disabled)
    {
        const int width = 30;
        const int height = 18;
        const float radius = height / 2f;
        const float knobRadius = radius - 2.5f;

        var palette = UiFactory.Palette;
        var track = on ? UiFactory.ToGodot(palette.Accent) : UiFactory.ToGodot(palette.Border);
        var text = UiFactory.ToGodot(palette.Text);
        var surface = UiFactory.ToGodot(palette.Surface);
        var knob = text.Luminance >= surface.Luminance ? text : surface;
        var opacity = disabled ? 0.45f : 1f;
        var knobX = on ? width - radius : radius;

        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;

                // Capsule: distance to the segment between the two end-cap centres, minus the radius.
                var cx = Math.Clamp(px, radius, width - radius);
                var trackDistance = MathF.Sqrt(((px - cx) * (px - cx)) + ((py - radius) * (py - radius))) - radius;
                var knobDistance = MathF.Sqrt(((px - knobX) * (px - knobX)) + ((py - radius) * (py - radius))) - knobRadius;
                var trackAlpha = Math.Clamp(0.5f - trackDistance, 0f, 1f);
                var knobAlpha = Math.Clamp(0.5f - knobDistance, 0f, 1f);

                var alpha = knobAlpha + (trackAlpha * (1f - knobAlpha));
                if (alpha <= 0f)
                {
                    image.SetPixel(x, y, new Color(track.R, track.G, track.B, 0f));
                    continue;
                }

                var r = ((knob.R * knobAlpha) + (track.R * trackAlpha * (1f - knobAlpha))) / alpha;
                var g = ((knob.G * knobAlpha) + (track.G * trackAlpha * (1f - knobAlpha))) / alpha;
                var b = ((knob.B * knobAlpha) + (track.B * trackAlpha * (1f - knobAlpha))) / alpha;
                image.SetPixel(x, y, new Color(r, g, b, alpha * opacity));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
