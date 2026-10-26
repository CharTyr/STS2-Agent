using Godot;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The small form helpers every page uses: labelled rows, drop-down selection by text or metadata,
/// the model drop-down, and the records that tie the settings editors back to their items.
/// </summary>
/// <remarks>
/// Gathered here from the pages, the settings form and the chat card, which each kept a few of them
/// at the bottom of a file that was already past its size budget.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>
    /// The item an editor built at <paramref name="index"/> belongs to: the one still at that index if
    /// its id agrees (which also keeps a hand-edited file with duplicate ids working), otherwise the
    /// first with that id, otherwise none -- an item deleted elsewhere is not resurrected.
    /// </summary>
    private static T? MatchById<T>(IReadOnlyList<T> items, int index, string id, Func<T, string> idOf)
        where T : class
    {
        if (index < items.Count && string.Equals(idOf(items[index]), id, StringComparison.Ordinal))
        {
            return items[index];
        }

        return items.FirstOrDefault(item => string.Equals(idOf(item), id, StringComparison.Ordinal));
    }

    private static Control Labeled(string label, Control child)
    {
        var row = new HBoxContainer();
        var text = UiFactory.Wrapped(label, 13);
        text.CustomMinimumSize = new Vector2(160, 0);
        text.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(child);
        return row;
    }

    private static OptionButton FillModelCombo(AgentSettings settings, string? selectedId, bool includeEmpty)
    {
        var combo = UiFactory.Combo();
        var selectedIndex = -1;
        if (includeEmpty)
        {
            combo.AddItem(Loc.T("(默认/无)"));
            combo.SetItemMetadata(0, "");
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedIndex = 0;
            }
        }

        var start = includeEmpty ? 1 : 0;
        if (!includeEmpty && string.IsNullOrWhiteSpace(selectedId))
        {
            combo.AddItem(Loc.T("(未选择)"));
            combo.SetItemMetadata(0, "");
            selectedIndex = 0;
            start = 1;
        }

        for (var i = 0; i < settings.Models.Count; i++)
        {
            var model = settings.Models[i];
            combo.AddItem(model.Label);
            combo.SetItemMetadata(start + i, model.Id);
            if (string.Equals(model.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = start + i;
            }
        }

        if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(selectedId))
        {
            combo.AddItem(Loc.T("(当前绑定不可用：{0})", selectedId));
            combo.SetItemMetadata(combo.ItemCount - 1, selectedId);
            selectedIndex = combo.ItemCount - 1;
        }

        combo.Selected = selectedIndex;
        return combo;
    }

    private static string SelectedText(OptionButton? combo)
    {
        if (combo == null || combo.Selected < 0)
        {
            return string.Empty;
        }

        return combo.GetItemText(combo.Selected);
    }

    private static string SelectedMetadata(OptionButton? combo)
    {
        if (combo == null || combo.Selected < 0)
        {
            return string.Empty;
        }

        return combo.GetItemMetadata(combo.Selected).AsString();
    }

    private static void SelectComboByMetadata(OptionButton combo, string? id)
    {
        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (string.Equals(combo.GetItemMetadata(i).AsString(), id, StringComparison.OrdinalIgnoreCase))
            {
                combo.Selected = i;
                return;
            }
        }

        combo.Selected = 0;
    }

    private static void SelectByText(OptionButton combo, string? value)
    {
        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (string.Equals(combo.GetItemText(i), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.Selected = i;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            // Preserve an unknown value visibly until the user chooses a supported option.
            // Falling back to item 0 would silently rewrite the model configuration on save.
            combo.AddItem(value);
            combo.Selected = combo.ItemCount - 1;
            return;
        }

        combo.Selected = combo.ItemCount > 0 ? 0 : -1;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Trim(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        text = text.Replace("\n", " ");
        return text.Length <= max ? text : text[..max] + "…";
    }

    private sealed record EndpointEditors(string Id, LineEdit Name, LineEdit Url, LineEdit Key, CheckBox Enabled, OptionButton Protocol);

    private sealed record ModelEditors(
        string Id,
        LineEdit Display,
        LineEdit ModelName,
        OptionButton Endpoint,
        CheckBox Vision,
        OptionButton ThinkingMode,
        OptionButton ThinkingIntensity,
        LineEdit ContextWindow);
}
