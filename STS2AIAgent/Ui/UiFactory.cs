using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// Every control the overlay builds, styled from one place.
/// </summary>
/// <remarks>
/// The overlay used to hand Godot's defaults straight through: unstyled buttons, one font size, and
/// a single panel border, which is why six pages of dense text read as one wall. Nothing here is
/// cosmetic for its own sake -- a control that cannot be told apart from its neighbour costs the
/// player a second every time they look at it, and the panel is open while they are playing.
///
/// The shape of the module is deliberate: the palette comes from <see cref="OverlayThemeCatalog"/>
/// (Godot-free and pinned offline), and everything in this file is the translation of that palette
/// into Godot <c>StyleBox</c>es and font overrides. A new colour or spacing does not become a new
/// branch in a page builder.
/// </remarks>
internal static class UiFactory
{
    /// <summary>The active palette. Set once at build and again when the theme setting changes.</summary>
    private static OverlayPalette _palette = OverlayThemeCatalog.Resolve(OverlayThemeCatalog.DefaultId);

    // Spacing scale. Four values rather than free numbers, because the thing that made the old layout
    // look unfinished was inconsistent gaps far more than any single wrong gap.
    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 18;

    // Type scale.
    public const int FontTitle = 17;
    public const int FontHeading = 14;
    public const int FontBody = 13;
    public const int FontCaption = 11;

    public static OverlayPalette Palette => _palette;

    /// <summary>Switches the palette. Existing controls keep the colours they were built with.</summary>
    public static void UseTheme(string? themeId)
    {
        _palette = OverlayThemeCatalog.Resolve(themeId);
    }

    public static Color ToGodot(OverlayColor color) => new(color.R, color.G, color.B, color.A);

    // Named colours, kept for the call sites that predate the theme module. They now read from the
    // active palette instead of being fixed, so switching themes actually moves them.
    public static Color Bg => ToGodot(_palette.Backdrop);
    public static Color BgRaised => ToGodot(_palette.SurfaceRaised);
    public static Color Accent => ToGodot(_palette.Accent);
    public static Color Text => ToGodot(_palette.Text);
    public static Color Muted => ToGodot(_palette.Muted);

    /// <summary>How a button is meant to read: the page's main action, an ordinary one, or a caution.</summary>
    public enum ButtonKind
    {
        Normal,
        Primary,
        Ghost,
        Danger
    }

