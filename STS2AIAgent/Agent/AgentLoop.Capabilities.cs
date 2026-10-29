using System.Collections.Concurrent;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// What this session has learnt about a provider's streaming (W7): a gateway that truncates streamed
/// tool arguments does it every time, so after the second truncation the act turns of that model go
/// out without streaming from the first request instead of paying for a broken stream each turn.
/// </summary>
/// <remarks>
/// Two layers, on purpose. A model-test verdict is persisted per route (<see
/// cref="ProviderCapability"/>) and applies from the first request of the next session; a truncation
/// observed live is remembered in memory and reported back to the runtime, which writes the same
/// persisted verdict once so the next launch does not have to rediscover it. Both key on
/// <see cref="ModelRoleProbe.Fingerprint"/>, so editing the endpoint, model, key or thinking protocol
/// starts clean.
/// </remarks>
internal sealed partial class AgentLoop
{
    /// <summary>Truncations seen before streaming is given up for a model's act turns.</summary>
    internal const int StreamTruncationsBeforeDemotion = 2;

    private readonly ConcurrentDictionary<string, int> _streamTruncations = new(StringComparer.Ordinal);

    /// <summary>Routes whose live truncation was already handed to the runtime for persistence.</summary>
    private readonly ConcurrentDictionary<string, bool> _reportedDemotions = new(StringComparer.Ordinal);

    /// <summary>
    /// Told once per route when a live truncation proves the provider's stream path unusable. Wired by
    /// <c>AgentRuntime</c> to write the same verdict the model test persists; null in test loops.
    /// </summary>
    private readonly Action<ResolvedModel>? _onStreamToolArgsUnreliable = null;

    private static string RouteKey(ResolvedModel resolved) => ModelRoleProbe.Fingerprint(resolved);

    /// <summary>
    /// Whether this model's act turns should skip streaming from the start: the model test recorded
    /// that its streamed arguments are unusable, or this session already saw two truncations.
    /// </summary>
    internal bool StreamToolArgsUnreliable(ResolvedModel resolved) =>
        ProviderCapability.StreamToolArgsUnreliable(_settings(), resolved)
        || (_streamTruncations.TryGetValue(RouteKey(resolved), out var count)
            && count >= StreamTruncationsBeforeDemotion);

    /// <summary>
    /// The output-cap field this route wants. The persisted probe verdict decides whether the play
    /// request speaks <c>max_tokens</c> or <c>max_completion_tokens</c>; an unprobed route keeps the
    /// broadly compatible spelling and the client renames it once if the server objects.
    /// </summary>
    internal string MaxTokensParameter(ResolvedModel resolved) =>
        ProviderCapability.MaxTokensFieldFor(_settings(), resolved);

    private void NoteStreamTruncation(ResolvedModel resolved)
    {
        var count = _streamTruncations.AddOrUpdate(RouteKey(resolved), 1, (_, seen) => seen + 1);
        if (count < StreamTruncationsBeforeDemotion)
        {
            return;
        }

        // One write per route and session: the runtime is told as soon as the second truncation proves
        // the provider's stream path is unreliable, not once per later turn.
        if (_reportedDemotions.TryAdd(RouteKey(resolved), true))
        {
            _onStreamToolArgsUnreliable?.Invoke(resolved);
        }
    }

    /// <summary>
    /// An act that left out an index the frame can supply on its own (exactly one usable choice) is
    /// complete; executing it beats re-sending the whole request without streaming.
    /// </summary>
    private static bool CanFillMissingIndex(string? argumentsJson, string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return false;
        }

        try
        {
            using var args = ParseArgs(argumentsJson);
            var action = ReadString(args, "action")?.Trim().ToLowerInvariant();
            return action != null && ActIndexValidator.ResolveUniqueIndex(action, stateJson) != null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
