using System.Collections.Generic;

namespace STS2AIAgent.Ui;

/// <summary>What a rich log has to do to go from what it shows to what it should show.</summary>
internal enum RichLogChange
{
    /// <summary>Same entries: touch nothing, so the reader's scroll and selection survive.</summary>
    None,

    /// <summary>The old entries are a prefix of the new ones: append only the new tail.</summary>
    Append,

    /// <summary>Anything else: clear and redraw.</summary>
    Replace
}

/// <summary>
/// Decides how a rich log moves from one list of BBCode entries to the next.
/// </summary>
/// <remarks>
/// The logs used to be cleared and redrawn on every refresh and every 800 ms tick, which threw away
/// the reader's scroll position and text selection several times a second: scrolling up to read an
/// earlier turn was pulled straight back to the bottom. Comparing entry lists instead of whole strings
/// keeps every append on an entry boundary, so the appended tail is always balanced BBCode.
///
/// Godot-free, so the executable test project can pin the three outcomes.
/// </remarks>
internal static class RichLogDiff
{
    public static RichLogChange Plan(IReadOnlyList<string> shown, IReadOnlyList<string> next)
    {
        if (next.Count < shown.Count)
        {
            return RichLogChange.Replace;
        }

        for (var index = 0; index < shown.Count; index++)
        {
            if (!string.Equals(shown[index], next[index], StringComparison.Ordinal))
            {
                return RichLogChange.Replace;
            }
        }

        return next.Count == shown.Count ? RichLogChange.None : RichLogChange.Append;
    }
}