    private static StyleBoxFlat Box(
        Color background,
        Color border,
        int radius,
        int borderWidth = 1,
        int padX = SpaceMd,
        int padY = SpaceSm)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            BorderColor = border
        };
    }

    /// <summary>The panel behind the whole overlay: one surface, one border, a soft corner.</summary>
    public static StyleBoxFlat PanelStyle(Color? color = null, int radius = 10)
    {
        return Box(
            color ?? ToGodot(_palette.Backdrop),
            ToGodot(_palette.Border),
            radius,
            borderWidth: 1,
            padX: SpaceMd,
            padY: SpaceMd);
    }

    /// <summary>A section card: the raised surface that separates one group of rows from the next.</summary>
    public static StyleBoxFlat CardStyle()
    {
        return Box(ToGodot(_palette.Surface), ToGodot(_palette.Border), 8, borderWidth: 1);
    }

    /// <summary>
    /// A button that can be told apart from the one next to it.
    /// </summary>
    /// <remarks>
    /// Godot's default button theme is a grey slab that looks identical whether it is the page's main
    /// action or a footnote, and it ignores the panel's palette entirely. Each kind now carries its own
    /// normal/hover/pressed/disabled boxes so hover gives feedback and the primary action is obvious.
    /// </remarks>
    public static Button Button(string text, Action? onPressed = null, ButtonKind kind = ButtonKind.Normal)
    {
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None
        };

        ApplyButtonTheme(button, kind);
        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    private static void ApplyButtonTheme(Button button, ButtonKind kind)
    {
        var border = ToGodot(_palette.Border);
        Color background;
        Color text;
        Color hover;
        Color pressed;

        switch (kind)
        {
            case ButtonKind.Primary:
                background = ToGodot(_palette.Accent);
                text = ToGodot(_palette.AccentText);
                hover = background.Lightened(0.12f);
                pressed = background.Darkened(0.12f);
                break;
            case ButtonKind.Ghost:
                background = new Color(1f, 1f, 1f, 0.04f);
                text = ToGodot(_palette.Muted);
                hover = new Color(1f, 1f, 1f, 0.10f);
                pressed = new Color(1f, 1f, 1f, 0.02f);
                break;
            case ButtonKind.Danger:
                background = ToGodot(_palette.SurfaceRaised);
                text = ToGodot(_palette.Danger);
                hover = ToGodot(_palette.SurfaceRaised).Lightened(0.10f);
                pressed = ToGodot(_palette.SurfaceRaised).Darkened(0.10f);
                break;
            default:
                background = ToGodot(_palette.SurfaceRaised);
                text = ToGodot(_palette.Text);
                hover = background.Lightened(0.10f);
                pressed = background.Darkened(0.10f);
                break;
        }

        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", kind == ButtonKind.Primary ? text : text.Lightened(0.15f));
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_disabled_color", ToGodot(_palette.Muted));

        button.AddThemeStyleboxOverride("normal", Box(background, border, 6));
        button.AddThemeStyleboxOverride("hover", Box(hover, kind == ButtonKind.Primary ? background : border, 6));
        button.AddThemeStyleboxOverride("pressed", Box(pressed, border, 6));
        button.AddThemeStyleboxOverride("disabled", Box(background.Darkened(0.25f), border, 6));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>
    /// One tab in the header row. The selected tab is drawn as a filled pill, which is the only thing
    /// that tells the player where they are: six identical buttons in a row do not.
    /// </summary>
    /// <remarks>
    /// <c>ClipText</c> stays off. Godot's clipped button reports a minimum width that does not include
    /// its label, so a tab row laid out by natural width collapsed to five empty slivers with the
    /// text cut off -- observed live on 2026-09-20 in the first pass of this redesign. The label has
    /// to drive the button's size for a wrapping row to work.
    /// </remarks>
    public static Button TabButton(string text, bool active, Action? onPressed)
    {
        var border = ToGodot(_palette.Border);
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None
        };

        var activeBackground = ToGodot(_palette.Accent);
        var idleBackground = new Color(1f, 1f, 1f, 0.03f);
        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeColorOverride("font_color", active ? ToGodot(_palette.AccentText) : ToGodot(_palette.Muted));
        button.AddThemeColorOverride("font_hover_color", active ? ToGodot(_palette.AccentText) : ToGodot(_palette.Text));
        button.AddThemeColorOverride("font_pressed_color", ToGodot(_palette.AccentText));
        button.AddThemeStyleboxOverride(
            "normal",
            Box(active ? activeBackground : idleBackground, active ? activeBackground : new Color(0, 0, 0, 0), 6, padX: SpaceSm, padY: 6));
        button.AddThemeStyleboxOverride(
            "hover",
            Box(active ? activeBackground.Lightened(0.10f) : new Color(1f, 1f, 1f, 0.09f), border, 6, padX: SpaceSm, padY: 6));
        button.AddThemeStyleboxOverride(
            "pressed",
            Box(active ? activeBackground.Darkened(0.12f) : new Color(1f, 1f, 1f, 0.05f), border, 6, padX: SpaceSm, padY: 6));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    public static Label Label(string text, int size = FontBody, bool muted = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? ToGodot(_palette.Muted) : ToGodot(_palette.Text));
        return label;
    }

    /// <summary>A section heading inside a page: larger, in the accent colour, with the gap above it.</summary>
    public static Label Heading(string text)
    {
        var label = Label(text, FontHeading);
        label.AddThemeColorOverride("font_color", ToGodot(_palette.Accent));
        return label;
    }

    /// <summary>A short status chip, used for the header's live state.</summary>
    public static Label Badge(string text, ButtonKind tone = ButtonKind.Normal)
    {
        var color = tone switch
        {
            ButtonKind.Primary => ToGodot(_palette.Positive),
            ButtonKind.Danger => ToGodot(_palette.Danger),
            ButtonKind.Ghost => ToGodot(_palette.Muted),
            _ => ToGodot(_palette.Warning)
        };
        var label = Label(text, FontCaption);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>
    /// A titled section: heading, then the rows, on the raised surface.
    /// </summary>
    /// <remarks>
    /// The pages are long, and a flat column of twenty rows gives the eye nowhere to land. A card per
    /// topic costs one container and makes the panel scannable while a combat turn is running.
    /// </remarks>
    public static Control Card(string? title, params Control[] children)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", CardStyle());
        var column = Column();
        if (!string.IsNullOrEmpty(title))
        {
            column.AddChild(Heading(title));
        }

        foreach (var child in children)
        {
            column.AddChild(child);
        }

        card.AddChild(column);
        return card;
    }

    /// <summary>A vertical gap, so spacing is a value rather than a magic minimum size.</summary>
    public static Control Gap(int height = SpaceSm)
    {
        return new Control
        {
            CustomMinimumSize = new Vector2(0, height),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
    }

    public static Control Divider()
    {
        var divider = new Panel { CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
        divider.AddThemeStyleboxOverride("panel", Box(ToGodot(_palette.Border), new Color(0, 0, 0, 0), 0, padX: 0, padY: 0));
        return divider;
    }

    public static LineEdit Line(string text = "", string placeholder = "", bool secret = false)
    {
        var edit = new LineEdit
        {
            Text = text,
            PlaceholderText = placeholder,
            Secret = secret,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        edit.AddThemeFontSizeOverride("font_size", FontBody);
        edit.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted));
        edit.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.Surface), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        edit.AddThemeStyleboxOverride("focus", Box(ToGodot(_palette.Surface), ToGodot(_palette.Accent), 6, padX: SpaceSm, padY: 6));
        return edit;
    }

    public static CheckBox Check(string text, bool on)
    {
        var box = new CheckBox
        {
            Text = text,
            ButtonPressed = on,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        box.AddThemeFontSizeOverride("font_size", FontBody);
        box.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        box.AddThemeColorOverride("font_hover_color", ToGodot(_palette.Accent));
        box.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return box;
    }

    public static OptionButton Combo()
    {
        var combo = new OptionButton { MouseFilter = Control.MouseFilterEnum.Stop };
        combo.AddThemeFontSizeOverride("font_size", FontBody);
        combo.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        combo.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.SurfaceRaised), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        combo.AddThemeStyleboxOverride("hover", Box(ToGodot(_palette.SurfaceRaised).Lightened(0.08f), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        combo.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return combo;
    }

    public static TextEdit Multiline(string text = "", int minHeight = 72)
    {
        var edit = new TextEdit
        {
            Text = text,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, minHeight),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        edit.AddThemeFontSizeOverride("font_size", FontBody);
        edit.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted));
        edit.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.Surface), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        edit.AddThemeStyleboxOverride("focus", Box(ToGodot(_palette.Surface), ToGodot(_palette.Accent), 6, padX: SpaceSm, padY: 6));
        return edit;
    }

    public static RichTextLabel Rich(bool follow = true)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollFollowing = follow,
            SelectionEnabled = true,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        label.AddThemeFontSizeOverride("normal_font_size", FontBody);
        label.AddThemeColorOverride("default_color", ToGodot(_palette.Text));
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        return label;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", SpaceSm);
        foreach (var child in children)
        {
            child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(child);
        }

        return row;
    }

    /// <summary>A row whose children keep their natural width instead of being stretched.</summary>
    public static HBoxContainer TightRow(params Control[] children)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", SpaceSm);
        foreach (var child in children)
        {
            child.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            row.AddChild(child);
        }

        return row;
    }

    public static VBoxContainer Column()
    {
        var column = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        column.AddThemeConstantOverride("separation", SpaceSm);
        return column;
    }

    public static ScrollContainer Scroll(Control child, float minHeight = 0)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        if (minHeight > 0)
        {
            scroll.CustomMinimumSize = new Vector2(0, minHeight);
        }

        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        child.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.AddChild(child);
        return scroll;
    }

    public static bool TryParseHotkey(string? raw, out Key key)
    {
        var value = (raw ?? "F8").Trim().ToUpperInvariant();
        if (value.Length >= 2 && value[0] == 'F' && int.TryParse(value[1..], out var number) && number is >= 1 and <= 12)
        {
            key = Key.F1 + (number - 1);
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out key))
        {
            return true;
        }

        key = Key.F8;
        return false;
    }
}
