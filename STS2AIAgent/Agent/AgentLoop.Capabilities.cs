using System.Collections.Concurrent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// How an act reaches the game, from the richest channel to the plainest. A provider that cannot carry
/// a channel's arguments is moved one step down, for the turn at once and for the route after it has
/// happened twice.
/// </summary>
internal enum ActChannel
{
    /// <summary>A tool call on a streamed reply: the default, and the only one that shows live reasoning.</summary>
    StreamedTools,

    /// <summary>A tool call on a non-streamed reply, for gateways that cut the arguments short only when streaming.</summary>
    Tools,

    /// <summary>
    /// No tools at all: the act is the JSON object the reply text carries (<see cref="PlayPrompt.JsonActFallback"/>).
    /// For models without tool calling, and for gateways that cut tool arguments short on both paths.
    /// </summary>
    ReplyJson
}

/// <summary>
/// What this session has learnt about a provider's act channels (W7). A gateway that truncates tool
/// arguments does it every time, so after the second truncation on a channel the act turns of that
/// model start one channel down instead of paying for a broken request each turn.
/// </summary>
/// <remarks>
/// Two layers, on purpose. A verdict is persisted per route (<see cref="ProviderCapability"/>) and
/// applies from the first request of the next session; a truncation observed live is remembered in
/// memory and reported back to the runtime, which writes the same persisted verdict once so the next
/// launch does not have to rediscover it. Both key on <see cref="ModelRoleProbe.Fingerprint"/>, so
/// editing the endpoint, model, key or thinking protocol starts clean.
///
/// The ladder ends at <see cref="ActChannel.ReplyJson"/> rather than at "stop": the gateway that cut
/// streamed arguments after the action name also cut the non-streamed ones (live, 2026-10-28), and
/// three such turns stopped auto-play. Plain reply text is the one thing every chat endpoint returns
/// whole, and the act parser already reads an act from it.
/// </remarks>
internal sealed partial class AgentLoop
{
    /// <summary>Truncations seen on a channel before a model's act turns start below it.</summary>
    internal const int StreamTruncationsBeforeDemotion = 2;

    private readonly ConcurrentDictionary<string, int> _truncations = new(StringComparer.Ordinal);

    /// <summary>Route-and-channel pairs whose live truncation was already handed to the runtime.</summary>
    private readonly ConcurrentDictionary<string, bool> _reportedDemotions = new(StringComparer.Ordinal);

    /// <summary>
    /// Told once per route and channel when live truncations prove the channel unusable. Wired by
    /// <c>AgentRuntime</c> to persist the verdict; null in test loops.
    /// </summary>
    private readonly Action<ResolvedModel, ActChannel>? _onActChannelUnreliable = null;

    private static string RouteKey(ResolvedModel resolved) => ModelRoleProbe.Fingerprint(resolved);

    private static string ChannelKey(ResolvedModel resolved, ActChannel channel) => RouteKey(resolved) + "\n" + channel;

    /// <summary>
    /// The channel a request starts on. A turn that may not act (chat) keeps the model's tools and its
    /// stream: only an act can be cut short, and the read tools are what a question needs.
    /// </summary>
    internal ActChannel ActChannelFor(ResolvedModel resolved, bool allowAct)
    {
        if (!resolved.Model.SupportsTools)
        {
            return ActChannel.ReplyJson;
        }

        if (!allowAct)
        {
            return ActChannel.StreamedTools;
        }

        if (Unreliable(resolved, ActChannel.Tools))
        {
            return ActChannel.ReplyJson;
        }

        return Unreliable(resolved, ActChannel.StreamedTools) ? ActChannel.Tools : ActChannel.StreamedTools;
    }

    /// <summary>
    /// Whether this model's act turns should skip streaming from the start: the model test recorded
    /// that its streamed arguments are unusable, or this session already saw two truncations.
    /// </summary>
    internal bool StreamToolArgsUnreliable(ResolvedModel resolved) => Unreliable(resolved, ActChannel.StreamedTools);

    private bool Unreliable(ResolvedModel resolved, ActChannel channel)
    {
        var persisted = channel == ActChannel.StreamedTools
            ? ProviderCapability.StreamToolArgsUnreliable(_settings(), resolved)
            : ProviderCapability.ToolArgsUnreliable(_settings(), resolved);
        return persisted
            || (_truncations.TryGetValue(ChannelKey(resolved, channel), out var count)
                && count >= StreamTruncationsBeforeDemotion);
    }

    /// <summary>
    /// The output-cap field this route wants. The persisted probe verdict decides whether the play
    /// request speaks <c>max_tokens</c> or <c>max_completion_tokens</c>; an unprobed route keeps the
    /// broadly compatible spelling and the client renames it once if the server objects.
    /// </summary>
    internal string MaxTokensParameter(ResolvedModel resolved) =>
        ProviderCapability.MaxTokensFieldFor(_settings(), resolved);

    /// <summary>
    /// Records that <paramref name="channel"/> could not carry this turn's act and returns the channel
    /// one step down. Moving to <see cref="ActChannel.ReplyJson"/> also tells the model how to answer
    /// without tools, in the system prompt the rest of the turn is sent with.
    /// </summary>
    private ActChannel Demote(ResolvedModel resolved, ActChannel channel, List<LlmMessage> messages)
    {
        NoteTruncation(resolved, channel);
        var next = channel == ActChannel.StreamedTools ? ActChannel.Tools : ActChannel.ReplyJson;
        if (next == ActChannel.ReplyJson)
        {
            AppendJsonActInstructions(messages);
        }

        return next;
    }

    private void NoteTruncation(ResolvedModel resolved, ActChannel channel)
    {
        var key = ChannelKey(resolved, channel);
        var count = _truncations.AddOrUpdate(key, 1, (_, seen) => seen + 1);
        if (count < StreamTruncationsBeforeDemotion)
        {
            return;
        }

        // One write per route, channel and session: the runtime is told as soon as the second
        // truncation proves the channel unreliable, not once per later turn.
        if (_reportedDemotions.TryAdd(key, true))
        {
            _onActChannelUnreliable?.Invoke(resolved, channel);
        }
    }

    /// <summary>
    /// Tells the model how to act without tools, by appending to the static system prompt at index 0.
    /// It belongs there rather than near the final instruction: it is part of the unchanging prefix, so
    /// it does not cost the steps that follow a cacheable message. Appended once.
    /// </summary>
    private static void AppendJsonActInstructions(List<LlmMessage> messages)
    {
        if (messages.Count == 0 || messages[0].Role != "system"
            || (messages[0].Content ?? string.Empty).Contains(PlayPrompt.JsonActFallback, StringComparison.Ordinal))
        {
            return;
        }

        messages[0] = LlmMessage.System((messages[0].Content ?? string.Empty) + "\n\n" + PlayPrompt.JsonActFallback);
    }

    /// <summary>The prompt half of <see cref="ActChannelFor"/>: an act turn that starts without tools is told how to answer.</summary>
    private void AppendJsonActFallbackIfNeeded(List<LlmMessage> messages, ResolvedModel resolved, bool allowAct)
    {
        if (allowAct && ActChannelFor(resolved, allowAct) == ActChannel.ReplyJson)
        {
            AppendJsonActInstructions(messages);
        }
    }

    /// <summary>
    /// An act that left out an index the frame can supply on its own (exactly one usable choice) is
    /// complete; executing it beats re-sending the whole request one channel down.
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
