using System.Collections.Concurrent;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// What this session has learnt about a provider's streaming (W7): a gateway that truncates streamed
/// tool arguments does it every time, so after the second truncation the act turns of that model go
/// out without streaming from the first request instead of paying for a broken stream each turn.
/// </summary>
/// <remarks>
/// Kept per loop instance and per model route (endpoint + model id), in memory: a restart re-learns
/// it in two turns, and a provider that fixes its gateway is not stuck on the slower path forever.
/// </remarks>
internal sealed partial class AgentLoop
{
    /// <summary>Truncations seen before streaming is given up for a model's act turns.</summary>
    internal const int StreamTruncationsBeforeDemotion = 2;

    private readonly ConcurrentDictionary<string, int> _streamTruncations = new(StringComparer.Ordinal);

    private static string RouteKey(ResolvedModel resolved) =>
        (resolved.Endpoint.BaseUrl ?? string.Empty) + "|" + resolved.Model.Model;

    /// <summary>Whether this model's act turns should skip streaming from the start.</summary>
    internal bool StreamToolArgsUnreliable(ResolvedModel resolved) =>
        _streamTruncations.TryGetValue(RouteKey(resolved), out var count) && count >= StreamTruncationsBeforeDemotion;

    private void NoteStreamTruncation(ResolvedModel resolved) =>
        _streamTruncations.AddOrUpdate(RouteKey(resolved), 1, (_, count) => count + 1);

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
