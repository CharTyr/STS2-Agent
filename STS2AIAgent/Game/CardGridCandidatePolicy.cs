namespace STS2AIAgent.Game;

/// <summary>
/// The rules that turn a card grid's on-screen window into its whole card list (#218).
/// </summary>
/// <remarks>
/// The game's card grid only instantiates holders for the rows near the scroll position. A campfire
/// upgrade with 27 upgradable cards showed 25 holders, and the two below the fold were never offered:
/// <c>selection.cards</c> and <c>select_deck_card</c> both indexed the visible holders only.
///
/// The full list lives in a private field of the grid (or of the screen that filled it), whose name
/// this mod cannot compile against. So the reader collects every card collection those nodes hold and
/// this policy picks the one the grid is showing a window of. Kept free of game types so the rules
/// are tested offline.
/// </remarks>
internal static class CardGridCandidatePolicy
{
    /// <summary>One card collection found on a node, ranked by how close that node is to the holders.</summary>
    internal readonly record struct Candidate<T>(int OwnerRank, IReadOnlyList<T> Items)
        where T : class;

    /// <summary>
    /// The card list the grid's visible holders are a window of, or the visible cards when no collection
    /// qualifies.
    /// </summary>
    /// <remarks>
    /// A collection qualifies only if it holds every visible card. That rules out the selected-cards list
    /// and any partial buffer. A collection with more cards than the window beats one that only mirrors
    /// it; among those the nearest owner wins (the grid before the screen that filled it), and within one
    /// owner the largest. When nothing holds more than the window, the window is the whole list.
    /// </remarks>
    public static IReadOnlyList<T> ResolveCandidates<T>(IReadOnlyList<T> visible, IEnumerable<Candidate<T>> candidates)
        where T : class
    {
        if (visible.Count == 0)
        {
            return visible;
        }

        IReadOnlyList<T>? best = null;
        var bestRank = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var items = DistinctByReference(candidate.Items);
            if (items.Count <= visible.Count || !ContainsAllByReference(items, visible))
            {
                continue;
            }

            if (best == null ||
                candidate.OwnerRank < bestRank ||
                candidate.OwnerRank == bestRank && items.Count > best.Count)
            {
                best = items;
                bestRank = candidate.OwnerRank;
            }
        }

        return best ?? visible;
    }

    /// <summary>
    /// Which way to scroll so the target enters the window: +1 down, -1 up, 0 when the target is already
    /// inside the visible range (or nothing is visible to compare with).
    /// </summary>
    public static int ScrollDirection(int targetIndex, IReadOnlyCollection<int> visibleIndices)
    {
        var known = visibleIndices.Where(index => index >= 0).ToArray();
        if (targetIndex < 0 || known.Length == 0)
        {
            return 0;
        }

        if (targetIndex > known.Max())
        {
            return 1;
        }

        return targetIndex < known.Min() ? -1 : 0;
    }

    /// <summary>
    /// Ranks a screen method that takes one card as the native "card clicked" handler, or null when the
    /// name does not read as one.
    /// </summary>
    /// <remarks>
    /// Last-resort path for a card the grid would not scroll into view. Only a handler named for a click
    /// or a selection may be invoked; anything that removes, deselects, hovers or previews is refused,
    /// because calling the wrong one-card method would change the deck instead of picking from it.
    /// </remarks>
    public static int? RankCardClickHandler(string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
        {
            return null;
        }

        foreach (var refused in RefusedHandlerWords)
        {
            if (methodName.Contains(refused, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        if (methodName.Contains("Click", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (methodName.Contains("Pressed", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return methodName.Contains("Select", StringComparison.OrdinalIgnoreCase) ? 2 : null;
    }

    private static readonly string[] RefusedHandlerWords =
    {
        "Deselect", "Unselect", "Remove", "Hover", "Focus", "Preview", "Inspect", "Highlight", "Add", "Transform", "Upgrade", "Enchant",
    };

    private static IReadOnlyList<T> DistinctByReference<T>(IReadOnlyList<T> items)
        where T : class
    {
        var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var distinct = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (item != null && seen.Add(item))
            {
                distinct.Add(item);
            }
        }

        return distinct;
    }

    private static bool ContainsAllByReference<T>(IReadOnlyList<T> items, IReadOnlyList<T> required)
        where T : class
    {
        var set = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        return required.All(item => set.Contains(item));
    }
}
