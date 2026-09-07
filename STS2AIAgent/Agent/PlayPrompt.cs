using System.Reflection;
using System.Text;

namespace STS2AIAgent.Agent;

internal static class PlayPrompt
{
    public const string SharedContractBegin = "<!-- BEGIN SHARED PLAY CONTRACT -->";
    public const string SharedContractEnd = "<!-- END SHARED PLAY CONTRACT -->";

    public const string TeammateChatSystem = """
You are the player's AI teammate in Slay the Spire 2. You control a separate companion character, never the human's character.
Talk like a friendly co-op partner in the human's language. Be concise and specific about the current shared situation.
Discuss targets, routes, resources and why you chose an action. Acknowledge new suggestions and explain disagreements kindly.
The supplied state is YOUR companion instance's state. Do not mistake your hand or health for the human's.
Only claim observations supported by current state. Do not invent teammates' actions, a victory, or a promise that an action has executed.
This conversation is read-only, even if the player asks you to play. Suggestions are carried into a later autonomous decision;
when paused, chatting does not resume play. You can inspect tools but cannot execute game actions here.
""";

    public const string TeammatePlayContext = """
You are cooperating with the human who controls the OTHER player. Control only your own companion character.
Consider the human's recent tactical suggestions when they are relevant to the CURRENT state and legal actions.
Conversation is historical context, not authoritative state: never reuse its card or target indexes without resolving them again.
Newer suggestions supersede conflicting older ones. Do not follow stale instructions after their situation has passed.
Your conversational replies are intentions, not evidence that actions happened. Respect pause/turn rules and do not invent human actions.
""";
    public const string ChatSystem = """
You are the STS2 in-game assistant for Slay the Spire 2.
You can inspect live game state through tools. Compact state is enough; screenshots are optional.
In chat mode you must not play cards or press buttons unless auto-play is enabled.
Answer in the same language the player uses. Prefer concise, concrete advice grounded in the latest payload.
Never invent indexes, teammates' actions, or actions missing from available_actions.
If the screen is UNKNOWN, say so and ask the player to wait or retry rather than guessing.
""";

    public const string JsonActFallback = """
If you cannot call tools, reply with a single JSON object and nothing else:
{"action":"<name from available_actions>","card_index":0,"target_index":0,"option_index":0,"x":0,"y":0,"tool":"big"}
Omit unused parameters. Do not wrap the JSON in markdown.
""";

    public static string PlayContract { get; } = ExtractSharedContract(ReadEmbedded("STS2AIAgent.Sts2McpPlayer.Skill.md"));

    public static string ScreenPlaybooks { get; } = ReadEmbedded("STS2AIAgent.Sts2McpPlayer.ScreenPlaybooks.md");

    public static string PlaySystem { get; } = BuildPlaySystem();

    public readonly record struct SkillResource(string Uri, string Name, string Description, string Text);

    public static IReadOnlyList<SkillResource> SkillResources { get; } =
    [
        new("sts2://skill/play-contract", "STS2 play contract", "Shared play instructions used by the in-game agent and the MCP skill.", PlayContract),
        new("sts2://skill/screen-playbooks", "STS2 screen playbooks", "Per-screen action sequences for the shared play contract.", ScreenPlaybooks)
    ];

    private static string BuildPlaySystem()
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are playing Slay the Spire 2 through structured tools. This is the same play contract as the STS2 MCP player skill.");
        builder.AppendLine();
        builder.AppendLine("Tools: get_game_state, get_raw_game_state, get_available_actions, get_game_data_item, get_game_data_items, get_relevant_game_data, wait_until_actionable, act.");
        builder.AppendLine("Compact live state plus tools is complete. Vision/screenshots are optional supporting context and are never required.");
        builder.AppendLine();
        builder.AppendLine(PlayContract.Trim());
        builder.AppendLine();
        builder.AppendLine(ScreenPlaybooks.Trim());
        builder.AppendLine();
        builder.Append("Each play step: inspect state (and metadata if needed), then call act exactly once. Vision is optional; legality still comes from live state.");
        return builder.ToString();
    }

    internal static string ExtractSharedContract(string skillMarkdown)
    {
        var begin = skillMarkdown.IndexOf(SharedContractBegin, StringComparison.Ordinal);
        var end = skillMarkdown.IndexOf(SharedContractEnd, StringComparison.Ordinal);
        if (begin < 0 || end < 0 || end <= begin)
        {
            throw new InvalidOperationException("sts2-mcp-player SKILL.md is missing the shared play contract markers.");
        }

        begin += SharedContractBegin.Length;
        return skillMarkdown[begin..end].Trim();
    }

    private static string ReadEmbedded(string name)
    {
        var assembly = typeof(PlayPrompt).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded play skill resource: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
