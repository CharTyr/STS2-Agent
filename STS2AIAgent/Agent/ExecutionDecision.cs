using System.Text.Json.Nodes;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// The fast execution model's (Jev's) chosen action for one frame, resolved to the action+indices the
/// bridge accepts. It is a subset of <see cref="AgentTurnResult"/>: the orchestrator turns one into
/// the other, and the two-layer engine keeps the LLM planner out of the per-action loop -- Jev only
/// reads a single-frame snapshot plus the current <see cref="PlayStrategy"/> and answers immediately,
/// so slow model latency never sits between the game and the click.
/// </summary>
/// <remarks>
/// Every index is nullable because most actions take none of them; the concrete fields a chosen
/// <c>JevOption</c> carries decide which are set. The usage/confidence fields mirror
/// <see cref="AgentTurnResult"/> so a ledger can account a pure-Jev turn (no provider call) exactly
/// like a model turn -- a zero-request turn stays observable to the session budget instead of
/// vanishing from it.
/// </remarks>
internal sealed record ExecutionDecision
{
    /// <summary>The action name from <c>available_actions</c>, or null when the decider chose nothing.</summary>
    public string? Action { get; init; }

    /// <summary>Hand card index for <c>play_card</c>.</summary>
    public int? CardIndex { get; init; }

    /// <summary>Target index when the chosen card/potion/option targets something.</summary>
    public int? TargetIndex { get; init; }

    /// <summary>Option index for map/reward/shop/event/rest/lobby/potion choices.</summary>
    public int? OptionIndex { get; init; }

    /// <summary>Cell X for a coordinate action (crystal).</summary>
    public int? X { get; init; }

    /// <summary>Cell Y for a coordinate action (crystal).</summary>
    public int? Y { get; init; }

    /// <summary>Tool argument for an action that takes one (crystal).</summary>
    public string? Tool { get; init; }

    /// <summary>The one-sentence rationale the overlay and decision log show the player.</summary>
    public string? Reason { get; init; }

    /// <summary>Optional decider confidence in [0,1]; null when the decider does not score itself.</summary>
    public double? Confidence { get; init; }

    /// <summary>Optional per-option probabilities keyed by <see cref="JevOption.Id"/>; null when unscored.</summary>
    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    /// <summary>Model usage for this turn; null for a pure-Jev turn that never called the provider.</summary>
    public LlmUsage? Usage { get; init; }

    /// <summary>Provider requests spent producing this decision; 0 for a pure-Jev turn.</summary>
    public int RequestsSpent { get; init; }

    /// <summary>The decider's own failure, if it chose nothing because it could not decide.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The action+indices serialized to the exact JSON argument object <see cref="ActJsonParser"/> and
    /// the <c>act</c> handler read: <c>action</c>, <c>card_index</c>, <c>target_index</c>,
    /// <c>option_index</c>, <c>x</c>, <c>y</c>, <c>tool</c>, <c>reason</c>. Unused parameters are
    /// omitted, as the play contract's JSON fallback asks, so the object stays minimal and a null
    /// argument never reaches a handler that would reject a null where it wanted an integer.
    /// </summary>
    public string ToActArgumentsJson()
    {
        var arguments = new JsonObject();
        if (!string.IsNullOrWhiteSpace(Action))
        {
            arguments["action"] = Action;
        }

        if (CardIndex.HasValue)
        {
            arguments["card_index"] = CardIndex.Value;
        }

        if (TargetIndex.HasValue)
        {
            arguments["target_index"] = TargetIndex.Value;
        }

        if (OptionIndex.HasValue)
        {
            arguments["option_index"] = OptionIndex.Value;
        }

        if (X.HasValue)
        {
            arguments["x"] = X.Value;
        }

        if (Y.HasValue)
        {
            arguments["y"] = Y.Value;
        }

        if (!string.IsNullOrWhiteSpace(Tool))
        {
            arguments["tool"] = Tool;
        }

        if (!string.IsNullOrWhiteSpace(Reason))
        {
            arguments["reason"] = Reason;
        }

        return arguments.ToJsonString();
    }
}
