using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Game;

internal static partial class GameStateService
{

    private static MapPayload? BuildMapPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (!TryGetMapScreen(currentScreen, runState, out var mapScreen))
        {
            return null;
        }

        var visibleNodes = FindDescendants<NMapPoint>(mapScreen!)
            .Where(node => GodotObject.IsInstanceValid(node))
            .GroupBy(node => node.Point.coord)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => node.GlobalPosition.Y)
                    .ThenBy(node => node.GlobalPosition.X)
                    .First());

        var availableNodes = visibleNodes.Values
            .Where(node => IsMapNodeClickable(mapScreen!, node))
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToArray();
        var availableCoords = new HashSet<MapCoord>(availableNodes.Select(node => node.Point.coord));
        var visitedCoords = new HashSet<MapCoord>(runState!.VisitedMapCoords);
        var allMapPoints = GetAllMapPoints(runState.Map);
        var playerVotes = BuildMapPlayerVotePayloads(runState);
        var localVote = playerVotes.FirstOrDefault(vote => vote.is_local)?.coord;
        var votesByCoord = playerVotes
            .Where(vote => vote.coord != null)
            .GroupBy(vote => $"{vote.coord!.row},{vote.coord.col}")
            .ToDictionary(group => group.Key, group => group.ToArray());

        return new MapPayload
        {
            current_node = BuildMapCoordPayload(runState!.CurrentMapCoord),
            is_travel_enabled = mapScreen!.IsTravelEnabled,
            is_traveling = mapScreen.IsTraveling,
            map_generation_count = RunManager.Instance.MapSelectionSynchronizer.MapGenerationCount,
            rows = runState.Map.GetRowCount(),
            cols = runState.Map.GetColumnCount(),
            starting_node = BuildMapCoordPayload(runState.Map.StartingMapPoint.coord),
            boss_node = BuildMapCoordPayload(runState.Map.BossMapPoint.coord),
            second_boss_node = BuildMapCoordPayload(runState.Map.SecondBossMapPoint?.coord),
            nodes = allMapPoints
                .Select(point => BuildMapGraphNodePayload(
                    point,
                    visibleNodes.TryGetValue(point.coord, out var mapNode) ? mapNode : null,
                    visitedCoords,
                    availableCoords,
                    runState.CurrentMapCoord,
                    runState.Map.StartingMapPoint.coord,
                    runState.Map.BossMapPoint.coord,
                    runState.Map.SecondBossMapPoint?.coord))
                .ToArray(),
            available_nodes = availableNodes.Select((node, index) => BuildMapNodePayload(node, index, votesByCoord)).ToArray(),
            local_vote = localVote,
            player_votes = playerVotes
        };
    }

    private static MapNodePayload BuildMapNodePayload(
        NMapPoint node,
        int index,
        IReadOnlyDictionary<string, MapPlayerVotePayload[]>? votesByCoord = null)
    {
        var voteKey = $"{node.Point.coord.row},{node.Point.coord.col}";
        votesByCoord ??= new Dictionary<string, MapPlayerVotePayload[]>();
        votesByCoord.TryGetValue(voteKey, out var voters);
        voters ??= Array.Empty<MapPlayerVotePayload>();

        return new MapNodePayload
        {
            index = index,
            row = node.Point.coord.row,
            col = node.Point.coord.col,
            node_type = node.Point.PointType.ToString(),
            state = node.State.ToString(),
            vote_count = voters.Length,
            has_local_vote = voters.Any(voter => voter.is_local),
            voted_player_ids = voters.Select(voter => voter.player_id).ToArray()
        };
    }

    private static MapGraphNodePayload BuildMapGraphNodePayload(
        MapPoint point,
        NMapPoint? mapNode,
        HashSet<MapCoord> visitedCoords,
        HashSet<MapCoord> availableCoords,
        MapCoord? currentCoord,
        MapCoord startCoord,
        MapCoord bossCoord,
        MapCoord? secondBossCoord)
    {
        return new MapGraphNodePayload
        {
            row = point.coord.row,
            col = point.coord.col,
            node_type = point.PointType.ToString(),
            state = ResolveMapPointState(point.coord, mapNode, visitedCoords, availableCoords, currentCoord),
            visited = visitedCoords.Contains(point.coord),
            is_current = currentCoord.HasValue && currentCoord.Value == point.coord,
            is_available = availableCoords.Contains(point.coord),
            is_start = point.coord == startCoord,
            is_boss = point.coord == bossCoord,
            is_second_boss = secondBossCoord.HasValue && point.coord == secondBossCoord.Value,
            parents = point.parents
                .OrderBy(parent => parent.coord.row)
                .ThenBy(parent => parent.coord.col)
                .Select(parent => BuildMapCoordPayload(parent.coord)!)
                .ToArray(),
            children = point.Children
                .OrderBy(child => child.coord.row)
                .ThenBy(child => child.coord.col)
                .Select(child => BuildMapCoordPayload(child.coord)!)
                .ToArray()
        };
    }

    private static MapCoordPayload? BuildMapCoordPayload(MapCoord? coord)
    {
        if (!coord.HasValue)
        {
            return null;
        }

        return new MapCoordPayload
        {
            row = coord.Value.row,
            col = coord.Value.col
        };
    }

    private static MapPlayerVotePayload[] BuildMapPlayerVotePayloads(RunState runState)
    {
        var localPlayer = GetLocalPlayer(runState);
        return runState.Players
            .Select(player =>
            {
                var vote = RunManager.Instance.MapSelectionSynchronizer.GetVote(player);
                return new MapPlayerVotePayload
                {
                    player_id = NetIdToString(player.NetId),
                    slot_index = runState.GetPlayerSlotIndex(player),
                    is_local = localPlayer != null && player.NetId == localPlayer.NetId,
                    coord = BuildMapCoordPayload(vote?.coord)
                };
            })
            .OrderBy(vote => vote.slot_index)
            .ToArray();
    }

    /// <summary>
    /// Host main menu, not the companion window, autoplay not running, and no dual-instance
    /// launch already in flight. Advertising does not require a verified play model: the
    /// unverified route still launches the teammate paused for external takeover.
    /// </summary>
    /// <summary>
    /// Host main menu with a saved multiplayer run on disk: the same gate the game uses to show
    /// "Load" instead of "Host" in its multiplayer submenu. The companion never continues a run
    /// itself. Continue also uses the autoplay / companion structural probe, and DualLaunching so a
    /// launch already in flight is not advertised again.
    /// </summary>
    public static IReadOnlyList<NMapPoint> GetAvailableMapNodes(IScreenContext? currentScreen, RunState? runState)
    {
        if (!TryGetMapScreen(currentScreen, runState, out var mapScreen))
        {
            return Array.Empty<NMapPoint>();
        }

        return FindDescendants<NMapPoint>(mapScreen!)
            .Where(node => GodotObject.IsInstanceValid(node) && IsMapNodeClickable(mapScreen!, node))
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToArray();
    }

    /// <summary>
    /// Whether a click on this map point would be honoured right now -- the game's own
    /// <c>NMapPoint.IsTravelable</c> rule, not the point's <c>IsEnabled</c> alone. <c>IsEnabled</c> is a
    /// cache the game refreshes only when a point's state changes, never when travel is toggled:
    /// once a node is picked, <c>TravelToMapCoord</c> turns travel off while every next-row point
    /// stays enabled for the whole path/fade animation. Reading the cache kept <c>choose_map_node</c>
    /// advertised for seconds while <c>OnRelease</c> silently ignored the click, and the agent
    /// re-sent it in a loop.
    /// </summary>
    private static bool IsMapNodeClickable(NMapScreen mapScreen, NMapPoint node)
    {
        if (!node.IsEnabled || mapScreen.IsTraveling)
        {
            return false;
        }

        return mapScreen.IsTravelEnabled || mapScreen.IsDebugTravelEnabled;
    }

    private static IReadOnlyList<MapPoint> GetAllMapPoints(ActMap map)
    {
        var points = new Dictionary<MapCoord, MapPoint>();

        void AddPoint(MapPoint? point)
        {
            if (point == null)
            {
                return;
            }

            points[point.coord] = point;
        }

        foreach (var point in map.GetAllMapPoints())
        {
            AddPoint(point);
        }

        AddPoint(map.StartingMapPoint);
        AddPoint(map.BossMapPoint);
        AddPoint(map.SecondBossMapPoint);

        return points.Values
            .OrderBy(point => point.coord.row)
            .ThenBy(point => point.coord.col)
            .ToArray();
    }

    private static string ResolveMapPointState(
        MapCoord coord,
        NMapPoint? mapNode,
        HashSet<MapCoord> visitedCoords,
        HashSet<MapCoord> availableCoords,
        MapCoord? currentCoord)
    {
        if (mapNode != null)
        {
            return mapNode.State.ToString();
        }

        if (availableCoords.Contains(coord))
        {
            return MapPointState.Travelable.ToString();
        }

        if (visitedCoords.Contains(coord) || (currentCoord.HasValue && currentCoord.Value == coord))
        {
            return MapPointState.Traveled.ToString();
        }

        return MapPointState.Untravelable.ToString();
    }
}
