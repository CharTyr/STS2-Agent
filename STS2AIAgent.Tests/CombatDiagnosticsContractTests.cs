namespace STS2AIAgent.Tests;

/// <summary>
/// Source contracts for diagnostics that require the live Godot combat runtime.
/// The lightweight test project cannot instantiate CardModel or the action queue,
/// so it verifies that both raw state and agent view retain the required evidence.
/// </summary>
internal static class CombatDiagnosticsContractTests
{
    public static void HandPayloadKeepsNativeCanPlayEvidence()
    {
        var rawStateSource = ReadSource("STS2AIAgent/Game/GameStateService.cs");
        var handBody = WithoutWhitespace(MethodBody(rawStateSource, "BuildHandCardPayload"));
        var agentHandBody = WithoutWhitespace(MethodBody(rawStateSource, "BuildAgentHandCardPayload"));

        Assert.Contains(
            "varcanPlay=card.CanPlay(outvarreason,outvarpreventer)",
            handBody,
            StringComparison.Ordinal);
        Assert.Contains("playable=targetSupported&&canPlay", handBody, StringComparison.Ordinal);
        Assert.Contains("can_play_result=canPlay", handBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_reason_raw=", handBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_preventer_id=GetModelIdEntry(preventer)", handBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_preventer_type=preventer?.GetType().FullName", handBody, StringComparison.Ordinal);

        Assert.Contains("unplayable_reason=card.unplayable_reason", agentHandBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_reason_raw=card.unplayable_reason_raw", agentHandBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_preventer_id=card.unplayable_preventer_id", agentHandBody, StringComparison.Ordinal);
        Assert.Contains("unplayable_preventer_type=card.unplayable_preventer_type", agentHandBody, StringComparison.Ordinal);
    }

    public static void CombatPayloadDistinguishesQueueModalAndSnapshotLocks()
    {
        var rawStateSource = ReadSource("STS2AIAgent/Game/GameStateService.cs");
        var stateSource = WithoutWhitespace(rawStateSource);
        var combatBody = WithoutWhitespace(MethodBody(rawStateSource, "BuildCombatPayload"));
        var agentCombatBody = WithoutWhitespace(MethodBody(rawStateSource, "BuildAgentCombatPayload"));

        Assert.Contains("GetOpenModal()", stateSource, StringComparison.Ordinal);
        Assert.Contains("ActionExecutor.CurrentlyRunningAction", stateSource, StringComparison.Ordinal);
        Assert.Contains("ActionQueueSet.GetReadyAction()", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"modal_open\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"game_action_running\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"game_action_queued\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"snapshot_stabilizing\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("hand_in_card_play=hand?.InCardPlay", stateSource, StringComparison.Ordinal);
        Assert.Contains("hand_in_card_selection=hand?.IsInCardSelection", stateSource, StringComparison.Ordinal);
        Assert.Contains("action_readiness=BuildCombatActionReadinessPayload", combatBody, StringComparison.Ordinal);
        Assert.Contains("action_readiness=combat.action_readiness", agentCombatBody, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }
        }

        throw new FileNotFoundException($"Could not locate source file: {relativePath}");
    }

    private static string WithoutWhitespace(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static string MethodBody(string source, string methodName)
    {
        var nameIndex = source.LastIndexOf($" {methodName}(", StringComparison.Ordinal);
        var openBrace = nameIndex < 0 ? -1 : source.IndexOf('{', nameIndex);
        if (openBrace < 0)
        {
            throw new InvalidOperationException($"Method body is missing: {methodName}");
        }

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[openBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method body is unterminated: {methodName}");
    }
}
