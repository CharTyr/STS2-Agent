using Godot;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The controls a player sets a value with: fields, switches, drop-downs and the theme picker.
/// </summary>
/// <remarks>
/// Split out of <c>UiFactory.cs</c> when the 2026-10-27 visual pass took that file past the size
/// budget. Fields and drop-downs share one well style, <see cref="FieldStyle"/>, so a drop-down beside
/// a text field reads as the same kind of thing at the same height.
/// </remarks>
internal static partial class UiFactory
{
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
            Inset,
            selected ? ToGodot(_palette.Accent) : new Color(0, 0, 0, 0),
            RadiusControl,
            borderWidth: selected ? 2 : 0,
            padX: 6,
            padY: 6);
        var hover = Box(Inset, selected ? ToGodot(_palette.Accent) : Veil(0.24f), RadiusControl, borderWidth: selected ? 2 : 1, padX: 6, padY: 6);
        tile.AddThemeStyleboxOverride("panel", idle);

        var contents = Column();
        contents.AddThemeConstantOverride("separation", 4);
        contents.AddChild(OverlaySwatch.Build(preset));
        var name = Label(Loc.T(preset.Label), FontCaption, muted: !selected);
        if (selected) Bold(name);
        contents.AddChild(name);
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

    /// <summary>A field: a well with a hairline that turns accent on focus.</summary>
    private static StyleBoxFlat FieldStyle(bool focused) =>
        Box(Inset, focused ? ToGodot(_palette.Accent) : Veil(0.10f), RadiusControl, padX: SpaceSm + 2, padY: ControlPadY);

    private static void ApplyLineTheme(LineEdit edit)
    {
        edit.AddThemeColorOverride("font_color", ToGodot(_palette.Text));
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted) with { A = 0.75f });
        edit.AddThemeStyleboxOverride("normal", FieldStyle(focused: false));
        edit.AddThemeStyleboxOverride("focus", FieldStyle(focused: true));
        edit.AddThemeStyleboxOverride("read_only", FieldStyle(focused: false));
    }

    public static CheckBox Check(string text, bool on)
    {
        var box = new CheckBox
        {
            Text = text,
            ButtonPressed = on,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            FocusMode = Control.FocusModeEnum.None
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

    /// <summary>
    /// The label colours. The switch icons and the empty boxes behind them come from
    /// <see cref="OverlayGodotTheme"/>, which is rebuilt with the palette.
    /// </summary>
    private static void ApplyCheckTheme(CheckBox box)
    {
        var text = ToGodot(_palette.Text);
        box.AddThemeColorOverride("font_color", text);
        box.AddThemeColorOverride("font_hover_color", text);
        box.AddThemeColorOverride("font_pressed_color", text);
        box.AddThemeColorOverride("font_hover_pressed_color", text);
        box.AddThemeColorOverride("font_disabled_color", ToGodot(_palette.Muted));
        box.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public static OptionButton Combo()
    {
        var combo = new OptionButton
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            FocusMode = Control.FocusModeEnum.None
        };
        combo.AddThemeFontSizeOverride("font_size", FontBody);
        ApplyComboTheme(combo);
        return combo;
    }

    /// <summary>Repaints an existing drop-down in the active palette.</summary>
    public static void ReapplyComboTheme(OptionButton combo)
    {
        ApplyComboTheme(combo);
    }

    /// <summary>A drop-down reads as a field you can open: the field's well, its hairline brightening on hover.</summary>
    private static void ApplyComboTheme(OptionButton combo)
    {
        var text = ToGodot(_palette.Text);
        combo.AddThemeColorOverride("font_color", text);
        combo.AddThemeColorOverride("font_hover_color", text);
        combo.AddThemeColorOverride("font_pressed_color", text);
        combo.AddThemeColorOverride("font_hover_pressed_color", text);
        combo.AddThemeColorOverride("font_disabled_color", ToGodot(_palette.Muted));
        combo.AddThemeStyleboxOverride("normal", FieldStyle(focused: false));
        combo.AddThemeStyleboxOverride("hover", Box(Inset, Veil(0.24f), RadiusControl, padX: SpaceSm + 2, padY: ControlPadY));
        combo.AddThemeStyleboxOverride("pressed", FieldStyle(focused: true));
        combo.AddThemeStyleboxOverride("hover_pressed", FieldStyle(focused: true));
        combo.AddThemeStyleboxOverride("disabled", Box(Veil(0.04f), new Color(0, 0, 0, 0), RadiusControl, borderWidth: 0, padX: SpaceSm + 2, padY: ControlPadY));
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
        edit.AddThemeColorOverride("font_placeholder_color", ToGodot(_palette.Muted) with { A = 0.75f });
        edit.AddThemeStyleboxOverride("normal", FieldStyle(focused: false));
        edit.AddThemeStyleboxOverride("focus", FieldStyle(focused: true));
        // The MCP config box is read-only, and a read-only TextEdit draws this box instead of
        // "normal": left unset, it was the one engine-grey rectangle on the settings page.
        edit.AddThemeStyleboxOverride("read_only", FieldStyle(focused: false));
    }
}
