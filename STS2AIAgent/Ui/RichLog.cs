using System.Collections.Generic;
using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// A <c>RichTextLabel</c> that is redrawn from a list of BBCode entries without fighting its reader.
/// </summary>
/// <remarks>
/// The conversation, the team chat and the decision log are re-read on every runtime change and on
/// the 800 ms panel tick. They used to clear and redraw every time, and the conversation then forced
/// its scroll to the bottom, so a player who scrolled up to read an earlier turn was pulled back down
/// within a second, and any text they had selected to copy was dropped.
///
/// Now an unchanged list touches nothing, a list that only grew is appended to, and only a real
/// change redraws. The scroll follows the newest entry only when the reader was already there:
/// <see cref="LogAnchor.Bottom"/> for logs that grow downwards, <see cref="LogAnchor.Top"/> for the
/// newest-first decision log. A reader who scrolled away keeps their place.
///
/// Godot's own <c>ScrollFollowing</c> is turned off: it re-arms itself whenever a clear resets the
/// bar to zero, which is exactly the redraw this class exists to make harmless.
/// </remarks>
internal sealed class RichLog
{
    /// <summary>Which edge the newest entry arrives at, and therefore which edge the view sticks to.</summary>
    public enum LogAnchor
    {
        Bottom,
        Top
    }

    /// <summary>How close to an edge still counts as "at" it, in pixels.</summary>
    private const double EdgeSlack = 4;

    private readonly List<string> _shown = new();
    private readonly LogAnchor _anchor;

    /// <summary>
    /// Whether the reader is parked at the anchored edge. Kept up to date from the scroll bar, so a
    /// resize -- the panel opening, a page becoming visible, the window changing size -- can put a
    /// reader who was at the newest entry back there after the text reflows.
    /// </summary>
    private bool _atEdge = true;
    private bool _settling;

    public RichLog(RichTextLabel label, LogAnchor anchor)
    {
        Label = label;
        _anchor = anchor;
        label.ScrollFollowing = false;
        label.GetVScrollBar().ValueChanged += OnScrolled;
        label.Resized += OnResized;
    }

    public RichTextLabel Label { get; }

    /// <summary>Shows exactly <paramref name="entries"/>, doing as little to the label as it can.</summary>
    public void Show(IReadOnlyList<string> entries)
    {
        if (!GodotObject.IsInstanceValid(Label))
        {
            return;
        }

        var change = RichLogDiff.Plan(_shown, entries);
        if (change == RichLogChange.None)
        {
            return;
        }

        // The tracked flag rather than a fresh reading: a hidden or not-yet-laid-out label reports a
        // scroll range that says nothing about where the reader left it.
        var atEdge = _atEdge;
        var offset = Label.GetVScrollBar().Value;

        if (change == RichLogChange.Append)
        {
            _settling = true;
            for (var index = _shown.Count; index < entries.Count; index++)
            {
                Label.AppendText(entries[index]);
                _shown.Add(entries[index]);
            }

            _settling = false;

            // Appending below the view moves nothing; only a reader parked at the bottom follows.
            if (_anchor == LogAnchor.Bottom && atEdge)
            {
                Callable.From(() => Settle(true, offset)).CallDeferred();
            }

            return;
        }

        // The clear resets the bar to zero; that is not the reader scrolling.
        _settling = true;
        Label.Clear();
        _shown.Clear();
        foreach (var entry in entries)
        {
            Label.AppendText(entry);
            _shown.Add(entry);
        }

        _settling = false;
        _atEdge = atEdge;

        // Deferred: the text appended this frame has not been laid out, so the bar's range still
        // describes the old content.
        Callable.From(() => Settle(atEdge, offset)).CallDeferred();
    }

    /// <summary>Forgets what is shown, so the next <see cref="Show"/> redraws in full.</summary>
    public void Reset()
    {
        _shown.Clear();
    }

    private void Settle(bool toEdge, double offset)
    {
        if (!GodotObject.IsInstanceValid(Label) || !Label.IsInsideTree())
        {
            return;
        }

        // Reading the content height lays out the pending text, which brings the bar's range up to date.
        Label.GetContentHeight();
        var bar = Label.GetVScrollBar();
        _settling = true;
        bar.Value = toEdge ? (_anchor == LogAnchor.Bottom ? bar.MaxValue : 0) : offset;
        _settling = false;
        _atEdge = toEdge;
    }

    private bool IsAtEdge(ScrollBar bar)
    {
        return _anchor == LogAnchor.Bottom
            ? bar.Value + bar.Page >= bar.MaxValue - EdgeSlack
            : bar.Value <= EdgeSlack;
    }

    private void OnScrolled(double value)
    {
        if (_settling || !GodotObject.IsInstanceValid(Label))
        {
            return;
        }

        _atEdge = IsAtEdge(Label.GetVScrollBar());
    }

    private void OnResized()
    {
        if (_atEdge)
        {
            Callable.From(() => Settle(true, 0)).CallDeferred();
        }
    }
}
