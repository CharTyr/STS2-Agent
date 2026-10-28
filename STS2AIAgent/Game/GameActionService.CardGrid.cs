using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>Reaching a card the card grid has not drawn yet (#218), for <c>select_deck_card</c>.</summary>
internal static partial class GameActionService
{
    private const int CardGridScrollStepLimit = 60;
    private const int CardGridScrollSettleFrames = 12;
    private const int CardGridScrollStallLimit = 3;
    private static readonly TimeSpan CardGridScrollTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CardGridHandlerTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Clicks <paramref name="card"/> on a card grid, scrolling it into view first when the grid has no
    /// holder for it (#218).
    /// </summary>
    /// <remarks>
    /// The grid only instantiates holders near its scroll position, so a card below the fold has nothing
    /// to click. The paths, in order of how closely they match a player:
    /// 1. its holder is on screen: press it, the same signal the visible cards always used;
    /// 2. scroll toward it -- a mouse wheel over the grid, then any Godot scroll container or scrollbar
    ///    in the screen -- until a holder shows it, and press that;
    /// 3. a holder for it exists but is hidden: press that;
    /// 4. the screen's own one-card click handler, found by signature and a click/select name.
    /// When none applies the card is reported unreachable instead of clicking some other card.
    /// </remarks>
    private static async Task PressCardGridCardAsync(
        NCardGridSelectionScreen screen,
        CardModel card,
        IReadOnlyList<CardModel> cards,
        int optionIndex)
    {
        var holder = GameStateService.FindCardGridHolder(screen, card, includeHidden: false)
            ?? await ScrollCardGridToCardAsync(screen, card, cards);

        // Scrolling waits on frames, and the screen can close or change meanwhile. A holder or handler
        // of a screen that is no longer up must not be clicked.
        if (!IsCurrentCardGridScreen(screen))
        {
            throw new ApiException(409, "invalid_action", "The card selection closed while scrolling to the card.", new
            {
                action = "select_deck_card",
                reason = "screen_changed",
                option_index = optionIndex
            });
        }

        holder ??= GameStateService.FindCardGridHolder(screen, card, includeHidden: true);
        if (holder != null)
        {
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            return;
        }

        if (await TryInvokeCardGridClickHandlerAsync(screen, card) is string handlerName)
        {
            Log.Info($"[STS2AIAgent] select_deck_card: option {optionIndex} has no holder after scrolling; called {screen.GetType().Name}.{handlerName}.");
            return;
        }

        throw new ApiException(409, "invalid_target", "The card is in the selection but the grid could not bring it on screen.", new
        {
            action = "select_deck_card",
            reason = "card_not_reachable",
            option_index = optionIndex,
            option_count = cards.Count,
            visible_count = GameStateService.GetCardGridVisibleHolders(screen).Count
        });
    }

    private static async Task<NGridCardHolder?> ScrollCardGridToCardAsync(
        NCardGridSelectionScreen screen,
        CardModel card,
        IReadOnlyList<CardModel> cards)
    {
        var targetIndex = IndexOfReference(cards, card);
        if (targetIndex < 0)
        {
            return null;
        }

        var deadline = DateTime.UtcNow + CardGridScrollTimeout;
        var useWheel = true;
        var stalled = 0;
        // When the list order does not tell which way the card is (the window's list positions straddle
        // it), sweep down to the end, then back up.
        var sweep = 1;
        for (var step = 0; step < CardGridScrollStepLimit && DateTime.UtcNow < deadline; step++)
        {
            if (!IsCurrentCardGridScreen(screen))
            {
                return null;
            }

            var visible = GameStateService.GetCardGridVisibleHolders(screen);
            if (visible.Count == 0)
            {
                return null;
            }

            var direction = CardGridCandidatePolicy.ScrollDirection(
                targetIndex,
                visible.Select(holder => IndexOfReference(cards, holder.CardModel)).ToArray());
            var sweeping = direction == 0;
            if (sweeping)
            {
                direction = sweep;
            }

            var before = VisibleCardSignature(visible);
            var scrolled = useWheel
                ? PushCardGridWheel(screen, visible, direction)
                : ScrollCardGridControls(screen, visible[0], direction);

            for (var frame = 0; frame < CardGridScrollSettleFrames; frame++)
            {
                await WaitForNextFrameAsync();
                if (!IsCurrentCardGridScreen(screen))
                {
                    return null;
                }

                var found = GameStateService.FindCardGridHolder(screen, card, includeHidden: false);
                if (found != null)
                {
                    Log.Info($"[STS2AIAgent] select_deck_card: scrolled card {targetIndex} into view after {step + 1} step(s) ({(useWheel ? "wheel" : "scroll control")}).");
                    return found;
                }
            }

            var moved = scrolled &&
                VisibleCardSignature(GameStateService.GetCardGridVisibleHolders(screen)) != before;
            if (moved)
            {
                stalled = 0;
                continue;
            }

            if (++stalled < CardGridScrollStallLimit)
            {
                continue;
            }

            stalled = 0;
            if (sweeping && sweep > 0)
            {
                sweep = -1;
                continue;
            }

            if (!useWheel)
            {
                break;
            }

            useWheel = false;
            sweep = 1;
        }

        Log.Warn($"[STS2AIAgent] select_deck_card: card {targetIndex} of {cards.Count} did not scroll into view.");
        return null;
    }

    /// <summary>A mouse-wheel notch over the middle of the visible cards, the way a player scrolls the grid.</summary>
    private static bool PushCardGridWheel(NCardGridSelectionScreen screen, IReadOnlyList<NGridCardHolder> visible, int direction)
    {
        var viewport = screen.GetViewport();
        if (viewport == null || visible.Count == 0)
        {
            return false;
        }

        var sum = Vector2.Zero;
        foreach (var holder in visible)
        {
            sum += holder.GetGlobalTransformWithCanvas().Origin;
        }

        var position = sum / visible.Count;
        var button = direction > 0 ? MouseButton.WheelDown : MouseButton.WheelUp;
        foreach (var pressed in new[] { true, false })
        {
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = button,
                Pressed = pressed,
                Factor = 1f,
                Position = position,
                GlobalPosition = position
            }, inLocalCoords: true);
        }

        return true;
    }

    /// <summary>
    /// Moves the Godot scroll container that holds the grid, or a vertical scrollbar beside it, by half a
    /// page. Only controls on the holders' own ancestor chain count, so another pane on the screen never
    /// moves.
    /// </summary>
    private static bool ScrollCardGridControls(NCardGridSelectionScreen screen, NGridCardHolder anchor, int direction)
    {
        var chain = new HashSet<Node>();
        for (Node? node = anchor.GetParent(); node != null; node = node.GetParent())
        {
            chain.Add(node);
            if (ReferenceEquals(node, screen))
            {
                break;
            }
        }

        var moved = false;
        foreach (var node in EnumerateDescendants(screen))
        {
            if (node is ScrollContainer container && chain.Contains(container) && container.IsVisibleInTree())
            {
                var page = Math.Max(1, (int)(container.Size.Y * 0.5f));
                container.ScrollVertical += direction * page;
                moved = true;
            }
            else if (node is VScrollBar bar && bar.GetParent() is Node parent && chain.Contains(parent) && bar.IsVisibleInTree())
            {
                bar.Value += direction * Math.Max(bar.Page * 0.5, bar.Step > 0 ? bar.Step : 1);
                moved = true;
            }
        }

        return moved;
    }

    private static IEnumerable<Node> EnumerateDescendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child))
            {
                continue;
            }

            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Calls the screen's own one-card click handler, found by signature: an instance method declared on
    /// the screen's game types that takes exactly one <see cref="CardModel"/> and whose name reads as a
    /// click or a selection (<see cref="CardGridCandidatePolicy.RankCardClickHandler"/>). Returns the
    /// handler's name once it has finished -- a Task-returning handler is awaited -- or null.
    /// </summary>
    private static async Task<string?> TryInvokeCardGridClickHandlerAsync(NCardGridSelectionScreen screen, CardModel card)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var candidates = new List<(int Rank, MethodInfo Method)>();
        for (var type = screen.GetType(); type != null && type.Namespace?.StartsWith("Godot", StringComparison.Ordinal) != true; type = type.BaseType)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(flags);
            }
            catch
            {
                continue;
            }

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (method.IsAbstract ||
                    method.ContainsGenericParameters ||
                    parameters.Length != 1 ||
                    parameters[0].ParameterType != typeof(CardModel) ||
                    (method.ReturnType != typeof(void) && !typeof(Task).IsAssignableFrom(method.ReturnType)) ||
                    CardGridCandidatePolicy.RankCardClickHandler(method.Name) is not int rank)
                {
                    continue;
                }

                candidates.Add((rank, method));
            }
        }

        // Most derived first within a rank: an override on the concrete screen is what the base's
        // dispatch would reach anyway, and a base-only helper is the less specific choice.
        var chosen = candidates.OrderBy(candidate => candidate.Rank).Select(candidate => candidate.Method).FirstOrDefault();
        if (chosen == null)
        {
            return null;
        }

        try
        {
            if (chosen.Invoke(screen, new object[] { card }) is Task pending)
            {
                var finished = await WaitForGameTaskAsync(pending, CardGridHandlerTimeout);
                if (finished == null)
                {
                    // Still running: the click settle that follows keeps watching the selection count.
                    Log.Warn($"[STS2AIAgent] select_deck_card: {screen.GetType().Name}.{chosen.Name} still running after {CardGridHandlerTimeout.TotalSeconds:0}s.");
                    return chosen.Name;
                }

                await finished;
            }

            return chosen.Name;
        }
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] select_deck_card: {screen.GetType().Name}.{chosen.Name} failed: {ex}");
            return null;
        }
    }

    private static bool IsCurrentCardGridScreen(NCardGridSelectionScreen screen) =>
        GodotObject.IsInstanceValid(screen) &&
        ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), screen);

    private static int IndexOfReference(IReadOnlyList<CardModel> cards, CardModel? card)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], card))
            {
                return index;
            }
        }

        return -1;
    }

    private static string VisibleCardSignature(IReadOnlyList<NGridCardHolder> visible) =>
        string.Join(",", visible.Select(holder => holder.CardModel == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(holder.CardModel)));
}
