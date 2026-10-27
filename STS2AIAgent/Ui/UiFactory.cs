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
/// branch in a page builder. What Godot draws by itself -- scroll bars, popups, tooltips, check box
/// icons, the font -- comes from <see cref="OverlayGodotTheme"/> on the host.
///
/// The visual rules, revised on 2026-10-27 after "the UI looks cheap": depth comes from fill, not
/// from outlines and shadows. The first version gave every card, button, tile and help panel a solid
/// border and a drop shadow (the help panel and the title bar inherited the window's 18 px one), and
/// drew every heading, accent bar, tab and switch in the accent colour, so nothing stood out because
/// everything did. Now cards are one step lighter than the window with a hairline edge, wells
/// (fields, logs, tiles, help) are one step darker, the accent is kept for the primary action, the
/// selected tab's underline, focus and switches, and headings carry weight instead of colour.
/// </remarks>
internal static partial class UiFactory
{
    /// <summary>The active palette. Set once at build and again when the theme setting changes.</summary>
    private static OverlayPalette _palette = OverlayThemeCatalog.Resolve(OverlayThemeCatalog.DefaultId);

    // Spacing scale. Four values rather than free numbers, because the thing that made the old layout
    // look unfinished was inconsistent gaps far more than any single wrong gap.
    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 18;

    // Corner radii. Cards are rounder than the controls inside them, so a button nests in its card
    // instead of echoing the same corner.
    public const int RadiusControl = 8;
    public const int RadiusCard = 10;
    public const int RadiusWindow = 12;
    private const int RadiusPill = 99;

    /// <summary>
    /// Vertical padding shared by buttons, fields and drop-downs, so a row mixing them lines up: the
    /// old values were 8, 6 and 6, and a field beside its button sat four pixels short.
    /// </summary>
    private const int ControlPadY = 7;
    private const int ControlPadX = 12;

    // Type scale.
    public const int FontTitle = 16;
    public const int FontHeading = 14;
    public const int FontBody = 13;
    public const int FontCaption = 12;

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

    /// <summary>The well colour: fields, logs, tiles and help panels sit one step below the card.</summary>
    private static Color Inset => ToGodot(_palette.Backdrop) with { A = 1f };

    /// <summary>
    /// The colour that reads as "lifted" off a well: the raised surface on the dark themes, the
    /// paper surface on the light one, where the raised tone is darker than the backdrop.
    /// </summary>
    private static Color Lifted => IsDark(Inset) ? ToGodot(_palette.SurfaceRaised) : ToGodot(_palette.Surface);

    /// <summary>How a button is meant to read: the page's main action, an ordinary one, or a caution.</summary>
    public enum ButtonKind
    {
        Normal,
        Primary,
        Ghost,
        Danger,

        /// <summary>The edge tab: drawn over the game rather than inside the panel.</summary>
        Floating
    }

    /// <summary>
    /// Metadata key holding the semantic facts a control was built with, so
    /// <see cref="ReapplyButtonTheme"/> and <see cref="ReapplyLabelTheme"/> can repaint it correctly.
    /// </summary>
    private const string MetaKind = "sts2_button_kind";

    /// <summary>
    /// Metadata key on a tab or segment button: whether it is the selected one. Its presence is what
    /// tells <see cref="ReapplyButtonTheme"/> to repaint a tab rather than an ordinary button.
    /// </summary>
    private const string MetaTabActive = "sts2_tab_active";

    /// <summary>Present on a segment of a <see cref="Ui.SegmentedSwitch"/>, which is drawn unlike a page tab.</summary>
    private const string MetaSegment = "sts2_tab_segment";
    private const string MetaMuted = "sts2_label_muted";
    private const string MetaAccent = "sts2_label_accent";

    /// <summary>A label drawn in a status tone; the repaint keeps the tone instead of resetting it.</summary>
    private const string MetaTone = "sts2_label_tone";

