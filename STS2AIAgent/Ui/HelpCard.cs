using System.Collections.Generic;
using Godot;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// A card whose usage notes live behind a "?" button in its title bar instead of in the page body.
/// </summary>
/// <remarks>
/// The overlay used to explain itself in grey paragraphs under every control, which buried the
/// controls. A card now carries its labels and values only; the "how does this work" text is one
/// panel, closed by default, that the "?" toggles open right under the title.
///
/// Whether a panel is open is remembered by key rather than by control. The settings form and the
/// theme switch rebuild their controls, and a panel the player had opened should still be open
/// afterwards. The key is a short ASCII id, not the text, so it survives a language change.
/// </remarks>
internal static class HelpCard
{
    private static readonly HashSet<string> Open = new();

    /// <summary>
    /// A titled card with a "?" in its title bar. <paramref name="help"/> is already translated by the
    /// caller, so the coverage test that scans <c>Loc.T</c> call sites still sees every sentence.
    /// </summary>
    public static Control Build(string key, string title, string help, params Control[] children)
    {
        var card = UiFactory.Card(title, children);
        var header = card.FindChild(UiFactory.CardHeaderName, recursive: true, owned: false) as HBoxContainer;
        if (header?.GetParent() is not VBoxContainer column)
        {
            // The card changed shape; a card without its help is better than a card that throws.
            return card;
        }

        var text = UiFactory.Wrapped(help, UiFactory.FontCaption, muted: false);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = Open.Contains(key)
        };
        UiFactory.TagSurface(panel, UiFactory.SurfaceRole.ChromeRaised, 6);
        panel.AddChild(text);

        var button = UiFactory.Button("?", null, UiFactory.ButtonKind.Ghost);
        button.CustomMinimumSize = new Vector2(28, 0);
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        button.TooltipText = Loc.T("说明");
        button.Pressed += () =>
        {
            panel.Visible = !panel.Visible;
            if (panel.Visible)
            {
                Open.Add(key);
            }
            else
            {
                Open.Remove(key);
            }
        };

        var spacer = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        header.AddChild(spacer);
        header.AddChild(button);
        column.AddChild(panel);
        column.MoveChild(panel, header.GetIndex() + 1);
        return card;
    }
}
