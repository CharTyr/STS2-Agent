using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentLoop
{
    private const int MaxToolRounds = 8;

    /// <summary>The one-line nudge after a reply that explained instead of acting.</summary>
    internal const string ActNowNudge =
        "You replied without calling a tool. Call the act tool now with one action from available_actions in the latest state and the index fields it needs. Do not explain first.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly IGameBridge _bridge;
    private readonly ILlmClientFactory _factory;
    private readonly Func<AgentSettings> _settings;
    private readonly Func<string?>? _teamContext;
    private readonly Func<SessionBudgetGuard?>? _budgetGuard;
    private readonly Func<IReadOnlyList<DecisionLogEntry>>? _recentDecisions;
    private readonly Func<int>? _lastPromptTokens;
    private readonly IActionDecider? _decider;
    private readonly Func<IActionDecider?>? _deciderProvider;
    private readonly StrategyStore? _strategyStore;
    private readonly Func<double>? _confidenceThreshold;
    private readonly Func<string, (string Id, string Text)?>? _peekPlayInstruction;
    private readonly Action<string>? _acknowledgePlayInstruction;
    private readonly Action<string>? _onReasoningDelta;

    public AgentLoop(
        IGameBridge bridge,
        ILlmClientFactory factory,
        Func<AgentSettings> settings,
        Func<string?>? teamContext = null,
        Func<SessionBudgetGuard?>? budgetGuard = null,
        Func<IReadOnlyList<DecisionLogEntry>>? recentDecisions = null,
        Func<int>? lastPromptTokens = null,
        IActionDecider? decider = null,
        StrategyStore? strategyStore = null,
        Func<double>? confidenceThreshold = null,
        Func<IActionDecider?>? deciderProvider = null,
        Func<string, (string Id, string Text)?>? peekPlayInstruction = null,
        Action<string>? acknowledgePlayInstruction = null,
        Action<string>? onReasoningDelta = null)
    {
        _bridge = bridge;
        _factory = factory;
        _settings = settings;
        _teamContext = teamContext;
        _budgetGuard = budgetGuard;
        _recentDecisions = recentDecisions;
        _lastPromptTokens = lastPromptTokens;
        _decider = decider;
        _strategyStore = strategyStore;
        _confidenceThreshold = confidenceThreshold;
        _peekPlayInstruction = peekPlayInstruction;
        _acknowledgePlayInstruction = acknowledgePlayInstruction;
        // The streaming partial the caller wants to show while a reply is still arriving. It stays a
        // callback rather than a field on the result because a partial is only meaningful in flight.
        _onReasoningDelta = onReasoningDelta;
        // A live provider wins over the captured instance: the runtime constructs once, but the
        // Jev configuration can appear after that, and a decider frozen at construction is the
        // dual-layer toggle that never turns on.
        _deciderProvider = deciderProvider;
    }

    /// <summary>The decider for this turn: the live provider's answer when wired, else the captured one.</summary>
    private IActionDecider? ResolveDecider()
    {
        return _deciderProvider?.Invoke() ?? _decider;
    }

    public async Task<AgentTurnResult> ChatAsync(
        string userText,
        IReadOnlyList<ChatTurn> history,
        ChatOptions options,
        CancellationToken cancellationToken,
        Action<string>? checkState = null)
    {
        var settings = _settings();
        var resolved = options.TeammateConversation ? settings.ResolvePlayModel() : settings.ResolveConversationModel();
        var system = options.TeammateConversation ? PlayPrompt.TeammateChatSystem : PlayPrompt.ChatSystem;
        var languageInstruction = PlayPrompt.ReplyLanguageInstruction(settings.ReplyLanguage);
        if (languageInstruction.Length > 0)
        {
            system += Environment.NewLine + languageInstruction;
        }

        if (!string.IsNullOrWhiteSpace(options.ExtraSystemInstruction))
        {
            system += Environment.NewLine + options.ExtraSystemInstruction.Trim();
        }

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(system)
        };

        // The conversation stream carries display-only turns as well -- the reasoning and the action a
        // turn took, drawn as their own bubbles. They have roles no provider accepts, so only the two
        // conversational roles are replayed; the rest exist for the page that renders them.
        foreach (var turn in history.Where(turn => turn.Role is "user" or "assistant").TakeLast(12))
        {
            messages.Add(new LlmMessage { Role = turn.Role, Content = turn.Text });
        }

        if (options.AttachState)
        {
            var state = await _bridge.GetCompactStateJsonAsync(cancellationToken);
            checkState?.Invoke(state);
            messages.Add(LlmMessage.User("Current compact game state:\n" + state));
        }

        byte[]? screenshot = null;
        var visionNote = await TryDescribeOrAttachVisionAsync(
            resolved,
            settings,
            options.AttachScreenshot,
            cancellationToken);
        if (visionNote.Caption != null)
        {
            messages.Add(LlmMessage.User(visionNote.Caption));
        }

        screenshot = visionNote.AttachToPrimary ? visionNote.Jpeg : null;
        messages.Add(LlmMessage.User(userText, screenshot));

        // There is no "act" switch in the overlay: the conversation is read-only unless the player's
        // own words ask it to play. The explicit phrase is the whole opt-in, so a message that is not
        // asking for a move never moves one.
        var allowAct = !options.TeammateConversation &&
            !options.ReadOnly &&
            PlayIntent.Detect(userText);
        AppendJsonActFallbackIfNeeded(messages, resolved, allowAct);
        var tools = allowAct ? AgentTools.Play : AgentTools.ReadOnly;
        return await CompleteWithToolsAsync(
            resolved,
            messages,
            tools,
            allowAct,
            stopAfterAct: false,
            cancellationToken,
            checkState,
            initialUsage: visionNote.Usage,
            initialRequests: visionNote.RequestsSpent);
    }

    public async Task<AgentTurnResult> PlayOnceAsync(CancellationToken cancellationToken, Action<string>? checkState = null, Action<string>? reportPhase = null, Action<double?>? observeDecider = null)
    {
        reportPhase?.Invoke(PlayPhases.ReadingState);
        var settings = _settings();
        // Only the non-combat mode reads before waiting: it must yield to combat at once rather than
        // wait out an enemy turn first. Otherwise the read that used to sit here only re-checked the
        // run boundary the caller had just checked, and cost a full state build per decision.
        if (settings.NonCombatOnlyEnabled)
        {
            var initialStateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
            checkState?.Invoke(initialStateJson);
            if (NonCombatOnlyPolicy.IsCombatSnapshot(initialStateJson))
            {
                return WaitForCombat();
            }
        }

        reportPhase?.Invoke(PlayPhases.WaitingForGame);
        // One call: the frame that proved actionable is the frame whose state comes back.
        var ready = await _bridge.WaitUntilActionableWithStateAsync(TimeSpan.FromSeconds(20), cancellationToken);
        checkState?.Invoke(ready.CompactJson);
        if (!ready.Actionable)
        {
            return new AgentTurnResult
            {
                Error = "Timed out waiting for an actionable state.",
                WaitingForGame = true,
                ToolRounds = 0,
                RequestsSpent = 0
            };
        }

        var stateJson = ready.CompactJson;
        settings = _settings();
        if (settings.NonCombatOnlyEnabled && NonCombatOnlyPolicy.IsCombatSnapshot(stateJson))
        {
            return WaitForCombat();
        }

        var escape = await TryEscapeSandpitAsync(stateJson, cancellationToken, checkState, reportPhase);
        if (escape != null) return escape;

        // Dual-layer mode: when a decider is wired in, Jev picks the concrete action and the LLM only
        // plans. A confident-enough Jev answer is executed here and the LLM prompt below never runs;
        // a low-confidence or failed answer falls through to it, so an unsure fast model can never
        // strand the turn -- the slow path is always there to take over. The per-role toggle is read
        // live from settings, so switching the mode off stops Jev on the very next turn without
        // rebuilding the loop. The companion instance reads the co-op toggle; the host reads solo.
        var dualLayerOn = _teamContext != null
            ? settings.DualLayerCoopEnabled
            : settings.DualLayerSoloEnabled;
        // A player's one-shot instruction must reach the very next decision, not be skipped by a
        // confident Jev action. This turn uses the regular model when guidance is queued.
        var playInstruction = _peekPlayInstruction?.Invoke(stateJson);
        var pending = new AgentTurnResult { RequestsSpent = 0 };
        if (playInstruction == null && dualLayerOn && ResolveDecider() is { } decider && _strategyStore is { } store)
        {
            reportPhase?.Invoke(PlayPhases.AskingJev);
            pending = await TryDecideWithJevAsync(decider, store, cancellationToken, checkState, reportPhase, observeDecider);
            if (pending.Acted != null || pending.Error != null || pending.WaitingForCombat)
            {
                return pending;
            }
        }

        // The fallback half of the same dual-layer engine: when Jev could not commit, the slow model
        // still works from the planner's standing strategy instead of re-deriving one from the frame.
        var strategy = dualLayerOn && _strategyStore is { } strategyStore ? strategyStore.Current : null;
        var resolved = settings.ResolvePlayModel();
        return await PlayWithModelAsync(settings, resolved, stateJson, pending, cancellationToken, checkState, reportPhase,
            playInstruction, strategy);
    }

    internal static AgentTurnResult WaitForCombat(AgentTurnResult? spent = null) => new()
    {
        Reasoning = Loc.T("非战斗模式已启用；Agent 保持只读并等待战斗结束。"),
        WaitingForGame = true,
        WaitingForPlayer = true,
        WaitingForCombat = true,
        ToolRounds = spent?.ToolRounds ?? 0,
        Usage = spent?.Usage,
        RequestsSpent = spent?.RequestsSpent ?? 0,
        DangerScore = spent?.DangerScore,
        JevElapsedMilliseconds = spent?.JevElapsedMilliseconds,
        OfferedOptionIds = spent?.OfferedOptionIds,
        StrategyUpdatedAt = spent?.StrategyUpdatedAt
    };

    private async Task<AgentTurnResult> CompleteWithToolsAsync(
        ResolvedModel resolved,
        List<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        bool allowAct,
        bool stopAfterAct,
        CancellationToken cancellationToken,
        Action<string>? checkState = null,
        LlmUsage? initialUsage = null,
        int initialRequests = 0,
        Action<string>? reportPhase = null,
        Action? onFirstPrimaryRequest = null)
    {
        string? lastText = null;
        string? lastReasoning = null;
        string? acted = null;
        string? actResult = null;
        string? lastActError = null;
        string? actFingerprint = null;
        var actUnsettled = false;
        var rounds = 0;
        var readToolRounds = 0;
        var streamDemoted = false;
        var nudged = false;
        var accumulatedUsage = initialUsage;
        var requestsSpent = initialRequests;
        var toolStreamFallbackTried = false;

        void RememberAccepted(string action, string response, string? reason)
        {
            acted = action;
            actResult = response;
            actUnsettled = true;
            // The one-line reason the model put in the act arguments is a fallback, not an override:
            // the provider's real reasoning_content is the thinking the player asked to see, and it
            // must survive the act that followed it.
            lastReasoning ??= reason;
        }

        // One receipt shape for every exit, so a new field cannot be forgotten on one of them.
        AgentTurnResult Receipt(string? error = null, bool reasoningExhausted = false, bool requiresConfiguration = false) => new()
        {
            AssistantText = lastText, Reasoning = lastReasoning,
            Acted = acted, ActResultJson = actResult, Error = error, StateFingerprint = actFingerprint,
            ExecutedUnsettled = actUnsettled, ReasoningBudgetExhausted = reasoningExhausted,
            RequiresConfiguration = requiresConfiguration, ToolRounds = rounds, ReadToolRounds = readToolRounds,
            StreamDemoted = streamDemoted, Usage = accumulatedUsage, RequestsSpent = requestsSpent
        };

        // The one place an act from the model -- a tool call or JSON in the reply text -- is executed.
        async Task<(string ResultJson, string? Error)> RunActAsync(string argumentsJson)
        {
            var reason = TryReadActReason(argumentsJson);
            reportPhase?.Invoke(PlayPhases.ExecutingAction);
            var outcome = await ExecuteActAsync(argumentsJson, cancellationToken, checkState,
                (action, response) => RememberAccepted(action, response, reason));
            if (outcome.Error != null)
            {
                lastActError = outcome.Error;
                return (outcome.ResultJson, outcome.Error);
            }

            // Real reasoning_content wins; the act-args reason is the fallback.
            lastReasoning ??= reason;
            acted = outcome.Action;
            actResult = outcome.ResultJson;
            actFingerprint = outcome.Fingerprint;
            actUnsettled = outcome.Unsettled;
            lastActError = null;
            return (outcome.ResultJson, null);
        }

        try
        {
            var client = CreateClient(resolved.Endpoint);
            for (var round = 0; round < MaxToolRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rounds = round + 1;
                var request = new LlmRequest
                {
                    Model = resolved.Model.Model,
                    Messages = messages.ToArray(),
                    Tools = resolved.Model.SupportsTools ? tools : null,
                    Thinking = resolved.Model.GetThinkingIntensity(),
                    ThinkingMode = resolved.Model.ThinkingMode,
                    Stream = !(allowAct && resolved.Model.SupportsTools && toolStreamFallbackTried),
                    // The only request a player watches think: the turn's own completion. Vision
                    // captions and role probes run without a callback, so their reasoning is not shown.
                    OnReasoningDelta = _onReasoningDelta
                };

                var budgetReason = _budgetGuard?.Invoke()?.CheckBudget(requestsSpent, accumulatedUsage?.TotalTokens ?? 0);
                if (budgetReason != null)
                {
                    return Receipt(budgetReason);
                }

                LlmCompletion completion;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Acknowledge queued guidance only at the first primary provider dispatch.
                    // Vision/canceled preparation did not consume it; later rounds must not consume
                    // another instruction. The runtime callback is an in-memory, nonthrowing dequeue.
                    if (allowAct && _settings().NonCombatOnlyEnabled
                        && NonCombatOnlyPolicy.IsCombatSnapshot(await _bridge.GetCompactStateJsonAsync(cancellationToken)))
                        throw new NonCombatOnlyYieldException();
                    if (round == 0) onFirstPrimaryRequest?.Invoke();
                    requestsSpent++;
                    completion = await client.CompleteAsync(request, cancellationToken);
                    if (completion.Usage != null)
                    {
                        accumulatedUsage = LlmUsage.Combine(accumulatedUsage, completion.Usage);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Receipt(ex.Message,
                        requiresConfiguration: ex is LlmException { StatusCode: >= 400 and < 500 and not 408 and not 429 });
                }

                lastText = completion.Content;
                lastReasoning = completion.Reasoning ?? lastReasoning;
                if (completion.ToolCalls.Count == 0)
                {
                    var mayAct = allowAct && acted == null;
                    // An act written as JSON in the reply text is an act, whether or not the model has
                    // native tools: re-asking a tool-capable model that answered in text cost a whole
                    // second request for an answer it had already given.
                    if (mayAct && ActJsonParser.TryParse(completion.Content, wholeReplyOnly: resolved.Model.SupportsTools, out var actJson))
                    {
                        var parsed = await RunActAsync(actJson);
                        if (parsed.Error == null && stopAfterAct)
                        {
                            return Receipt();
                        }

                        messages.Add(LlmMessage.Assistant(completion.Content));
                        messages.Add(LlmMessage.User("Act result:\n" + parsed.ResultJson));
                        continue;
                    }

                    // A thinking model that spent its whole output on reasoning will do it again;
                    // the recovery policy owns that retry (with backoff), not this loop.
                    if (mayAct && stopAfterAct && resolved.Model.SupportsTools
                        && !CompletionErrors.IsReasoningBudgetExhausted(completion))
                    {
                        // Only a reply with nothing in it is the signature of a provider that cannot
                        // serialize tools on its stream path (or a stream that already failed a nudge):
                        // re-send once without streaming. Demoting on any tool-less reply re-sent the
                        // whole ~11k-token prompt on 63% of decisions (ledger of 2026-10-28).
                        var silent = string.IsNullOrWhiteSpace(completion.Content) && string.IsNullOrWhiteSpace(completion.Reasoning);
                        if (request.Stream && !toolStreamFallbackTried && (silent || nudged))
                        {
                            toolStreamFallbackTried = true;
                            streamDemoted = true;
                            continue;
                        }

                        // Prose instead of a tool call: one short nudge on the same conversation keeps
                        // the provider's prefix cache, unlike a fresh re-send.
                        if (!silent && !nudged)
                        {
                            nudged = true;
                            messages.Add(LlmMessage.Assistant(completion.Content, null, completion.Reasoning));
                            messages.Add(LlmMessage.User(ActNowNudge));
                            continue;
                        }
                    }

                    return Receipt(
                        acted == null ? (lastActError ?? CompletionErrors.Empty(completion, completion.Reasoning, rounds)) : null,
                        reasoningExhausted: acted == null && CompletionErrors.IsReasoningBudgetExhausted(completion));
                }

                // Some gateways stream only the first fragment of a tool call's arguments and then
                // finish the stream: the JSON closes after the action name, so the call is
                // syntactically valid but unusable. The non-streaming body of the same request carries
                // every field (measured live 2026-10-28), so retry once without streaming instead of
                // spending the three-failure budget on a request the provider never delivered.
                if (allowAct &&
                    acted == null &&
                    request.Stream &&
                    !toolStreamFallbackTried &&
                    completion.ToolCalls.Any(call =>
                        string.Equals(call.Name, "act", StringComparison.OrdinalIgnoreCase) &&
                        LooksLikeTruncatedStreamedAct(call.ArgumentsJson)))
                {
                    toolStreamFallbackTried = true;
                    streamDemoted = true;
                    continue;
                }

                // Carry the provider's reasoning back with the tool-call turn: DeepSeek/Kimi thinking
                // modes reject the next tool round with HTTP 400 when it is missing.
                var messagesBeforeToolCalls = messages.Count;
                var retryStreamedAct = false;
                var readThisRound = false;
                messages.Add(LlmMessage.Assistant(completion.Content, completion.ToolCalls, completion.Reasoning));
                foreach (var call in completion.ToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(call.Name, "act", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!allowAct)
                        {
                            messages.Add(LlmMessage.Tool(call.Id, """{"error":"act is disabled in chat mode"}"""));
                            continue;
                        }

                        if (acted != null)
                        {
                            messages.Add(LlmMessage.Tool(call.Id, """{"error":"only one act is allowed per decision"}"""));
                            continue;
                        }

                        var actOutcome = await RunActAsync(call.ArgumentsJson);
                        messages.Add(LlmMessage.Tool(call.Id, actOutcome.ResultJson));
                        if (actOutcome.Error == null)
                        {
                            if (stopAfterAct)
                            {
                                return Receipt();
                            }
                        }
                        else if (request.Stream &&
                            !toolStreamFallbackTried &&
                            IsMissingIndexError(actOutcome.Error))
                        {
                            // A stream can also stop right after card_index, leaving the target out.
                            // That only surfaces at validation, so use the rejection itself as the
                            // truncation signal and replay the turn once without streaming.
                            retryStreamedAct = true;
                            break;
                        }

                        continue;
                    }

                    readThisRound = true;
                    var toolJson = await ExecuteReadToolAsync(call.Name, call.ArgumentsJson, cancellationToken, checkState);
                    messages.Add(LlmMessage.Tool(call.Id, toolJson));
                }

                if (readThisRound)
                {
                    readToolRounds++;
                }

                if (retryStreamedAct)
                {
                    messages.RemoveRange(messagesBeforeToolCalls, messages.Count - messagesBeforeToolCalls);
                    toolStreamFallbackTried = true;
                    streamDemoted = true;
                    continue;
                }
            }

            return Receipt(acted == null && lastActError != null
                ? lastActError
                : "Reached the tool-call round limit without a final answer.");
        }
        catch (NonCombatOnlyYieldException ex)
        {
            ex.Receipt = Receipt();
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(Receipt(), ex, cancellationToken);
        }
        catch (AutoPlayStoppedException ex)
        {
            ex.Receipt = Receipt();
            throw;
        }
    }

    private async Task<(string? Caption, byte[]? Jpeg, bool AttachToPrimary, LlmUsage? Usage, int RequestsSpent)> TryDescribeOrAttachVisionAsync(
        ResolvedModel primary,
        AgentSettings settings,
        bool attachRequested,
        CancellationToken cancellationToken,
        int pendingRequests = 0,
        int pendingTokens = 0)
    {
        if (!attachRequested)
        {
            return (null, null, false, null, 0);
        }

        var vision = settings.TryResolveVisionModel();
        if (!primary.Model.SupportsVision && vision == null)
        {
            return (null, null, false, null, 0);
        }

        byte[]? jpeg;
        try
        {
            jpeg = await _bridge.CaptureScreenshotJpegAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            jpeg = null;
        }

        if (jpeg == null || jpeg.Length == 0)
        {
            return (null, null, false, null, 0);
        }

        if (primary.Model.SupportsVision)
        {
            return (null, jpeg, true, null, 0);
        }

        if (vision == null)
        {
            return (null, null, false, null, 0);
        }

        var visionBudget = _budgetGuard?.Invoke()?.CheckBudget(pendingRequests, pendingTokens);
        if (visionBudget != null)
        {
            return (visionBudget, jpeg, false, null, 0);
        }

        var visionRequests = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = CreateClient(vision.Endpoint);
            visionRequests = 1;
            var completion = await client.CompleteAsync(new LlmRequest
            {
                Model = vision.Model.Model,
                Messages = new[]
                {
                    LlmMessage.System("Describe this Slay the Spire 2 screenshot for a non-vision gameplay model. Focus on screen type, visible cards, enemies, rewards, and UI prompts. Be concise."),
                    LlmMessage.User("Describe the current game view.", jpeg)
                },
                Thinking = vision.Model.GetThinkingIntensity(),
                ThinkingMode = vision.Model.ThinkingMode
            }, cancellationToken);

            var caption = string.IsNullOrWhiteSpace(completion.Content)
                ? "Vision model returned an empty description."
                : "Vision observation:\n" + completion.Content;
            return (caption, jpeg, false, completion.Usage, 1);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(new AgentTurnResult { RequestsSpent = visionRequests }, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            return ("Vision model failed: " + ex.Message, jpeg, false, null, visionRequests);
        }
    }

    private async Task<string> ExecuteReadToolAsync(
        string name,
        string argumentsJson,
        CancellationToken cancellationToken,
        Action<string>? checkState = null)
    {
        try
        {
            using var args = ParseArgs(argumentsJson);
            return name switch
            {
                "get_game_state" => InvokeCheckState(await _bridge.GetCompactStateJsonAsync(cancellationToken), checkState),
                "get_raw_game_state" => await _bridge.GetRawStateJsonAsync(cancellationToken),
                "get_available_actions" => await _bridge.GetAvailableActionsJsonAsync(cancellationToken),
                "wait_until_actionable" => await WaitUntilActionableJsonAsync(args, cancellationToken, checkState),
                "get_game_data_item" => await _bridge.GetGameDataItemJsonAsync(
                    ReadString(args, "collection") ?? string.Empty,
                    ReadString(args, "item_id") ?? string.Empty,
                    cancellationToken),
                "get_game_data_items" => await _bridge.GetGameDataItemsJsonAsync(
                    ReadString(args, "collection") ?? string.Empty,
                    GameDataFilter.ParseItemIds(ReadString(args, "item_ids")),
                    cancellationToken),
                "get_relevant_game_data" => await _bridge.GetRelevantGameDataJsonAsync(
                    ReadString(args, "collection") ?? string.Empty,
                    GameDataFilter.ParseItemIds(ReadString(args, "item_ids")),
                    cancellationToken),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool '{name}'" }, JsonOptions)
            };
        }
        catch (AutoPlayStoppedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentErrorEnvelope.Serialize(ex, JsonOptions);
        }
    }

    private async Task<string> WaitUntilActionableJsonAsync(
        JsonDocument args,
        CancellationToken cancellationToken,
        Action<string>? checkState = null)
    {
        var timeout = TimeSpan.FromSeconds(ReadTimeoutSeconds(args));
        var actionable = await _bridge.WaitUntilActionableAsync(timeout, cancellationToken);
        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        checkState?.Invoke(stateJson);
        // The wait schema is shared with the MCP surface, so `raw_state` is honored here too rather
        // than advertised and ignored. `checkState` keeps the compact view either way: it feeds the
        // runtime's own state, not this answer.
        if (ReadBool(args, "raw_state"))
        {
            stateJson = await _bridge.GetRawStateJsonAsync(cancellationToken);
        }
        var actionsJson = await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            actionable,
            timeout_seconds = timeout.TotalSeconds,
            state = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson),
            actions = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(actionsJson) ? "[]" : actionsJson)
        }, JsonOptions);
    }

    private async Task<(string? Action, string ResultJson, string? Fingerprint, bool Unsettled, string? Error)> ExecuteActAsync(
        string argumentsJson,
        CancellationToken cancellationToken,
        Action<string>? checkState = null,
        Action<string, string>? onAccepted = null,
        bool requireSandpitEscape = false)
    {
        string? acceptedAction = null;
        string? acceptedResponse = null;
        try
        {
            using var args = ParseArgs(argumentsJson);
            var action = ReadString(args, "action")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
            {
                return (null, """{"error":"action is required"}""", null, false, "action is required");
            }

            // One snapshot for the legality check and the index validator. They used to be three
            // reads, each rebuilding the whole state on the game thread, and the game can advance
            // between two of them -- which is how the validator could judge an index against a
            // payload the legality check never saw.
            var snapshotJson = await _bridge.GetActionSnapshotJsonAsync(cancellationToken);
            if (_settings().NonCombatOnlyEnabled && NonCombatOnlyPolicy.IsCombatSnapshot(snapshotJson))
            {
                throw new NonCombatOnlyYieldException();
            }
            using var snapshot = ParseArgs(snapshotJson);
            var state = ReadSnapshotPart(snapshot, "state", JsonValueKind.Object);
            var descriptors = ReadSnapshotPart(snapshot, "available_actions", JsonValueKind.Array);
            var compactJson = state.GetRawText();
            checkState?.Invoke(compactJson);
            var actionsJson = descriptors.GetRawText();
            var legal = ReadActionNames(state);
            if (!legal.Contains(action, StringComparer.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    error = "Action is not in available_actions.",
                    action,
                    available_actions = legal
                }, JsonOptions);
                return (null, json, null, false, "illegal action");
            }

            var cardIndex = ReadInt(args, "card_index");
            var targetIndex = ReadInt(args, "target_index");
            var optionIndex = ReadInt(args, "option_index");
            var x = ReadInt(args, "x");
            var y = ReadInt(args, "y");
            var tool = ReadString(args, "tool");
            // The tool schema is shared with the MCP surface, so this one is honored here too rather
            // than advertised and ignored.
            var rawState = ReadBool(args, "raw_state");
            var indexError = ActIndexValidator.Validate(
                action,
                cardIndex,
                targetIndex,
                optionIndex,
                actionsJson,
                compactJson);
            if (indexError != null)
            {
                // The rejection carries the offending field, what was submitted, and the indices the
                // payload actually offers; the envelope and the echo around it are unchanged.
                var json = JsonSerializer.Serialize(new
                {
                    error = AgentErrorEnvelope.ToPayload(indexError.ToApiException(action, legal)),
                    action,
                    card_index = cardIndex,
                    target_index = targetIndex,
                    option_index = optionIndex,
                    x,
                    y,
                    tool
                }, JsonOptions);
                return (null, json, null, false, indexError.Message);
            }

            ValidateSandpitDispatch(compactJson, action, cardIndex, requireSandpitEscape);

            cancellationToken.ThrowIfCancellationRequested();
            var result = await _bridge.ActAsync(
                action,
                cardIndex,
                targetIndex,
                optionIndex,
                x,
                y,
                tool,
                cancellationToken,
                rawState,
                latest =>
                {
                    if (_settings().NonCombatOnlyEnabled && NonCombatOnlyPolicy.IsCombatSnapshot(latest))
                        throw new NonCombatOnlyYieldException();
                    ValidateSandpitDispatch(latest, action, cardIndex, requireSandpitEscape);
                });
            if (AgentErrorEnvelope.TryReadError(result, out var bridgeError))
            {
                return (null, result, null, false, bridgeError ?? "act failed");
            }

            acceptedAction = action;
            acceptedResponse = result;
            onAccepted?.Invoke(action, result);

            if (ActIndexValidator.IsUnsettled(result))
            {
                var settled = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
                var latest = await _bridge.GetCompactStateJsonAsync(cancellationToken);
                checkState?.Invoke(latest);
                result = JsonSerializer.Serialize(new
                {
                    action,
                    status = settled ? "completed" : "pending",
                    stable = settled,
                    previous = JsonSerializer.Deserialize<JsonElement>(result),
                    state = JsonSerializer.Deserialize<JsonElement>(latest)
                }, JsonOptions);
                // A pending response tells the agent to stay inside this screen flow, not that the
                // act failed: the game accepted it. Return it without an error so the retry policy
                // does not spend the failure budget, and carry the unsettled flag so a long run of
                // them can still be stopped.
                return (action, result, NoProgressPolicy.Fingerprint(latest), !settled, null);
            }

            var settledState = await _bridge.GetCompactStateJsonAsync(cancellationToken);
            checkState?.Invoke(settledState);
            return (action, result, NoProgressPolicy.Fingerprint(settledState), false, null);
        }
        catch (NonCombatOnlyYieldException)
        {
            throw;
        }
        catch (AutoPlayStoppedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (acceptedAction != null)
            {
                var pending = JsonSerializer.Serialize(new
                {
                    action = acceptedAction, status = "pending", stable = false,
                    message = "Action accepted; follow-up state is unavailable. Refresh state before the next decision.",
                    accepted_response = acceptedResponse,
                    observation_error = DiagnosticExport.Redact(ex.Message)
                }, JsonOptions);
                return (acceptedAction, pending, null, true, null);
            }
            var failure = ex is JsonException
                ? new ApiException(400, "invalid_request", ex.Message) : ex;
            return (null, AgentErrorEnvelope.Serialize(failure, JsonOptions), null, false, failure.Message);
        }
    }

    private static string InvokeCheckState(string json, Action<string>? checkState)
    {
        checkState?.Invoke(json);
        return json;
    }

    /// <summary>
    /// Tells a model that cannot call tools how to answer instead, by appending to the static system
    /// prompt at index 0. It belongs there rather than near the final instruction: it is part of the
    /// unchanging prefix, so it does not cost the steps that follow a cacheable message.
    /// </summary>
    private static void AppendJsonActFallbackIfNeeded(List<LlmMessage> messages, ResolvedModel resolved, bool allowAct)
    {
        if (resolved.Model.SupportsTools || !allowAct || messages.Count == 0 || messages[0].Role != "system")
        {
            return;
        }

        messages[0] = LlmMessage.System((messages[0].Content ?? string.Empty) + "\n\n" + PlayPrompt.JsonActFallback);
    }
}
