using STS2AIAgent.Agent;
using STS2AIAgent.Config;

namespace STS2AIAgent.Tests;

internal static class ProactiveChatPolicyTests
{
    private static ProactiveChatInput Input(
        bool enabled = true,
        bool playRunning = true,
        string? budgetBlock = null,
        int messagesSent = 0,
        TimeSpan? sinceLastMessage = null,
        ProactiveChatMoment moment = ProactiveChatMoment.CombatStart) =>
        new(enabled, ProactiveChatTones.Default, playRunning, budgetBlock, messagesSent, sinceLastMessage, moment);

    public static void DefaultsStayOff()
    {
        var fresh = new AgentSettings();
        Assert.False(fresh.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Friendly, fresh.ProactiveChatTone);

        var created = AgentSettings.CreateDefault();
        Assert.False(created.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Default, created.ProactiveChatTone);
    }

    public static void UnknownToneFallsBackToDefault()
    {
        Assert.Equal(ProactiveChatTones.Default, ProactiveChatTones.Normalize(null));
        Assert.Equal(ProactiveChatTones.Default, ProactiveChatTones.Normalize("   "));
        Assert.Equal(ProactiveChatTones.Default, ProactiveChatTones.Normalize("dramatic"));
        Assert.Equal(ProactiveChatTones.Calm, ProactiveChatTones.Normalize(" CALM "));
        Assert.Equal(ProactiveChatTones.Terse, ProactiveChatTones.Normalize("Terse"));
        Assert.Equal(ProactiveChatTones.Default, ProactiveChatTones.Normalize(""));
    }

    public static void ShapeRepairKeepsOptInOffAndFixesTone()
    {
        var settings = new AgentSettings { ProactiveChatTone = "dramatic" };
        settings.EnsureValidShape();
        Assert.False(settings.ProactiveChatEnabled);
        Assert.Equal(ProactiveChatTones.Default, settings.ProactiveChatTone);
    }

    public static void ToneInstructionsAreDistinctAndBounded()
    {
        var instructions = ProactiveChatTones.Options
            .Select(option => ProactiveChatTones.BuildSystemInstruction(option.Id))
            .ToArray();
        Assert.Equal(ProactiveChatTones.Options.Count, instructions.Distinct().Count());
        foreach (var instruction in instructions)
        {
            Assert.Contains("one short sentence", instruction);
            Assert.Contains("never offer to play for the player", instruction);
        }

        Assert.Contains("warm co-op partner", ProactiveChatTones.BuildSystemInstruction(ProactiveChatTones.Friendly));
        Assert.Contains("steady, unhurried advisor", ProactiveChatTones.BuildSystemInstruction(ProactiveChatTones.Calm));
        Assert.Contains("clipped field report", ProactiveChatTones.BuildSystemInstruction(ProactiveChatTones.Terse));
        Assert.Contains("warm co-op partner", ProactiveChatTones.BuildSystemInstruction("unknown-tone"));
    }

    public static void SituationKeySeparatesCombatFromScreen()
    {
        Assert.Equal("COMBAT", ProactiveChatPolicy.SituationKey("COMBAT", true));
        Assert.Equal("COMBAT", ProactiveChatPolicy.SituationKey("MAP", true));
        Assert.Equal("MAP", ProactiveChatPolicy.SituationKey("map", false));
        Assert.Equal("", ProactiveChatPolicy.SituationKey(null, false));
    }

    public static void ObserveReportsOnlyBoundaryCrossings()
    {
        Assert.Equal(ProactiveChatMoment.None, ProactiveChatPolicy.Observe(null, null));
        Assert.Equal(ProactiveChatMoment.None, ProactiveChatPolicy.Observe("MAP", "MAP"));
        Assert.Equal(ProactiveChatMoment.CombatStart, ProactiveChatPolicy.Observe("MAP", "COMBAT"));
        Assert.Equal(ProactiveChatMoment.CombatEnd, ProactiveChatPolicy.Observe("COMBAT", "MAP"));
        Assert.Equal(ProactiveChatMoment.None, ProactiveChatPolicy.Observe("MAP", "REST"));
        Assert.Equal(ProactiveChatMoment.CombatEnd, ProactiveChatPolicy.Observe("COMBAT", "GAME_OVER"));
    }

    public static void DecideRefusesWhenDisabled()
    {
        var decision = ProactiveChatPolicy.Decide(Input(enabled: false));
        Assert.False(decision.Send);
        Assert.Equal("opt-in off", decision.Reason);
    }

    public static void DecideRefusesWithoutMoment()
    {
        var decision = ProactiveChatPolicy.Decide(Input(moment: ProactiveChatMoment.None));
        Assert.False(decision.Send);
        Assert.Equal("no moment", decision.Reason);
    }

    public static void DecideRefusesWhilePaused()
    {
        var decision = ProactiveChatPolicy.Decide(Input(playRunning: false));
        Assert.False(decision.Send);
        Assert.Equal("not playing", decision.Reason);
    }

    public static void DecideRefusesWhenBudgetBlocks()
    {
        var decision = ProactiveChatPolicy.Decide(Input(budgetBlock: "已达到会话请求次数上限"));
        Assert.False(decision.Send);
        Assert.Equal("budget", decision.Reason);
    }

    public static void DecideRefusesAtSessionCap()
    {
        var decision = ProactiveChatPolicy.Decide(Input(messagesSent: ProactiveChatPolicy.MaxMessagesPerSession));
        Assert.False(decision.Send);
        Assert.Equal("session cap", decision.Reason);
    }

    public static void DecideRefusesInsideMinimumInterval()
    {
        var decision = ProactiveChatPolicy.Decide(
            Input(messagesSent: 1, sinceLastMessage: ProactiveChatPolicy.MinInterval - TimeSpan.FromSeconds(1)));
        Assert.False(decision.Send);
        Assert.Equal("min interval", decision.Reason);

        var boundary = ProactiveChatPolicy.Decide(
            Input(messagesSent: 1, sinceLastMessage: ProactiveChatPolicy.MinInterval));
        Assert.True(boundary.Send);
        Assert.Null(boundary.Reason);
    }

    public static void DecideSendsWhenEveryGatePasses()
    {
        var first = ProactiveChatPolicy.Decide(Input());
        Assert.True(first.Send);
        Assert.Null(first.Reason);

        var later = ProactiveChatPolicy.Decide(
            Input(messagesSent: 1, sinceLastMessage: ProactiveChatPolicy.MinInterval + TimeSpan.FromSeconds(1)));
        Assert.True(later.Send);

        var lastAllowed = ProactiveChatPolicy.Decide(
            Input(messagesSent: ProactiveChatPolicy.MaxMessagesPerSession - 1, sinceLastMessage: null));
        Assert.True(lastAllowed.Send);
    }

    public static void BuildPromptDistinguishesMoments()
    {
        var start = ProactiveChatPolicy.BuildPrompt(ProactiveChatMoment.CombatStart);
        var end = ProactiveChatPolicy.BuildPrompt(ProactiveChatMoment.CombatEnd);
        Assert.True(start.Length > 0);
        Assert.True(end.Length > 0);
        Assert.True(!string.Equals(start, end, StringComparison.Ordinal));
        Assert.Contains("fight just started", start);
        Assert.Contains("fight just ended", end);
    }

    public static void SessionCapStaysSmall()
    {
        Assert.True(ProactiveChatPolicy.MaxMessagesPerSession <= 10);
        Assert.True(ProactiveChatPolicy.MinInterval >= TimeSpan.FromSeconds(30));
    }
}

