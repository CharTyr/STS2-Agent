using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Game;

/// <summary>Game-thread-only anchors; a disappearing node simply removes its decoration.</summary>
internal static class HudAnchors
{
    public static (Rect2? Card, Vector2? Target, Vector2? Player) Read(LiveIntent intent)
    {
        try
        {
            var room = NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return default;
            Rect2? cardRect = null;
            if (intent.CardCid is uint cid && NetCombatCardDb.Instance.TryGetCard(cid, out var card) && card != null)
                foreach (var holder in room.Ui.Hand.ActiveHolders)
                    if (GodotObject.IsInstanceValid(holder) && holder.IsVisibleInTree() && ReferenceEquals(holder.CardModel, card))
                    { cardRect = holder.GetGlobalRect(); break; }
            Vector2? targetPoint = null, playerPoint = null;
            var combat = CombatManager.Instance.DebugOnlyGetState();
            // Enemy indices include dead enemies, exactly like combat.enemies and the executor.
            if (intent.TargetName != null && intent.TargetIndex is int target && combat != null
                && target >= 0 && target < combat.Enemies.Count)
            {
                var targetNode = room.GetCreatureNode(combat.Enemies[target]);
                if (targetNode != null && GodotObject.IsInstanceValid(targetNode) && targetNode.IsInsideTree())
                    targetPoint = targetNode.GetTopOfHitbox();
            }
            var me = GameStateService.GetLocalPlayer(combat);
            var playerNode = room.GetCreatureNode(me?.Creature);
            if (playerNode != null && GodotObject.IsInstanceValid(playerNode) && playerNode.IsInsideTree())
                playerPoint = playerNode.GetTopOfHitbox();
            return (cardRect, targetPoint, playerPoint);
        }
        catch (Exception) { return default; }
    }
}