    /// <summary>What a bare <see cref="Panel"/> draws, so a live theme switch can repaint it.</summary>
    private const string MetaPanel = "sts2_panel_role";
    private const string PanelDot = "dot";
    private const string PanelDivider = "divider";

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
    private const string SurfaceBare = "bare";
    private const string SurfaceInset = "inset";
    private const string SurfaceTrack = "track";
    private const string SurfaceCallout = "callout";
    private const string MetaTint = "sts2_surface_tint";
    private const string MetaRadius = "sts2_surface_radius";

    /// <summary>How a panel container is styled, recorded on it so a live theme switch can repaint it.</summary>
    public enum SurfaceRole
    {
        /// <summary>The window itself.</summary>
        Chrome,

        /// <summary>A card-like box built outside <see cref="Card"/> (the endpoint and model editors).</summary>
        ChromeRaised,
        Card,
        Metric,
        Tinted,

        /// <summary>No fill and no edge: a container that only needs padding (the title bar).</summary>
        Bare,

        /// <summary>A well one step below the card: help panels, read-outs.</summary>
        Inset,

        /// <summary>The groove a segmented switch's segments sit in.</summary>
        Track,

        /// <summary>A tinted notice that spans the page, toned by <see cref="MetaTint"/>.</summary>
        Callout
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
            SurfaceRole.Bare => SurfaceBare,
            SurfaceRole.Inset => SurfaceInset,
            SurfaceRole.Track => SurfaceTrack,
            SurfaceRole.Callout => SurfaceCallout,
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
        var tone = (ButtonKind)panel.GetMeta(MetaTint, (int)ButtonKind.Normal).AsInt32();
        switch (role)
        {
            case SurfaceChrome:
                panel.AddThemeStyleboxOverride("panel", PanelStyle(radius: radius > 0 ? radius : RadiusWindow));
                break;
            case SurfaceChromeRaised:
            case SurfaceCard:
                panel.AddThemeStyleboxOverride("panel", CardStyle(radius > 0 ? radius : RadiusCard));
                break;
            case SurfaceMetric:
            case SurfaceInset:
                panel.AddThemeStyleboxOverride("panel", InsetStyle(radius > 0 ? radius : RadiusControl));
                break;
            case SurfaceBare:
                panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
                break;
            case SurfaceTrack:
                panel.AddThemeStyleboxOverride("panel", Box(Inset, Veil(0.06f), RadiusControl + 1, padX: 3, padY: 3));
                break;
            case SurfaceCallout:
                {
                    var (background, _) = ToneStyle(tone);
                    panel.AddThemeStyleboxOverride("panel", Box(background, new Color(0, 0, 0, 0), RadiusCard, borderWidth: 0, padX: SpaceMd, padY: SpaceSm + 2));
                    break;
                }
            case SurfaceTinted:
                {
                    var (background, _) = ToneStyle(tone);
                    panel.AddThemeStyleboxOverride("panel", ChipStyle(background, new Color(0, 0, 0, 0)));
                    break;
                }
        }
    }

