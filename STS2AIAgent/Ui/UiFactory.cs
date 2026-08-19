using Godot;

namespace STS2AIAgent.Ui;

internal static class UiFactory
{
    public static readonly Color Bg = new(0.07f, 0.08f, 0.11f, 0.94f);
    public static readonly Color BgRaised = new(0.12f, 0.14f, 0.18f, 1f);
    public static readonly Color Accent = new(0.82f, 0.62f, 0.28f, 1f);
    public static readonly Color Text = new(0.92f, 0.91f, 0.88f, 1f);
    public static readonly Color Muted = new(0.70f, 0.70f, 0.68f, 1f);

    public static StyleBoxFlat PanelStyle(Color? color = null, int radius = 8)
    {
        return new StyleBoxFlat
        {
            BgColor = color ?? Bg,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1, 1, 1, 0.08f)
        };
    }

    public static Button Button(string text, Action? onPressed = null)
    {
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        if (onPressed != null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    public static Label Label(string text, int size = 14, bool muted = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", muted ? Muted : Text);
        return label;
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
        edit.AddThemeFontSizeOverride("font_size", 14);
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
        box.AddThemeFontSizeOverride("font_size", 14);
        return box;
    }

    public static OptionButton Combo()
    {
        var combo = new OptionButton { MouseFilter = Control.MouseFilterEnum.Stop };
        combo.AddThemeFontSizeOverride("font_size", 14);
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
        edit.AddThemeFontSizeOverride("font_size", 14);
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
        label.AddThemeFontSizeOverride("normal_font_size", 13);
        return label;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var child in children)
        {
            child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(child);
        }

        return row;
    }

    public static VBoxContainer Column()
    {
        return new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
    }

    public static ScrollContainer Scroll(Control child, float minHeight = 0)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop
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
