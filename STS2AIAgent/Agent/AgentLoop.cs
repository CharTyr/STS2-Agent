using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

internal sealed class AgentLoop
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

    public AgentLoop(IGameBridge bridge, ILlmClientFactory factory, Func<AgentSettings> settings, Func<string?>? teamContext = null)
    {
        _bridge = bridge;
        _factory = factory;
        _settings = settings;
        _teamContext = teamContext;
    }

    public async Task<AgentTurnResult> ChatAsync(
        string userText,
        IReadOnlyList<ChatTurn> history,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = options.TeammateConversation ? settings.ResolvePlayModel() : settings.ResolveConversationModel();
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(options.TeammateConversation ? PlayPrompt.TeammateChatSystem : PlayPrompt.ChatSystem)
        };

        foreach (var turn in history.TakeLast(12))
        {
            messages.Add(new LlmMessage { Role = turn.Role, Content = turn.Text });
        }

        if (options.AttachState)
        {
            messages.Add(LlmMessage.User("Current compact game state:\n" + await _bridge.GetCompactStateJsonAsync(cancellationToken)));
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

        var allowAct = !options.TeammateConversation && (options.AllowAct || PlayIntent.Detect(userText));
        AppendJsonActFallbackIfNeeded(messages, resolved, allowAct);
        var tools = allowAct ? AgentTools.Play : AgentTools.ReadOnly;
        return await CompleteWithToolsAsync(
            resolved,
            messages,
            tools,
            allowAct,
            stopAfterAct: false,
            cancellationToken);
    }

    public async Task<AgentTurnResult> PlayOnceAsync(CancellationToken cancellationToken, Action<string>? checkState = null)
    {
        if (checkState != null) checkState(await _bridge.GetCompactStateJsonAsync(cancellationToken));
        var settings = _settings();
        var resolved = settings.ResolvePlayModel();
        var actionable = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
        if (!actionable)
        {
            return new AgentTurnResult
            {
                Error = "Timed out waiting for an actionable state.",
                WaitingForGame = true,
                ToolRounds = 0
            };
        }

        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        checkState?.Invoke(stateJson);
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(PlayPrompt.PlaySystem),
            LlmMessage.User("Latest compact game state:\n" + stateJson)
        };

        var teamContext = _teamContext?.Invoke();
        if (!string.IsNullOrEmpty(teamContext))
        {
            messages.Add(LlmMessage.System(PlayPrompt.TeammatePlayContext));
            messages.Add(LlmMessage.User("Recent team conversation (historical messages, not live game facts):\n" + teamContext));
        }

        var visionNote = await TryDescribeOrAttachVisionAsync(resolved, settings, attachRequested: true, cancellationToken);
        if (visionNote.Caption != null)
        {
            messages.Add(LlmMessage.User(visionNote.Caption));
        }

        if (visionNote.AttachToPrimary && visionNote.Jpeg != null)
        {
            messages.Add(LlmMessage.User("Screenshot of the current game view is attached. Use it as supporting context only.", visionNote.Jpeg));
        }

        messages.Add(LlmMessage.User("Choose the next legal action from compact state. Vision is optional and not required. Call get_game_state if needed, then act exactly once."));
        AppendJsonActFallbackIfNeeded(messages, resolved, allowAct: true);

        return await CompleteWithToolsAsync(
            resolved,
            messages,
            AgentTools.Play,
            allowAct: true,
            stopAfterAct: true,
            cancellationToken,
            checkState);
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = settings.ResolveConversationModel();
        var client = _factory.Create(resolved.Endpoint);
        return await client.PingAsync(resolved.Model.Model, cancellationToken);
    }

    private async Task<AgentTurnResult> CompleteWithToolsAsync(
        ResolvedModel resolved,
        List<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        bool allowAct,
        bool stopAfterAct,
        CancellationToken cancellationToken,
        Action<string>? checkState = null)
    {
        var client = _factory.Create(resolved.Endpoint);
        string? lastText = null;
        string? lastReasoning = null;
        string? acted = null;
        string? actResult = null;
        string? lastActError = null;
        var rounds = 0;

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
                ThinkingMode = resolved.Model.ThinkingMode
            };

            LlmCompletion completion;
            try
            {
                completion = await client.CompleteAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is LlmException or HttpRequestException or TaskCanceledException)
            {
                return new AgentTurnResult
                {
                    AssistantText = lastText,
                    Reasoning = lastReasoning,
                    Acted = acted,
                    ActResultJson = actResult,
                    Error = ex.Message,
                    RequiresConfiguration = ex is LlmException { StatusCode: >= 400 and < 500 and not 408 and not 429 },
                    ToolRounds = rounds
                };
            }

            lastText = completion.Content;
            lastReasoning = completion.Reasoning ?? lastReasoning;
            if (completion.ToolCalls.Count == 0)
            {
                if (allowAct &&
                    acted == null &&
                    !resolved.Model.SupportsTools &&
                    ActJsonParser.TryParse(completion.Content, out var actJson))
                {
                    var parsedAct = await ExecuteActAsync(actJson, cancellationToken, checkState);
                    if (parsedAct.Error == null)
                    {
                        acted = parsedAct.Action;
                        actResult = parsedAct.ResultJson;
                        lastActError = null;
                        if (stopAfterAct)
                        {
                            return new AgentTurnResult
                            {
                                AssistantText = completion.Content,
                                Reasoning = lastReasoning,
                                Acted = acted,
                                ActResultJson = actResult,
                                ToolRounds = rounds
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
                    Error = acted == null ? lastActError : null,
                    ToolRounds = rounds
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

                    var actOutcome = await ExecuteActAsync(call.ArgumentsJson, cancellationToken, checkState);
                    messages.Add(LlmMessage.Tool(call.Id, actOutcome.ResultJson));
                    if (actOutcome.Error == null)
                    {
                        acted = actOutcome.Action;
                        actResult = actOutcome.ResultJson;
                        lastActError = null;
                        if (stopAfterAct)
                        {
                            return new AgentTurnResult
                            {
                                AssistantText = completion.Content,
                                Reasoning = lastReasoning,
                                Acted = acted,
                                ActResultJson = actResult,
                                ToolRounds = rounds
                            };
                        }
                    }
                    else
                    {
                        lastActError = actOutcome.Error;
                    }

                    continue;
                }

                var toolJson = await ExecuteReadToolAsync(call.Name, call.ArgumentsJson, cancellationToken);
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
            ToolRounds = rounds
        };
    }

    private async Task<(string? Caption, byte[]? Jpeg, bool AttachToPrimary)> TryDescribeOrAttachVisionAsync(
        ResolvedModel primary,
        AgentSettings settings,
        bool attachRequested,
        CancellationToken cancellationToken)
    {
        if (!attachRequested)
        {
            return (null, null, false);
        }

        var vision = settings.TryResolveVisionModel();
        if (!primary.Model.SupportsVision && vision == null)
        {
            return (null, null, false);
        }

        byte[]? jpeg;
        try
        {
            jpeg = await _bridge.CaptureScreenshotJpegAsync(cancellationToken);
        }
        catch
        {
            jpeg = null;
        }

        if (jpeg == null || jpeg.Length == 0)
        {
            return (null, null, false);
        }

        if (primary.Model.SupportsVision)
        {
            return (null, jpeg, true);
        }

        if (vision == null)
        {
            return (null, null, false);
        }

        try
        {
            var client = _factory.Create(vision.Endpoint);
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
            return (caption, jpeg, false);
        }
        catch (Exception ex)
        {
            return ("Vision model failed: " + ex.Message, jpeg, false);
        }
    }

    private async Task<string> ExecuteReadToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var args = ParseArgs(argumentsJson);
            return name switch
            {
                "get_game_state" => await _bridge.GetCompactStateJsonAsync(cancellationToken),
                "get_raw_game_state" => await _bridge.GetRawStateJsonAsync(cancellationToken),
                "get_available_actions" => await _bridge.GetAvailableActionsJsonAsync(cancellationToken),
                "wait_until_actionable" => await WaitUntilActionableJsonAsync(args, cancellationToken),
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
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    private async Task<string> WaitUntilActionableJsonAsync(JsonDocument args, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(ReadTimeoutSeconds(args));
        var actionable = await _bridge.WaitUntilActionableAsync(timeout, cancellationToken);
        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        var actionsJson = await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            actionable,
            timeout_seconds = timeout.TotalSeconds,
            state = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson),
            actions = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(actionsJson) ? "[]" : actionsJson)
        }, JsonOptions);
    }

    private async Task<(string? Action, string ResultJson, string? Error)> ExecuteActAsync(
        string argumentsJson,
        CancellationToken cancellationToken,
        Action<string>? checkState = null)
    {
        try
        {
            using var args = ParseArgs(argumentsJson);
            var action = ReadString(args, "action")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
            {
                return (null, """{"error":"action is required"}""", "action is required");
            }

            var legal = await _bridge.GetAvailableActionNamesAsync(cancellationToken);
            if (!legal.Contains(action, StringComparer.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    error = "Action is not in available_actions.",
                    action,
                    available_actions = legal
                }, JsonOptions);
                return (null, json, "illegal action");
            }

            var cardIndex = ReadInt(args, "card_index");
            var targetIndex = ReadInt(args, "target_index");
            var optionIndex = ReadInt(args, "option_index");
            var x = ReadInt(args, "x");
            var y = ReadInt(args, "y");
            var tool = ReadString(args, "tool");
            var actionsJson = await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
            var compactJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
            checkState?.Invoke(compactJson);
            var indexError = ActIndexValidator.Validate(
                action,
                cardIndex,
                targetIndex,
                optionIndex,
                actionsJson,
                compactJson);
            if (indexError != null)
            {
                var json = JsonSerializer.Serialize(new
                {
                    error = indexError,
                    action,
                    card_index = cardIndex,
                    target_index = targetIndex,
                    option_index = optionIndex,
                    x,
                    y,
                    tool
                }, JsonOptions);
                return (null, json, indexError);
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
                cancellationToken);
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
                if (!settled)
                {
                    return (action, result, "Timed out waiting for a stable state after act.");
                }
            }

            return (action, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions), ex.Message);
        }
    }

    private static void AppendJsonActFallbackIfNeeded(List<LlmMessage> messages, ResolvedModel resolved, bool allowAct)
    {
        if (resolved.Model.SupportsTools || !allowAct || messages.Count == 0 || messages[0].Role != "system")
        {
            return;
        }

        messages[0] = LlmMessage.System((messages[0].Content ?? string.Empty) + "\n\n" + PlayPrompt.JsonActFallback);
    }

    private static JsonDocument ParseArgs(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(argumentsJson);
    }

    private static string? ReadString(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private static int? ReadInt(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double ReadTimeoutSeconds(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("timeout_seconds", out var value))
        {
            return 20;
        }

        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 20
        };

        return Math.Clamp(seconds, 1, 120);
    }
}