    /// <summary>
    /// Repaints a panel this factory built from the active palette.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="Panel"/> is invisible to a walk that only recognises typed controls, so the
    /// role is recorded on it. Without this a status dot keeps the old theme's colour after a live
    /// switch, beside a label already redrawn in the new one.
    /// </remarks>
    public static void ReapplyPanelTheme(Panel panel)
    {
        switch (panel.GetMeta(MetaPanel, "").AsString())
        {
            case PanelDot:
                var tone = (ButtonKind)panel.GetMeta(MetaTint, (int)ButtonKind.Ghost).AsInt32();
                panel.AddThemeStyleboxOverride("panel", Box(ToneStyle(tone).Foreground, new Color(0, 0, 0, 0), RadiusPill, borderWidth: 0, padX: 0, padY: 0));
                break;
            case PanelDivider:
                panel.AddThemeStyleboxOverride("panel", Box(Veil(0.08f), new Color(0, 0, 0, 0), 0, borderWidth: 0, padX: 0, padY: 0));
                break;
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
            BorderColor = border,
            AntiAliasing = true
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
    /// The panel behind the whole overlay: one surface, a hairline edge, soft corners and a depth shadow.
    /// </summary>
    /// <remarks>
    /// A light surface gets no drop shadow at all, only its border. A shadow tuned for a near-black
    /// panel is a smudge on an ivory one even after its opacity is scaled down -- reported live on
    /// 2026-09-21 as "the ivory theme's shadow is too harsh" -- and light interfaces separate layers
    /// with borders and fills rather than with darkness. <see cref="IsDark"/> is the test.
    ///
    /// Only the window uses this. The title bar and the help panels used to as well, which gave each
    /// of them the window's 18 px shadow inside the window.
    /// </remarks>
    public static StyleBoxFlat PanelStyle(Color? color = null, int radius = RadiusWindow)
    {
        var background = color ?? ToGodot(_palette.Backdrop);
        return Box(
            background,
            Veil(0.14f),
            radius,
            borderWidth: 1,
            padX: SpaceMd + 2,
            padY: SpaceMd,
            shadowSize: IsDark(background) ? 18 : 0,
            shadowColor: new Color(0f, 0f, 0f, 0.55f),
            shadowOffset: new Vector2(0, 6));
    }

    /// <summary>
    /// A section card: one step lighter than the window, with a hairline edge and no shadow.
    /// </summary>
    public static StyleBoxFlat CardStyle(int radius = RadiusCard)
    {
        return Box(ToGodot(_palette.Surface), Veil(0.07f), radius, borderWidth: 1, padX: SpaceMd + 2, padY: SpaceMd);
    }

    /// <summary>A well: one step darker than the card, no edge. Fields, logs, tiles and help sit in one.</summary>
    private static StyleBoxFlat InsetStyle(int radius = RadiusControl, int padX = SpaceSm + 2, int padY = SpaceSm)
    {
        return Box(Inset, new Color(0, 0, 0, 0), radius, borderWidth: 0, padX: padX, padY: padY);
    }

    /// <summary>
    /// Whether a surface is dark enough to carry a drop shadow and a light knob. Light surfaces do not.
    /// </summary>
    /// <remarks>
    /// The threshold sits above every dark preset in the catalogue and below ivory, so the dark themes
    /// keep the depth they were designed with and the light one gets borders instead.
    /// </remarks>
    private static bool IsDark(Color background)
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
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
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
        // A tab is a button too, and the walk reaches it through the Button arm. Repainting it as an
        // ordinary button erased the selected-tab fill on every theme change, so after picking a
        // theme the header and the solo/multiplayer switch no longer showed where the player was.
        if (button.HasMeta(MetaTabActive))
        {
            var active = button.GetMeta(MetaTabActive, false).AsBool();
            if (button.HasMeta(MetaSegment)) ApplySegmentTheme(button, active);
            else ApplyTabTheme(button, active);
            return;
        }

        var kind = (ButtonKind)button.GetMeta(MetaKind, (int)ButtonKind.Normal).AsInt32();
        ApplyButtonTheme(button, kind);
    }

    /// <summary>
    /// A faint fill or edge derived from the theme's text colour: hairlines, hover washes, scroll thumbs.
    /// </summary>
    /// <remarks>
    /// These used to be white at a few percent opacity, which is a visible tint on the dark themes and
    /// nothing at all on ivory: ghost buttons and idle tabs there read as loose text with no edge.
    /// The text colour is light on dark themes and dark on light ones, so the same alpha works on both.
    /// </remarks>
    internal static Color Veil(float alpha)
    {
        var text = ToGodot(_palette.Text);
        return new Color(text.R, text.G, text.B, alpha);
    }

    /// <summary>A colour moved towards the text colour: lighter on the dark themes, darker on ivory.</summary>
    private static Color Toward(Color color, float weight) => color.Lerp(ToGodot(_palette.Text) with { A = color.A }, weight);

    private static void ApplyButtonTheme(Button button, ButtonKind kind)
    {
        var clear = new Color(0, 0, 0, 0);
        var text = ToGodot(_palette.Text);
        Color background;
        Color foreground;
        Color hover;
        Color pressed;
        Color edge = clear;
        var radius = RadiusControl;
        var padX = ControlPadX;
        var shadow = 0;

        switch (kind)
        {
            case ButtonKind.Primary:
                background = ToGodot(_palette.Accent);
                foreground = ToGodot(_palette.AccentText);
                hover = background.Lightened(0.08f);
                pressed = background.Darkened(0.10f);
                break;
            case ButtonKind.Ghost:
                background = clear;
                foreground = ToGodot(_palette.Muted);
                hover = Veil(0.08f);
                pressed = Veil(0.04f);
                padX = SpaceSm + 2;
                break;
            case ButtonKind.Danger:
                var danger = ToGodot(_palette.Danger);
                background = danger with { A = 0.12f };
                foreground = danger;
                hover = danger with { A = 0.20f };
                pressed = danger with { A = 0.08f };
                break;
            case ButtonKind.Floating:
                background = ToGodot(_palette.Backdrop);
                foreground = ToGodot(_palette.Accent);
                hover = Toward(background, 0.08f);
                pressed = background;
                edge = Veil(0.16f);
                radius = RadiusCard;
                padX = 0;
                shadow = 8;
                break;
            default:
                background = ToGodot(_palette.SurfaceRaised);
                foreground = text;
                hover = Toward(background, 0.07f);
                pressed = Toward(background, 0.02f);
                edge = Veil(0.08f);
                break;
        }

        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeFontOverride("font", kind is ButtonKind.Primary or ButtonKind.Floating ? OverlayGodotTheme.Bold : OverlayGodotTheme.Regular);
        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", kind == ButtonKind.Ghost ? text : foreground);
        button.AddThemeColorOverride("font_pressed_color", kind == ButtonKind.Ghost ? text : foreground);
        button.AddThemeColorOverride("font_hover_pressed_color", kind == ButtonKind.Ghost ? text : foreground);
        button.AddThemeColorOverride("font_disabled_color", ToGodot(_palette.Muted) with { A = 0.65f });

        var shadowColor = new Color(0f, 0f, 0f, 0.35f);
        button.AddThemeStyleboxOverride("normal", Box(background, edge, radius, padX: padX, padY: ControlPadY, shadowSize: shadow, shadowColor: shadowColor));
        button.AddThemeStyleboxOverride("hover", Box(hover, edge, radius, padX: padX, padY: ControlPadY, shadowSize: shadow, shadowColor: shadowColor));
        button.AddThemeStyleboxOverride("pressed", Box(pressed, edge, radius, padX: padX, padY: ControlPadY, shadowSize: shadow, shadowColor: shadowColor));
        button.AddThemeStyleboxOverride("hover_pressed", Box(pressed, edge, radius, padX: padX, padY: ControlPadY, shadowSize: shadow, shadowColor: shadowColor));
        // Disabled is the same shape faded, for every kind. Darkening the fill turned a disabled
        // primary button into a brown slab that read as a different, broken control.
        button.AddThemeStyleboxOverride("disabled", Box(kind == ButtonKind.Ghost ? clear : Veil(0.05f), clear, radius, padX: padX, padY: ControlPadY));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>
    /// One tab in the header row, drawn as an underline: the selected tab in the text colour over an
    /// accent bar, the others muted over a hairline that runs the width of the row.
    /// </summary>
    /// <remarks>
    /// <c>ClipText</c> stays off. Godot's clipped button reports a minimum width that does not include
    /// its label, so a tab row laid out by natural width collapsed to five empty slivers with the
    /// text cut off -- observed live on 2026-09-20 in the first pass of this redesign. The label has
    /// to drive the button's size for a wrapping row to work.
    ///
    /// The tabs were filled accent pills, and the solo/multiplayer switch right under them was too, so
    /// the top of the play page was two stacked bars of the brightest colour on screen. An underline
    /// says "you are here" without competing with the page's own primary action.
    /// </remarks>
    public static Button TabButton(string text, bool active, Action? onPressed)
    {
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            FocusMode = Control.FocusModeEnum.None
        };

        button.SetMeta(MetaTabActive, active);
        ApplyTabTheme(button, active);
        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    private static void ApplyTabTheme(Button button, bool active)
    {
        StyleBoxFlat Underline(Color bar, int width)
        {
            var box = Box(new Color(0, 0, 0, 0), bar, 0, borderWidth: 0, padX: SpaceSm + 2, padY: SpaceSm);
            box.BorderWidthBottom = width;
            return box;
        }

        var text = ToGodot(_palette.Text);
        var muted = ToGodot(_palette.Muted);
        var accent = ToGodot(_palette.Accent);
        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeFontOverride("font", active ? OverlayGodotTheme.Bold : OverlayGodotTheme.Regular);
        button.AddThemeColorOverride("font_color", active ? text : muted);
        button.AddThemeColorOverride("font_hover_color", text);
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_hover_pressed_color", text);
        button.AddThemeStyleboxOverride("normal", Underline(active ? accent : Veil(0.10f), active ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", Underline(active ? accent : Veil(0.28f), active ? 2 : 1));
        button.AddThemeStyleboxOverride("pressed", Underline(accent, 2));
        button.AddThemeStyleboxOverride("hover_pressed", Underline(accent, 2));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>
    /// One segment of a <see cref="Ui.SegmentedSwitch"/>: the selected segment lifted off the groove,
    /// the others flat and muted.
    /// </summary>
    public static Button SegmentButton(string text, bool active, Action? onPressed)
    {
        var button = TabButton(text, active, onPressed);
        button.SetMeta(MetaSegment, true);
        ApplySegmentTheme(button, active);
        return button;
    }

    private static void ApplySegmentTheme(Button button, bool active)
    {
        var clear = new Color(0, 0, 0, 0);
        var text = ToGodot(_palette.Text);
        var lifted = Box(Lifted, Veil(0.08f), RadiusControl - 1, padX: SpaceSm + 2, padY: 5);
        button.AddThemeFontSizeOverride("font_size", FontBody);
        button.AddThemeFontOverride("font", active ? OverlayGodotTheme.Bold : OverlayGodotTheme.Regular);
        button.AddThemeColorOverride("font_color", active ? text : ToGodot(_palette.Muted));
        button.AddThemeColorOverride("font_hover_color", text);
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_hover_pressed_color", text);
        button.AddThemeStyleboxOverride("normal", active ? lifted : Box(clear, clear, RadiusControl - 1, borderWidth: 0, padX: SpaceSm + 2, padY: 5));
        button.AddThemeStyleboxOverride("hover", active ? lifted : Box(Veil(0.05f), clear, RadiusControl - 1, borderWidth: 0, padX: SpaceSm + 2, padY: 5));
        button.AddThemeStyleboxOverride("pressed", lifted);
        button.AddThemeStyleboxOverride("hover_pressed", lifted);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>
    /// A two-or-more-way exclusive switch drawn as segments in a groove, exactly one of them lifted.
    /// </summary>
    /// <remarks>
    /// This is the play page's solo/multiplayer mode switch and the MCP client picker. It rebuilds
    /// its buttons on each select rather than restyling in place -- the same trade the tab row makes,
    /// because a Godot <c>StyleBox</c> is set per control and swapping two buttons is cheaper to read
    /// than mutating two sets of overrides.
    /// </remarks>
    public static SegmentedSwitch SegmentedSwitch(IReadOnlyList<string> options, int selected, Action<int>? onSelected)
    {
        return new SegmentedSwitch(options, selected, onSelected);
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
        if (label.HasMeta(MetaTone))
        {
            label.AddThemeColorOverride("font_color", ToneText((ButtonKind)label.GetMeta(MetaTone, (int)ButtonKind.Ghost).AsInt32()));
            return;
        }

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

    /// <summary>A card heading: bold, in the text colour. The accent is kept for things that act.</summary>
    public static Label Heading(string text)
    {
        return Bold(Label(text, FontHeading));
    }

    /// <summary>
    /// A settings group label: small, bold and muted, with room above it. It names the cards under it
    /// rather than competing with their own headings.
    /// </summary>
    public static Label SectionLabel(string text)
    {
        var label = Bold(Label(text, FontCaption, muted: true));
        label.CustomMinimumSize = new Vector2(0, 26);
        label.VerticalAlignment = VerticalAlignment.Bottom;
        return label;
    }

    /// <summary>Sets a label in the bold face.</summary>
    public static Label Bold(Label label)
    {
        label.AddThemeFontOverride("font", OverlayGodotTheme.Bold);
        return label;
    }

    /// <summary>
    /// Draws a label in the accent colour and records that, so a theme change repaints it in the new
    /// accent instead of resetting it to body text (which is what happened to the window title).
    /// </summary>
    public static Label MarkAccent(Label label)
    {
        label.SetMeta(MetaAccent, true);
        label.AddThemeColorOverride("font_color", ToGodot(_palette.Accent));
        return label;
    }

    /// <summary>
    /// A read-out tile: a muted caption over its value, in a well.
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
        panel.AddThemeStyleboxOverride("panel", InsetStyle());
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

    /// <summary>The name of a card's title row, so <see cref="HelpCard"/> can find it to add its "?" button.</summary>
    public const string CardHeaderName = "CardHeader";

    /// <summary>
    /// A titled section: a bold heading, then the rows, on the card surface.
    /// </summary>
    /// <remarks>
    /// The pages are long, and a flat column of twenty rows gives the eye nowhere to land. A card per
    /// topic costs one container and makes the panel scannable while a combat turn is running.
    ///
    /// The heading had an accent bar beside it and was itself drawn in the accent; with a card per
    /// topic that was a dozen accent marks per page. Weight separates the heading from its rows.
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
        column.AddThemeConstantOverride("separation", SpaceSm + 2);
        if (!string.IsNullOrEmpty(title))
        {
            var headerRow = new HBoxContainer { Name = CardHeaderName, MouseFilter = Control.MouseFilterEnum.Ignore };
            headerRow.AddThemeConstantOverride("separation", SpaceXs + 2);
            var heading = Heading(title);
            heading.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            headerRow.AddChild(heading);
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

    /// <summary>A one-pixel hairline across the column.</summary>
    public static Control Divider()
    {
        var divider = new Panel
        {
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        divider.SetMeta(MetaPanel, PanelDivider);
        ReapplyPanelTheme(divider);
        return divider;
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
        label.AddThemeFontSizeOverride("bold_font_size", FontBody);
        label.AddThemeFontOverride("bold_font", OverlayGodotTheme.Bold);
        ApplyRichTheme(label);
        return label;
    }

    /// <summary>Repaints an existing rich-text view's base colour and well in the active palette.</summary>
    public static void ReapplyRichTheme(RichTextLabel label)
    {
        ApplyRichTheme(label);
    }

    /// <summary>
    /// Logs sit in a well, so the conversation and the decision record read as a read-out area of the
    /// card rather than loose text running into the controls below it.
    /// </summary>
    private static void ApplyRichTheme(RichTextLabel label)
    {
        label.AddThemeColorOverride("default_color", ToGodot(_palette.Text));
        label.AddThemeStyleboxOverride("normal", InsetStyle(padX: SpaceSm + 2, padY: SpaceSm));
        label.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
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

    /// <summary>A page's column of cards: the same column with a wider gap between its cards.</summary>
    public static VBoxContainer PageColumn()
    {
        var column = Column();
        column.AddThemeConstantOverride("separation", SpaceMd);
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
}
