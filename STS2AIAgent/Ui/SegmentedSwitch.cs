using System;
using System.Collections.Generic;
using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// A two-or-more-way exclusive switch drawn as a row of segment buttons, exactly one of them filled.
/// </summary>
/// <remarks>
/// The play page uses this for its solo/multiplayer mode switch. It is a small Godot control rather
/// than a factory-local lambda because the selection has to be re-drawn when the underlying setting
/// changes without a rebuild (the overlay's <c>RefreshDynamic</c> repaints live controls), and a
/// dedicated type gives that re-draw somewhere to live. The segments reuse
/// <see cref="UiFactory.TabButton"/>'s active/idle styling, so the selected segment reads the same
/// way the selected tab does.
/// </remarks>
internal sealed partial class SegmentedSwitch : HBoxContainer
{
    private readonly IReadOnlyList<string> _options;
    private readonly Action<int>? _onSelected;
    private int _selected;

    public SegmentedSwitch(IReadOnlyList<string> options, int selected, Action<int>? onSelected)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("A segmented switch needs at least one option.", nameof(options));
        }

        _options = options;
        _onSelected = onSelected;
        _selected = Math.Clamp(selected, 0, options.Count - 1);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", UiFactory.SpaceXs);
        Rebuild();
    }

    /// <summary>The index of the filled segment.</summary>
    public int Selected => _selected;

    /// <summary>Re-draws the selection without firing the callback. Used by the live refresh pass.</summary>
    public void SetSelected(int index)
    {
        var clamped = Math.Clamp(index, 0, _options.Count - 1);
        if (clamped == _selected)
        {
            return;
        }

        _selected = clamped;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        for (var i = 0; i < _options.Count; i++)
        {
            var index = i;
            var button = UiFactory.TabButton(_options[index], index == _selected, () => Select(index));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddChild(button);
        }
    }

    private void Select(int index)
    {
        if (index == _selected)
        {
            return;
        }

        _selected = index;
        Rebuild();
        _onSelected?.Invoke(index);
    }
}
