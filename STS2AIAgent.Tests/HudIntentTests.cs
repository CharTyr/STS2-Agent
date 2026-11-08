using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class HudIntentTests
{
    public static void UsesStableCardAndEnemySpace()
    {
        var now = DateTimeOffset.UtcNow;
        const string state = """{"screen":"COMBAT","combat":{"hand":[{"i":0,"cid":7,"line":"Strike","target":"enemy"}],"enemies":[{"i":1,"name":"Jaw Worm"}]}}""";
        var intent = LiveIntent.Create("""{"action":"play_card","card_cid":7,"target_index":1,"reason":"Finish the enemy","then":[{"action":"end_turn"}]}""", state, now);
        Assert.Equal("Strike", intent.CardName);
        Assert.Equal("Jaw Worm", intent.TargetName);
        Assert.True(intent.PlanPreview.Length > 0);
        Assert.True(HudModel.ShowIntent(intent, "COMBAT", true, now));
        Assert.False(HudModel.ShowIntent(intent, "MAP", true, now));
        Assert.False(HudModel.ShowIntent(intent, "COMBAT", false, now));
        Assert.False(HudModel.ShowIntent(intent, "COMBAT", true, now.AddSeconds(5)));
    }

    public static void AllyTargetDoesNotPointAtEnemy()
    {
        const string state = """{"screen":"COMBAT","combat":{"hand":[{"i":0,"cid":7,"line":"Help","target":"player"}],"enemies":[{"i":0,"name":"Wrong target"}]}}""";
        var intent = LiveIntent.Create("""{"action":"play_card","card_cid":7,"target_index":0} """, state, DateTimeOffset.UtcNow);
        Assert.Equal<string?>(null, intent.TargetName);
    }

    public static void CaptureHidesHudEvenWithPanelHidden()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        Assert.Contains("_instance?._hud?.Visible == true", source);
        Assert.Contains("_instance._hud.Visible = false", source);
        Assert.Contains("panel.Visible = _instance._capturePanelWasVisible", source);
        Assert.Contains("RemoveHud();", source);
    }
}
