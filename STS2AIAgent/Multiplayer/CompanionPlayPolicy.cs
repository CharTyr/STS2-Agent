namespace STS2AIAgent.Multiplayer;

internal readonly record struct CompanionMapOption(int Index, int VoteCount, bool HasLocalVote);

internal readonly record struct CompanionImmediateDecision(string Kind, string? Action, int? OptionIndex)
{
    public const string None = "none";
    public const string Act = "act";
    public const string Wait = "wait";
}

internal static class CompanionPlayPolicy
{
    public static CompanionImmediateDecision DecideMapVote(
        string screen,
        IReadOnlyList<string>? availableActions,
        IReadOnlyList<CompanionMapOption>? mapOptions)
    {
        var actions = availableActions ?? Array.Empty<string>();
        if (!string.Equals(screen, "MAP", StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionImmediateDecision(CompanionImmediateDecision.None, null, null);
        }

        if (!Contains(actions, "choose_map_node"))
        {
            return new CompanionImmediateDecision(CompanionImmediateDecision.None, null, null);
        }

        var options = mapOptions ?? Array.Empty<CompanionMapOption>();
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].HasLocalVote)
            {
                return new CompanionImmediateDecision(CompanionImmediateDecision.Wait, null, null);
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].VoteCount > 0)
            {
                return new CompanionImmediateDecision(
                    CompanionImmediateDecision.Act,
                    "choose_map_node",
                    options[i].Index);
            }
        }

        return new CompanionImmediateDecision(CompanionImmediateDecision.Wait, null, null);
    }

    private static bool Contains(IReadOnlyList<string> actions, string name)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
