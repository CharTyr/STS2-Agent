using System;
using System.Collections.Generic;
using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// A two-or-more-way exclusive switch drawn as segments in a groove, exactly one of them lifted.
/// </summary>
/// <remarks>
/// The play page uses this for its solo/multiplayer mode switch. It is a small Godot control rather
/// than a factory-local lambda because the selection has to be re-drawn when the underlying setting
/// changes without a rebuild (the overlay's <c>RefreshDynamic</c> repaints live controls), and a
/// dedicated type gives that re-draw somewhere to live.
///
/// The segments used to be the header tabs' filled accent pills, so the top of the play page was two
/// rows of the same bright bar. A groove with one lifted segment reads as "pick one of these" and
/// leaves the accent to the page's primary action.
/// </remarks>
internal sealed partial class SegmentedSwitch : PanelContainer
{
    private readonly IReadOnlyList<string> _options;
    private readonly Action<int>? _onSelected;
    private readonly HBoxContainer _row;
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
        UiFactory.TagSurface(this, UiFactory.SurfaceRole.Track);
        _row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", 2);
        AddChild(_row);
        Rebuild();
    }

    /// <summary>The index of the lifted segment.</summary>
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
        foreach (var child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }

        for (var i = 0; i < _options.Count; i++)
        {
            var index = i;
            var button = UiFactory.SegmentButton(_options[index], index == _selected, () => Select(index));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _row.AddChild(button);
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
