using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentLoop
{
    private const int MaxToolRounds = 8;
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
        Action<string>? acknowledgePlayInstruction = null)
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
        if (checkState != null) checkState(await _bridge.GetCompactStateJsonAsync(cancellationToken));
        var settings = _settings();
        var resolved = settings.ResolvePlayModel();
        reportPhase?.Invoke(PlayPhases.WaitingForGame);
        var actionable = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
        if (!actionable)
        {
            checkState?.Invoke(await _bridge.GetCompactStateJsonAsync(cancellationToken));
            return new AgentTurnResult
            {
                Error = "Timed out waiting for an actionable state.",
                WaitingForGame = true,
                ToolRounds = 0,
                RequestsSpent = 0
            };
        }

        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        checkState?.Invoke(stateJson);

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
            if (pending.Acted != null || pending.Error != null)
            {
                return pending;
            }
        }

        return await PlayWithModelAsync(settings, resolved, stateJson, pending, cancellationToken, checkState, reportPhase,
            playInstruction);
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var results = await TestConfiguredRolesAsync(force: true, cancellationToken);
        var play = results.FirstOrDefault(item => item.Role == ModelRoleNames.Play);
        return play.Record.Status == "verified" ? "ok" : play.Record.Error ?? "failed";
    }

    public async Task<IReadOnlyList<ModelRoleProbeResult>> TestConfiguredRolesAsync(bool force, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var results = new List<ModelRoleProbeResult>();
        var cache = new Dictionary<string, ModelRoleTestRecord>(StringComparer.Ordinal);
        foreach (var role in new[] { ModelRoleNames.Conversation, ModelRoleNames.Play, ModelRoleNames.Vision })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ModelRoleProbe.Resolve(settings, role);
            if (role == ModelRoleNames.Vision && resolved == null)
            {
                results.Add(new ModelRoleProbeResult(role, ModelRoleProbe.Unused(role), false));
                continue;
            }

            if (resolved == null)
            {
                results.Add(new ModelRoleProbeResult(role, ModelRoleProbe.Unverified(role, null), false));
                continue;
            }

            var current = ModelRoleProbe.Current(settings, role);
            var fingerprint = ModelRoleProbe.Fingerprint(resolved);
            if (!force && current.Status == "verified" && current.Fingerprint == fingerprint)
            {
                results.Add(new ModelRoleProbeResult(role, current, true));
                continue;
            }

            if (cache.TryGetValue(fingerprint, out var cached))
            {
                results.Add(new ModelRoleProbeResult(role, CopyForRole(cached, role), false));
                continue;
            }

            try
            {
                var client = CreateClient(resolved.Endpoint);
                await client.PingAsync(resolved.Model.Model, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var record = ModelRoleProbe.FromSuccess(role, resolved);
                cache[fingerprint] = record;
                results.Add(new ModelRoleProbeResult(role, record, false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var record = ModelRoleProbe.FromException(role, resolved, ex);
                cache[fingerprint] = record;
                results.Add(new ModelRoleProbeResult(role, record, false));
            }
        }

        return results;
    }

    private static ModelRoleTestRecord CopyForRole(ModelRoleTestRecord source, string role)
    {
        return new ModelRoleTestRecord
        {
            Role = role,
            Status = source.Status,
            CapabilityStatus = source.CapabilityStatus,
            EndpointId = source.EndpointId,
            EndpointName = source.EndpointName,
            ModelId = source.ModelId,
            ModelName = source.ModelName,
            Fingerprint = source.Fingerprint,
            StatusCode = source.StatusCode,
            Error = source.Error,
            NextStep = source.NextStep,
            TestedAt = source.TestedAt
        };
    }

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

        AgentTurnResult Receipt() => new()
        {
            AssistantText = lastText, Reasoning = lastReasoning,
            Acted = acted, ActResultJson = actResult, StateFingerprint = actFingerprint,
            ExecutedUnsettled = actUnsettled, ToolRounds = rounds,
            Usage = accumulatedUsage, RequestsSpent = requestsSpent
        };

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
                    Stream = !(allowAct && resolved.Model.SupportsTools && toolStreamFallbackTried)
                };

                var budgetReason = _budgetGuard?.Invoke()?.CheckBudget(requestsSpent, accumulatedUsage?.TotalTokens ?? 0);
                if (budgetReason != null)
                {
                    return new AgentTurnResult
                    {
                        AssistantText = lastText,
                        Reasoning = lastReasoning,
                        Acted = acted,
                        ActResultJson = actResult,
                        Error = budgetReason,
                        StateFingerprint = actFingerprint,
                        ExecutedUnsettled = actUnsettled,
                        ToolRounds = rounds,
                        Usage = accumulatedUsage,
                        RequestsSpent = requestsSpent
                    };
                }

                LlmCompletion completion;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Acknowledge queued guidance only at the first primary provider dispatch.
                    // Vision/canceled preparation did not consume it; later rounds must not consume
                    // another instruction. The runtime callback is an in-memory, nonthrowing dequeue.
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
                    return new AgentTurnResult
                    {
                        AssistantText = lastText,
                        Reasoning = lastReasoning,
                        Acted = acted,
                        ActResultJson = actResult,
                        StateFingerprint = actFingerprint,
                        ExecutedUnsettled = actUnsettled,
                        Error = ex.Message,
                        RequiresConfiguration = ex is LlmException { StatusCode: >= 400 and < 500 and not 408 and not 429 },
                        ToolRounds = rounds,
                        Usage = accumulatedUsage,
                        RequestsSpent = requestsSpent
                    };
                }

                lastText = completion.Content;
                lastReasoning = completion.Reasoning ?? lastReasoning;
                if (completion.ToolCalls.Count == 0)
                {
                    // A few OpenAI-compatible providers serialize tools correctly only for their
                    // non-streaming response path. No action has been accepted at this point, so
                    // one request demotion is safe; do not loop forever if the provider is simply
                    // refusing to call a tool.
                    if (allowAct &&
                        acted == null &&
                        stopAfterAct &&
                        resolved.Model.SupportsTools &&
                        request.Stream &&
                        !toolStreamFallbackTried)
                    {
                        toolStreamFallbackTried = true;
                        continue;
                    }

                    if (allowAct &&
                        acted == null &&
                        !resolved.Model.SupportsTools &&
                        ActJsonParser.TryParse(completion.Content, out var actJson))
                    {
                        var fallbackReason = TryReadActReason(actJson);
                        reportPhase?.Invoke(PlayPhases.ExecutingAction);
                        var parsedAct = await ExecuteActAsync(actJson, cancellationToken, checkState,
                            (action, response) => RememberAccepted(action, response, fallbackReason));
                        if (parsedAct.Error == null)
                        {
                            // The provider's reasoning_content (already in lastReasoning from this
                            // completion) is the thinking; the act-args reason only fills the gap
                            // when the provider sent none.
                            lastReasoning ??= fallbackReason;
                            acted = parsedAct.Action;
                            actResult = parsedAct.ResultJson;
                            actFingerprint = parsedAct.Fingerprint;
                            actUnsettled = parsedAct.Unsettled;
                            lastActError = null;
                            if (stopAfterAct)
                            {
                                return new AgentTurnResult
                                {
                                    AssistantText = completion.Content,
                                    Reasoning = lastReasoning,
                                    Acted = acted,
                                    ActResultJson = actResult,
                                    StateFingerprint = actFingerprint,
                                    ExecutedUnsettled = actUnsettled,
                                    ToolRounds = rounds,
                                    Usage = accumulatedUsage,
                                    RequestsSpent = requestsSpent
                                };
                            }
                        }
                        else
                        {
                            lastActError = parsedAct.Error;
                        }

                        messages.Add(LlmMessage.Assistant(completion.Content));
                        messages.Add(LlmMessage.User("Act result:\n" + parsedAct.ResultJson));
                        continue;
                    }

                    return new AgentTurnResult
                    {
                        AssistantText = completion.Content,
                        Reasoning = lastReasoning,
                        Acted = acted,
                        ActResultJson = actResult,
                        Error = acted == null
                            ? (lastActError ?? CompletionErrors.Empty(completion, completion.Reasoning, rounds))
                            : null,
                        ReasoningBudgetExhausted = acted == null && CompletionErrors.IsReasoningBudgetExhausted(completion),
                        StateFingerprint = actFingerprint,
                        ExecutedUnsettled = actUnsettled,
                        ToolRounds = rounds,
                        Usage = accumulatedUsage,
                        RequestsSpent = requestsSpent
                    };
                }

                messages.Add(LlmMessage.Assistant(completion.Content, completion.ToolCalls));
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

                        var actReason = TryReadActReason(call.ArgumentsJson);
                        reportPhase?.Invoke(PlayPhases.ExecutingAction);
                        var actOutcome = await ExecuteActAsync(call.ArgumentsJson, cancellationToken, checkState,
                            (action, response) => RememberAccepted(action, response, actReason));
                        messages.Add(LlmMessage.Tool(call.Id, actOutcome.ResultJson));
                        if (actOutcome.Error == null)
                        {
                            // Real reasoning_content wins; the act-args reason is the fallback.
                            lastReasoning ??= actReason;
                            acted = actOutcome.Action;
                            actResult = actOutcome.ResultJson;
                            actFingerprint = actOutcome.Fingerprint;
                            actUnsettled = actOutcome.Unsettled;
                            lastActError = null;
                            if (stopAfterAct)
                            {
                                return new AgentTurnResult
                                {
                                    AssistantText = completion.Content,
                                    Reasoning = lastReasoning,
                                    Acted = acted,
                                    ActResultJson = actResult,
                                    StateFingerprint = actFingerprint,
                                    ExecutedUnsettled = actUnsettled,
                                    ToolRounds = rounds,
                                    Usage = accumulatedUsage,
                                    RequestsSpent = requestsSpent
                                };
                            }
                        }
                        else
                        {
                            lastActError = actOutcome.Error;
                        }

                        continue;
                    }

                    var toolJson = await ExecuteReadToolAsync(call.Name, call.ArgumentsJson, cancellationToken, checkState);
                    messages.Add(LlmMessage.Tool(call.Id, toolJson));
                }
            }

            return new AgentTurnResult
            {
                AssistantText = lastText,
                Reasoning = lastReasoning,
                Acted = acted,
                ActResultJson = actResult,
                Error = acted == null && lastActError != null
                    ? lastActError
                    : "Reached the tool-call round limit without a final answer.",
                StateFingerprint = actFingerprint,
                ExecutedUnsettled = actUnsettled,
                ToolRounds = rounds,
                Usage = accumulatedUsage,
                RequestsSpent = requestsSpent
            };
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
        Action<string, string>? onAccepted = null)
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
                rawState);
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
