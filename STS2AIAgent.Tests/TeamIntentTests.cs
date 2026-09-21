using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// The typed teammate signal and the rule it drives.
/// </summary>
/// <remarks>
/// A team message was prose, and prose cannot be acted on reliably: "I am killing the left one"
/// does not tell a decision loop which enemy index that is, and two players repeating it cannot
/// tell whether they mean the same one. These contracts pin the typed form, its backward
/// compatibility with a text-only client, and the constraint the receiving loop is handed.
/// </remarks>
internal static class TeamIntentTests
{
    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static void NoIntentIsAllowedSoTextOnlyClientsKeepWorking()
    {
        Assert.Null(TeamIntent.Parse(Body("""{"message":"hello"}""")));
        Assert.Null(TeamIntent.Parse(Body("""{"message":"hello","intent":null}""")));
    }

    public static void KnownTypesParseTheirFields()
    {
        var focus = TeamIntent.Parse(Body("""{"intent":{"type":"focus_fire","enemy_index":2}}"""));
        Assert.NotNull(focus);
        Assert.Equal(TeamIntent.FocusFire, focus!.type);
        Assert.Equal(2, focus.enemy_index);

        var potion = TeamIntent.Parse(
            Body("""{"intent":{"type":"potion_ownership","potion_index":1,"player_id":" p2 ","potion_id":"FIRE_POTION"}}"""));
        Assert.NotNull(potion);
        Assert.Equal(1, potion!.potion_index);
        Assert.Equal("p2", potion.player_id);
        Assert.Equal("FIRE_POTION", potion.potion_id);
    }

    public static void AMalformedIntentIsRefusedRatherThanDropped()
    {
        // Silence would look like a teammate ignoring an instruction, so every one of these is an
        // error the sender can see rather than a signal that quietly disappears.
        foreach (var body in new[]
                 {
                     """{"intent":"focus_fire"}""",
                     """{"intent":{"enemy_index":1}}""",
                     """{"intent":{"type":"not_a_type","enemy_index":1}}""",
                     """{"intent":{"type":"focus_fire"}}""",
                     """{"intent":{"type":"target_announce"}}""",
                     """{"intent":{"type":"potion_ownership","player_id":"p2"}}""",
                     """{"intent":{"type":"focus_fire","enemy_index":"2"}}""",
                     """{"intent":{"type":"focus_fire","enemy_index":null}}"""
                 })
        {
            var threw = false;
            try
            {
                TeamIntent.Parse(Body(body));
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert.True(threw, "expected a refusal for " + body);
        }
    }

    public static void DescriptionNamesOnlyTheFieldsTheTypeCarries()
    {
        var focus = TeamIntent.Parse(Body("""{"intent":{"type":"focus_fire","enemy_index":2}}"""))!;
        Assert.Contains("enemy_index 2", focus.Describe());
        Assert.False(
            focus.Describe().Contains("potion", StringComparison.Ordinal),
            "a focus-fire line must not mention fields its type does not carry");

        var potion = TeamIntent.Parse(Body("""{"intent":{"type":"potion_ownership","potion_index":0}}"""))!;
        Assert.Contains("slot 0", potion.Describe());
        Assert.Contains("unspecified player", potion.Describe());
    }

    public static void TheConversationCarriesTheSignalBesideTheMessage()
    {
        var conversation = new TeamConversation();
        conversation.Add("user", "taking the left one", TeamIntent.Parse(Body("""{"intent":{"type":"focus_fire","enemy_index":0}}""")));
        conversation.Add("assistant", "understood");

        var context = conversation.BuildDecisionContext();
        Assert.NotNull(context);
        Assert.Contains("taking the left one", context!);
        Assert.Contains("focus_fire: this player is attacking enemy_index 0", context);
        Assert.Contains("human_teammate", context);
        Assert.Contains("ai_teammate", context);
    }

    public static void FocusFireBecomesAConstraintOnTheNextDecision()
    {
        var conversation = new TeamConversation();
        conversation.Add("user", "left one", TeamIntent.Parse(Body("""{"intent":{"type":"focus_fire","enemy_index":1}}""")));

        var context = conversation.BuildTeamContext();

        Assert.NotNull(context);
        Assert.Contains("enemy_index 1", context!);
        Assert.Contains("不要", context);
    }

    public static void ALaterAnnouncementSupersedesAnEarlierOne()
    {
        var conversation = new TeamConversation();
        conversation.Add("user", "left", TeamIntent.Parse(Body("""{"intent":{"type":"target_announce","enemy_index":0}}""")));
        conversation.Add("user", "actually right", TeamIntent.Parse(Body("""{"intent":{"type":"focus_fire","enemy_index":3}}""")));

        // The newest word on the target wins, whichever of the two forms it arrived in.
        Assert.Equal(3, conversation.LatestFocusFire()!.enemy_index);
        Assert.Contains("enemy_index 3", conversation.FocusFireInstruction()!);
    }

    public static void WithoutAnAnnouncementThereIsNoConstraint()
    {
        var conversation = new TeamConversation();
        Assert.Null(conversation.FocusFireInstruction());
        Assert.Null(conversation.BuildTeamContext());

        conversation.Add("user", "how is your health?");
        Assert.Null(conversation.FocusFireInstruction());
        // The conversation itself still reaches the loop; only the constraint is absent.
        Assert.NotNull(conversation.BuildTeamContext());
        Assert.False(conversation.BuildTeamContext()!.Contains("enemy_index", StringComparison.Ordinal));

        conversation.Add("assistant", "fine");
        conversation.Clear();
        Assert.Null(conversation.BuildTeamContext());
    }
}
