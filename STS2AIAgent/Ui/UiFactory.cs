using Godot;
using STS2AIAgent.Localization;

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

    /// <summary>
    /// Metadata key holding the semantic facts a control was built with, so
    /// <see cref="ReapplyButtonTheme"/> and <see cref="ReapplyLabelTheme"/> can repaint it correctly.
    /// </summary>
    private const string MetaKind = "sts2_button_kind";
    private const string MetaMuted = "sts2_label_muted";
    private const string MetaAccent = "sts2_label_accent";

    /// <summary>What a bare <see cref="Panel"/> draws, so a live theme switch can repaint it.</summary>
    private const string MetaPanel = "sts2_panel_role";
    private const string PanelAccentBar = "accent-bar";

    /// <summary>
    /// What a <see cref="PanelContainer"/> is styled as. A live theme switch repaints from this.
    /// </summary>
    /// <remarks>
    /// This exists because the first version of the live switch did not know about panel containers at
    /// all: it repainted labels and buttons and left every panel painted in the previous palette, so
    /// choosing the light theme gave light cards on a dark chrome --
    /// observed live on 2026-09-21. The role is recorded where the style is applied, so the style and
    /// its repaint cannot disagree.
    /// </remarks>
    private const string MetaSurface = "sts2_surface_role";
    private const string SurfaceChrome = "chrome";
    private const string SurfaceChromeRaised = "chrome-raised";
    private const string SurfaceCard = "card";
    private const string SurfaceMetric = "metric";
    private const string SurfaceTinted = "tinted";
    private const string MetaTint = "sts2_surface_tint";
    private const string MetaRadius = "sts2_surface_radius";

    /// <summary>How a panel container is styled, recorded on it so a live theme switch can repaint it.</summary>
    public enum SurfaceRole
    {
        Chrome,
        ChromeRaised,
        Card,
        Metric,
        Tinted
    }

    /// <summary>
    /// Styles a panel container and records the role, so <see cref="ReapplySurfaceTheme"/> can rebuild
    /// the same box in another palette.
    /// </summary>
    /// <remarks>
    /// One entry point rather than a style here and a metadata write there: what a surface is painted
    /// as and what it is repainted as have to be the same decision, and two call sites drift.
    /// </remarks>
    public static void TagSurface(PanelContainer panel, SurfaceRole role, int radius = 0)
    {
        panel.SetMeta(MetaSurface, role switch
        {
            SurfaceRole.Chrome => SurfaceChrome,
            SurfaceRole.ChromeRaised => SurfaceChromeRaised,
            SurfaceRole.Card => SurfaceCard,
            SurfaceRole.Metric => SurfaceMetric,
            _ => SurfaceTinted
        });
        panel.SetMeta(MetaRadius, radius);
        ReapplySurfaceTheme(panel);
    }

    /// <summary>
    /// Repaints a panel container this factory styled, in the active palette.
    /// </summary>
    /// <remarks>
    /// Without this a live theme switch gives light cards on a dark chrome, because a
    /// <c>PanelContainer</c> keeps the stylebox it was built with.
    /// </remarks>
    public static void ReapplySurfaceTheme(PanelContainer panel)
    {
        var role = panel.GetMeta(MetaSurface, "").AsString();
        var radius = panel.GetMeta(MetaRadius, 0).AsInt32();
        switch (role)
        {
            case SurfaceChrome:
                panel.AddThemeStyleboxOverride("panel", PanelStyle(radius: radius));
                break;
            case SurfaceChromeRaised:
                panel.AddThemeStyleboxOverride("panel", PanelStyle(ToGodot(_palette.SurfaceRaised), radius));
                break;
            case SurfaceCard:
                panel.AddThemeStyleboxOverride("panel", CardStyle());
                break;
            case SurfaceMetric:
                panel.AddThemeStyleboxOverride(
                    "panel",
                    Box(ToGodot(_palette.SurfaceRaised), ToGodot(_palette.Border), 6, borderWidth: 1, padX: SpaceSm, padY: SpaceSm, shadowSize: 2));
                break;
            case SurfaceTinted:
                {
                    var tone = (ButtonKind)panel.GetMeta(MetaTint, (int)ButtonKind.Normal).AsInt32();
                    var (background, foreground) = ToneStyle(tone);
                    panel.AddThemeStyleboxOverride("panel", ChipStyle(background, foreground.Darkened(0.40f)));
                    break;
                }
        }
    }

    /// <summary>
    /// Repaints a panel this factory built from the active palette.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="Panel"/> is invisible to a walk that only recognises typed controls, so the
    /// role is recorded on it. Without this the card headings keep the old theme's accent bar after a
    /// live switch -- a stale stripe next to freshly coloured text.
    /// </remarks>
    public static void ReapplyPanelTheme(Panel panel)
    {
        if (panel.GetMeta(MetaPanel, "").AsString() == PanelAccentBar)
        {
            panel.AddThemeStyleboxOverride("panel", Box(ToGodot(_palette.Accent), new Color(0, 0, 0, 0), 2, 0, 0, 0));
        }
    }

    internal static StyleBoxFlat Box(
        Color background,
        Color border,
        int radius,
        int borderWidth = 1,
        int padX = SpaceMd,
        int padY = SpaceSm,
        int shadowSize = 0,
        Color? shadowColor = null,
        Vector2? shadowOffset = null)
    {
        var box = new StyleBoxFlat
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

        if (shadowSize > 0)
        {
            box.ShadowSize = shadowSize;
            box.ShadowColor = shadowColor ?? new Color(0f, 0f, 0f, 0.40f);
            box.ShadowOffset = shadowOffset ?? new Vector2(0, 3);
        }

        return box;
    }

    /// <summary>
    /// The panel behind the whole overlay: one surface, one border, soft corner and depth shadow.
    /// </summary>
    /// <remarks>
    /// A light surface gets no drop shadow at all, only its border. A shadow tuned for a near-black
    /// panel is a smudge on an ivory one even after its opacity is scaled down -- reported live on
    /// 2026-09-21 as "the ivory theme's shadow is too harsh" -- and light interfaces separate layers
    /// with borders and fills rather than with darkness. <see cref="HasDropShadow"/> is the test.
    /// </remarks>
    public static StyleBoxFlat PanelStyle(Color? color = null, int radius = 10)
    {
        var background = color ?? ToGodot(_palette.Backdrop);
        return Box(
            background,
            ToGodot(_palette.Border),
            radius,
            borderWidth: 1,
            padX: SpaceMd,
            padY: SpaceMd,
            shadowSize: HasDropShadow(background) ? 18 : 0,
            shadowColor: new Color(0f, 0f, 0f, 0.55f),
            shadowOffset: new Vector2(0, 6));
    }

    /// <summary>A section card: the raised surface that separates one group of rows from the next.</summary>
    public static StyleBoxFlat CardStyle()
    {
        var background = ToGodot(_palette.Surface);
        return Box(
            background,
            ToGodot(_palette.Border),
            8,
            borderWidth: 1,
            padX: SpaceMd,
            padY: SpaceMd,
            shadowSize: HasDropShadow(background) ? 6 : 0,
            shadowColor: new Color(0f, 0f, 0f, 0.25f),
            shadowOffset: new Vector2(0, 2));
    }

    /// <summary>
    /// Whether a surface dark enough to carry a drop shadow. Light surfaces do not.
    /// </summary>
    /// <remarks>
    /// The threshold sits above every dark preset in the catalogue and below ivory, so the dark themes
    /// keep the depth they were designed with and the light one gets borders instead.
    /// </remarks>
    private static bool HasDropShadow(Color background)
    {
        var luminance = (0.2126f * background.R) + (0.7152f * background.G) + (0.0722f * background.B);
        return luminance < 0.5f;
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

        // The kind is remembered so the button can be repainted when the palette changes; without it a
        // live theme switch would have to guess whether a control was primary, ghost or danger.
        button.SetMeta(MetaKind, (int)kind);
        ApplyButtonTheme(button, kind);
        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    /// <summary>Repaints an existing button in the active palette, keeping the kind it was made with.</summary>
    public static void ReapplyButtonTheme(Button button)
    {
        var kind = (ButtonKind)button.GetMeta(MetaKind, (int)ButtonKind.Normal).AsInt32();
        ApplyButtonTheme(button, kind);
    }

    private static void ApplyButtonTheme(Button button, ButtonKind kind)
    {
        var border = ToGodot(_palette.Border);
        Color background;
        Color text;
        Color hover;
        Color pressed;
        Color borderColor = border;
        int shadow = 0;

        switch (kind)
        {
            case ButtonKind.Primary:
                background = ToGodot(_palette.Accent);
                text = ToGodot(_palette.AccentText);
                hover = background.Lightened(0.12f);
                pressed = background.Darkened(0.12f);
                borderColor = background.Lightened(0.25f);
                shadow = 4;
                break;
            case ButtonKind.Ghost:
                background = new Color(1f, 1f, 1f, 0.04f);
                text = ToGodot(_palette.Muted);
                hover = new Color(1f, 1f, 1f, 0.10f);
                pressed = new Color(1f, 1f, 1f, 0.02f);
                borderColor = new Color(1f, 1f, 1f, 0.08f);
                break;
            case ButtonKind.Danger:
                background = ToGodot(_palette.SurfaceRaised);
                text = ToGodot(_palette.Danger);
                hover = ToGodot(_palette.SurfaceRaised).Lightened(0.10f);
                pressed = ToGodot(_palette.SurfaceRaised).Darkened(0.10f);
                borderColor = ToGodot(_palette.Danger).Darkened(0.35f);
                break;
            default:
                background = ToGodot(_palette.SurfaceRaised);
                text = ToGodot(_palette.Text);
                hover = background.Lightened(0.10f);
                pressed = background.Darkened(0.10f);
                borderColor = border;
                shadow = 2;
                break;
        }

        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", kind == ButtonKind.Primary ? text : text.Lightened(0.15f));
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_disabled_color", ToGodot(_palette.Muted));

        button.AddThemeStyleboxOverride("normal", Box(background, borderColor, 6, shadowSize: shadow));
        button.AddThemeStyleboxOverride("hover", Box(hover, kind == ButtonKind.Primary ? borderColor.Lightened(0.15f) : border.Lightened(0.20f), 6, shadowSize: shadow + 2));
        button.AddThemeStyleboxOverride("pressed", Box(pressed, borderColor, 6));
        button.AddThemeStyleboxOverride("disabled", Box(background.Darkened(0.25f), border.Darkened(0.25f), 6));
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
        var idleBackground = new Color(1f, 1f, 1f, 0.035f);
        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeColorOverride("font_color", active ? ToGodot(_palette.AccentText) : ToGodot(_palette.Muted));
        button.AddThemeColorOverride("font_hover_color", active ? ToGodot(_palette.AccentText) : ToGodot(_palette.Text));
        button.AddThemeColorOverride("font_pressed_color", ToGodot(_palette.AccentText));
        button.AddThemeStyleboxOverride(
            "normal",
            Box(active ? activeBackground : idleBackground, active ? activeBackground.Lightened(0.20f) : new Color(0, 0, 0, 0), 6, padX: SpaceSm + 2, padY: 6, shadowSize: active ? 4 : 0));
        button.AddThemeStyleboxOverride(
            "hover",
            Box(active ? activeBackground.Lightened(0.10f) : new Color(1f, 1f, 1f, 0.09f), active ? activeBackground.Lightened(0.30f) : border, 6, padX: SpaceSm + 2, padY: 6));
        button.AddThemeStyleboxOverride(
            "pressed",
            Box(active ? activeBackground.Darkened(0.12f) : new Color(1f, 1f, 1f, 0.05f), border, 6, padX: SpaceSm + 2, padY: 6));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    /// <summary>
    /// A label. Wrapping is <b>off</b> by default; use <see cref="Wrapped"/> for text that has to
    /// reflow.
    /// </summary>
    /// <remarks>
    /// This default is the fix for a defect found in the live pass on 2026-09-21, and it is worth
    /// stating plainly because the obvious default is the wrong one: a <c>WordSmart</c> label reports a
    /// **combined minimum width of 1 pixel**. Containers that size themselves from minimums --
    /// <c>PanelContainer</c> around a column, and the column inside a <c>ScrollContainer</c> -- then
    /// collapse: measured live, one card came out 50 pixels wide with its heading label at
    /// <c>min=(1, 23)</c>, which is why headings rendered one character per line. With wrapping off, a
    /// label reports the width its text actually needs, and the containers above it get a real floor.
    ///
    /// The cost is that a label longer than its container is clipped rather than reflowed, so the few
    /// genuinely long strings take <see cref="Wrapped"/>.
    /// </remarks>
    public static Label Label(string text, int size = FontBody, bool muted = false, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.SetMeta(MetaMuted, muted);
        ApplyLabelColor(label);
        return label;
    }

    /// <summary>Repaints an existing label's colour in the active palette.</summary>
    public static void ReapplyLabelTheme(Label label)
    {
        ApplyLabelColor(label);
    }

    private static void ApplyLabelColor(Label label)
    {
        if (label.HasMeta(MetaAccent))
        {
            label.AddThemeColorOverride("font_color", ToGodot(_palette.Accent));
            return;
        }

        var muted = label.GetMeta(MetaMuted, false).AsBool();
        label.AddThemeColorOverride("font_color", muted ? ToGodot(_palette.Muted) : ToGodot(_palette.Text));
    }

    /// <summary>
    /// A label that reflows, and therefore can never widen the column it sits in.
    /// </summary>
    /// <remarks>
    /// Use this for any text that can run to a sentence -- including the dynamic labels whose text is
    /// short when the control is built and long later. That case is the one that keeps escaping review:
    /// a status line created with <c>"-"</c> reports a two-pixel minimum at build time and only becomes
    /// the widest thing on the page once the runtime fills it in. Measured live on 2026-09-21: a status
    /// line pushed the co-op page's column to 577 pixels against a 440-pixel panel and clipped every
    /// card against its edge.
    ///
    /// The minimum width is asserted here rather than set with a comment, because a silently ignored
    /// assignment is how this shipped twice: <c>WordSmart</c> only reflows down to the width the
    /// container offers, and a container that measures by minimum has already asked the wrong question.
    /// </remarks>
    public static Label Wrapped(string text, int size = FontCaption, bool muted = true)
    {
        var label = Label(text, size, muted, wrap: true);
        label.CustomMinimumSize = new Vector2(MinReflowWidth, 0);
        if (label.CustomMinimumSize.X < MinReflowWidth)
        {
            GD.PushError(
                $"UiFactory.Wrapped could not set a {MinReflowWidth}px floor; a reflowing label without "
                + "one lets its parent grow past the panel.");
        }

        return label;
    }

    /// <summary>
    /// The width floor a reflowing label keeps: wide enough to be a paragraph, far under the panel.
    /// </summary>
    public const int MinReflowWidth = 120;

    /// <summary>A section heading inside a page: larger, in the accent colour.</summary>
    public static Label Heading(string text)
    {
        var label = Label(text, FontHeading);
        label.SetMeta(MetaAccent, true);
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
        var pill = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var (bg, fg) = ToneStyle(tone);
        pill.SetMeta(MetaSurface, SurfaceTinted);
        pill.SetMeta(MetaTint, (int)tone);
        pill.AddThemeStyleboxOverride("panel", Box(bg, fg.Darkened(0.40f), 10, borderWidth: 1, padX: SpaceSm, padY: 2));
        var label = Label(text, FontCaption);
        label.AddThemeColorOverride("font_color", fg);
        pill.AddChild(label);
        return pill;
    }

    /// <summary>
    /// The background and foreground a tone is drawn with, so a control that changes tone at runtime
    /// repaints with exactly the pair a fresh <see cref="PillBadge"/> of that tone would use.
    /// </summary>
    public static (Color Background, Color Foreground) ToneStyle(ButtonKind tone)
    {
        return tone switch
        {
            ButtonKind.Primary => (ToGodot(_palette.Positive).Darkened(0.70f), ToGodot(_palette.Positive)),
            ButtonKind.Danger => (ToGodot(_palette.Danger).Darkened(0.70f), ToGodot(_palette.Danger)),
            ButtonKind.Ghost => (new Color(1f, 1f, 1f, 0.06f), ToGodot(_palette.Muted)),
            _ => (ToGodot(_palette.Warning).Darkened(0.70f), ToGodot(_palette.Warning))
        };
    }

    /// <summary>
    /// The pill shape itself: the same rounded chip <see cref="PillBadge"/> draws, exposed so a
    /// control whose tone changes can rebuild just the box and keep the label it already has.
    /// </summary>
    public static StyleBoxFlat ChipStyle(Color background, Color border)
    {
        return Box(background, border, 10, borderWidth: 1, padX: SpaceSm, padY: 2);
    }

    /// <summary>
    /// The theme picker: one tile per preset, each drawn as the palette it selects.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole when the selection moves, for the same reason the overlay's tab row is: a Godot
    /// stylebox is set per control, and restyling seven tiles in place is more code than building
    /// them. It is seven controls on a click, not a per-frame cost. The selected tile is drawn with
    /// the accent border and the others are not, because "which one is mine" is the one thing a grid
    /// of swatches has to answer at a glance.
    /// </remarks>
    public static Control ThemeSwatchPicker(
        IReadOnlyList<OverlayPalette> presets,
        string selectedId,
        Action<string> onSelected)
    {
        // Pass, not Ignore. Ignore makes the container and its whole subtree invisible to the mouse --
        // the tiles inside a picker built that way never receive a click at all, which is exactly how
        // this shipped and was reported broken on 2026-09-21. Pass lets the tiles handle their own
        // input while the grid itself stays out of the way.
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", SpaceSm);
        grid.AddThemeConstantOverride("v_separation", SpaceSm);

        foreach (var preset in presets)
        {
            grid.AddChild(ThemeSwatch(preset, preset.Id == selectedId, onSelected));
        }

        return grid;
    }

    private static Control ThemeSwatch(OverlayPalette preset, bool selected, Action<string> onSelected)
    {
        // A panel rather than a Button: a button draws its own label, and this tile's label has to sit
        // under its swatch rather than beside it, so the tile positions its own children and handles
        // its own click.
        var tile = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        var idle = Box(
            ToGodot(_palette.Surface),
            selected ? ToGodot(_palette.Accent) : ToGodot(_palette.Border),
            8,
            borderWidth: selected ? 2 : 1,
            padX: 4,
            padY: 4);
        var hover = Box(ToGodot(_palette.SurfaceRaised), ToGodot(_palette.Accent), 8, borderWidth: selected ? 2 : 1, padX: 4, padY: 4);
        tile.AddThemeStyleboxOverride("panel", idle);

        var contents = Column();
        contents.AddThemeConstantOverride("separation", 2);
        contents.AddChild(OverlaySwatch.Build(preset));
        contents.AddChild(Label(Loc.T(preset.Label), FontCaption, muted: !selected));
        tile.AddChild(contents);

        tile.MouseEntered += () => tile.AddThemeStyleboxOverride("panel", hover);
        tile.MouseExited += () => tile.AddThemeStyleboxOverride("panel", idle);
        tile.GuiInput += evt =>
        {
            if (evt is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                onSelected(preset.Id);
            }
        };
        return tile;
    }

    /// <summary>
    /// A modern metric tile showing a label and prominent value, suitable for dashboards.
    /// </summary>
    public static Control MetricTile(string title, Label valueLabel, string? hint = null)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // A floor as well as the flag: three tiles share the row, and below this a tile is a
            // clipped word rather than a reading.
            CustomMinimumSize = new Vector2(96, 0)
        };
        panel.SetMeta(MetaSurface, SurfaceMetric);
        panel.AddThemeStyleboxOverride("panel", Box(ToGodot(_palette.SurfaceRaised), ToGodot(_palette.Border), 6, borderWidth: 1, padX: SpaceSm, padY: SpaceSm, shadowSize: 2));
        var col = Column();
        col.AddThemeConstantOverride("separation", 2);
        var caption = Label(title, FontCaption, muted: true);
        // The value is "-" when the tile is built and a sentence once the runtime fills it. An
        // unwrapped heading reports that sentence as its minimum width and stretches the whole row
        // past the panel. WordSmart plus ExpandFill lets the tile stay at its 96px floor.
        valueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        valueLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(caption);
        col.AddChild(valueLabel);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            col.AddChild(Label(hint, FontCaption, muted: true));
        }
        panel.AddChild(col);
        return panel;
    }

    /// <summary>
    /// A titled section: heading with left accent indicator bar, then the rows, on the raised surface.
    /// </summary>
    /// <remarks>
    /// The pages are long, and a flat column of twenty rows gives the eye nowhere to land. A card per
    /// topic costs one container and makes the panel scannable while a combat turn is running.
    /// </remarks>
    public static Control Card(string? title, params Control[] children)
    {
        // ExpandFill is load-bearing, not tidiness. A PanelContainer sizes itself to its content's
        // *minimum*, and a wrapping label reports a minimum width of 1 pixel -- so a card added to a
        // bare Control, or to a container that hands out minimums, collapses to a sliver and wraps its
        // heading one character per line. Measured live on 2026-09-21: a card in the chat page
        // reported size=(8, 77) with its heading label at min=(1, 23), and rendered as "对" / "话"
        // stacked. Every card asks for the full width instead of trusting the parent to offer it.
        var card = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        card.SetMeta(MetaSurface, SurfaceCard);
        card.AddThemeStyleboxOverride("panel", CardStyle());
        var column = Column();
        if (!string.IsNullOrEmpty(title))
        {
            var headerRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            headerRow.AddThemeConstantOverride("separation", SpaceXs + 2);

            var tag = new Panel
            {
                CustomMinimumSize = new Vector2(3, 14),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            tag.SetMeta(MetaPanel, PanelAccentBar);
            tag.AddThemeStyleboxOverride("panel", Box(ToGodot(_palette.Accent), new Color(0, 0, 0, 0), 2, 0, 0, 0));
            headerRow.AddChild(tag);
            headerRow.AddChild(Heading(title));
            column.AddChild(headerRow);
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
        ApplyLineTheme(edit);
        return edit;
    }

    /// <summary>Repaints an existing single-line editor in the active palette.</summary>
    public static void ReapplyLineTheme(LineEdit edit)
    {
        ApplyLineTheme(edit);
    }

    private static void ApplyLineTheme(LineEdit edit)
    {
        edit.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted));
        edit.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.Surface), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        edit.AddThemeStyleboxOverride("focus", Box(ToGodot(_palette.Surface), ToGodot(_palette.Accent), 6, padX: SpaceSm, padY: 6));
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
        ApplyCheckTheme(box);
        return box;
    }

    /// <summary>Repaints an existing checkbox in the active palette.</summary>
    public static void ReapplyCheckTheme(CheckBox box)
    {
        ApplyCheckTheme(box);
    }

    private static void ApplyCheckTheme(CheckBox box)
    {
        box.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        box.AddThemeColorOverride("font_hover_color", ToGodot(_palette.Accent));
        box.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public static OptionButton Combo()
    {
        var combo = new OptionButton { MouseFilter = Control.MouseFilterEnum.Stop };
        combo.AddThemeFontSizeOverride("font_size", FontBody);
        ApplyComboTheme(combo);
        return combo;
    }

    /// <summary>Repaints an existing drop-down in the active palette.</summary>
    public static void ReapplyComboTheme(OptionButton combo)
    {
        ApplyComboTheme(combo);
    }

    private static void ApplyComboTheme(OptionButton combo)
    {
        combo.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        combo.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.SurfaceRaised), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        combo.AddThemeStyleboxOverride("hover", Box(ToGodot(_palette.SurfaceRaised).Lightened(0.08f), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        combo.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
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
        ApplyMultilineTheme(edit);
        return edit;
    }

    /// <summary>Repaints an existing multi-line editor in the active palette.</summary>
    public static void ReapplyMultilineTheme(TextEdit edit)
    {
        ApplyMultilineTheme(edit);
    }

    private static void ApplyMultilineTheme(TextEdit edit)
    {
        edit.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted));
        edit.AddThemeStyleboxOverride("normal", Box(ToGodot(_palette.Surface), ToGodot(_palette.Border), 6, padX: SpaceSm, padY: 6));
        edit.AddThemeStyleboxOverride("focus", Box(ToGodot(_palette.Surface), ToGodot(_palette.Accent), 6, padX: SpaceSm, padY: 6));
    }

    public static RichTextLabel Rich(bool follow = true)
    {
        // A rich log is the one place a sentence never goes through Wrapped. Without an explicit
        // wrap and a width floor, Godot sizes the label to the longest unwrapped BBCode line, which
        // is how a model reason pushed the decision page past the 440px panel. Measured live on
        // 2026-09-22: "Play Dismantle (14 dmg..." ran off the right edge while the parent scroll
        // had horizontal scrolling disabled.
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollFollowing = follow,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(MinReflowWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        label.AddThemeFontSizeOverride("normal_font_size", FontBody);
        label.AddThemeColorOverride("default_color", ToGodot(_palette.Text));
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        return label;
    }

    /// <summary>Repaints an existing rich-text view's base colour in the active palette.</summary>
    public static void ReapplyRichTheme(RichTextLabel label)
    {
        label.AddThemeColorOverride("default_color", ToGodot(_palette.Text));
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
