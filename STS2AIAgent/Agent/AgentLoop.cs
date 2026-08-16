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

    public AgentLoop(IGameBridge bridge, ILlmClientFactory factory, Func<AgentSettings> settings)
    {
        _bridge = bridge;
        _factory = factory;
        _settings = settings;
    }

    public async Task<AgentTurnResult> ChatAsync(
        string userText,
        IReadOnlyList<ChatTurn> history,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = settings.ResolveConversationModel();
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(PlayPrompt.ChatSystem)
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

        var allowAct = options.AllowAct || PlayIntent.Detect(userText);
        var tools = allowAct ? AgentTools.Play : AgentTools.ReadOnly;
        return await CompleteWithToolsAsync(
            resolved,
            settings,
            messages,
            tools,
            allowAct,
            stopAfterAct: false,
            cancellationToken);
    }

    public async Task<AgentTurnResult> PlayOnceAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = settings.ResolvePlayModel();
        var actionable = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
        if (!actionable)
        {
            return new AgentTurnResult
            {
                Error = "Timed out waiting for an actionable state.",
                ToolRounds = 0
            };
        }

        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(PlayPrompt.PlaySystem),
            LlmMessage.User("Latest compact game state:\n" + stateJson)
        };

        var visionNote = await TryDescribeOrAttachVisionAsync(resolved, settings, attachRequested: true, cancellationToken);
        if (visionNote.Caption != null)
        {
            messages.Add(LlmMessage.User(visionNote.Caption));
        }

        if (visionNote.AttachToPrimary && visionNote.Jpeg != null)
        {
            messages.Add(LlmMessage.User("Screenshot of the current game view is attached. Use it as supporting context only.", visionNote.Jpeg));
        }

        messages.Add(LlmMessage.User("Choose the next legal action. Call get_game_state if needed, then act exactly once."));

        return await CompleteWithToolsAsync(
            resolved,
            settings,
            messages,
            AgentTools.Play,
            allowAct: true,
            stopAfterAct: true,
            cancellationToken);
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
        AgentSettings settings,
        List<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        bool allowAct,
        bool stopAfterAct,
        CancellationToken cancellationToken)
    {
        var client = _factory.Create(resolved.Endpoint);
        string? lastText = null;
        string? lastReasoning = null;
        string? acted = null;
        string? actResult = null;
        var rounds = 0;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            rounds = round + 1;
            var request = new LlmRequest
            {
                Model = resolved.Model.Model,
                Messages = messages.ToArray(),
                Tools = resolved.Model.SupportsTools ? tools : null,
                Thinking = settings.GetThinkingIntensity(),
                ThinkingMode = resolved.Model.ThinkingMode
            };

            LlmCompletion completion;
            try
            {
                completion = await client.CompleteAsync(request, cancellationToken);
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
                    ToolRounds = rounds
                };
            }

            lastText = completion.Content;
            lastReasoning = completion.Reasoning ?? lastReasoning;
            if (completion.ToolCalls.Count == 0)
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

            messages.Add(LlmMessage.Assistant(completion.Content, completion.ToolCalls));
            foreach (var call in completion.ToolCalls)
            {
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

                    var actOutcome = await ExecuteActAsync(call.ArgumentsJson, cancellationToken);
                    acted = actOutcome.Action;
                    actResult = actOutcome.ResultJson;
                    messages.Add(LlmMessage.Tool(call.Id, actOutcome.ResultJson));
                    if (stopAfterAct)
                    {
                        return new AgentTurnResult
                        {
                            AssistantText = completion.Content,
                            Reasoning = lastReasoning,
                            Acted = acted,
                            ActResultJson = actResult,
                            Error = actOutcome.Error,
                            ToolRounds = rounds
                        };
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
            Error = "Reached the tool-call round limit without a final answer.",
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
                Thinking = ThinkingIntensity.Off,
                ThinkingMode = "prompt"
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
                "get_available_actions" => await _bridge.GetAvailableActionsJsonAsync(cancellationToken),
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

    private async Task<(string? Action, string ResultJson, string? Error)> ExecuteActAsync(
        string argumentsJson,
        CancellationToken cancellationToken)
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
                return (action, json, "illegal action");
            }

            var cardIndex = ReadInt(args, "card_index");
            var targetIndex = ReadInt(args, "target_index");
            var optionIndex = ReadInt(args, "option_index");
            var actionsJson = await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
            var compactJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
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
                    option_index = optionIndex
                }, JsonOptions);
                return (action, json, indexError);
            }

            var result = await _bridge.ActAsync(
                action,
                cardIndex,
                targetIndex,
                optionIndex,
                cancellationToken);
            if (ActIndexValidator.IsUnsettled(result))
            {
                var settled = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
                var latest = await _bridge.GetCompactStateJsonAsync(cancellationToken);
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
        catch (Exception ex)
        {
            return (null, JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions), ex.Message);
        }
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
}
