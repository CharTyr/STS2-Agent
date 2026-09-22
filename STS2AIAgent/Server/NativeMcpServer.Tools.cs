using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Server;

/// <summary>
/// Tool execution for the native MCP surface: given a tool name and its arguments, produce the
/// tool's JSON text.
/// </summary>
/// <remarks>
/// The other half of <see cref="NativeMcpServer"/> is the transport: HTTP/SSE framing, sessions,
/// Origin checks, and JSON-RPC dispatch. This half is what a tool actually does. They were one file
/// until it crossed its 1,000-line budget, and the split is the ratchet working rather than the
/// budget being raised -- the same move the two action/state services and the overlay's tabs made.
///
/// The argument readers live here too, and deliberately: every one of them exists to read a tool's
/// `arguments` object, and a reader that belonged to the transport would be a key the alignment
/// contract cannot see (see <c>test_native_tool_alignment.py</c>, which compares the keys each
/// switch case parses against the Python surface's `inputSchema`).
/// </remarks>
internal sealed partial class NativeMcpServer
{
    private async Task<string> ExecuteToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case "health_check":
                return JsonSerializer.Serialize(_health(), JsonOptions);
            case "get_game_state":
                return await _bridge.GetCompactStateJsonAsync(cancellationToken);
            case "get_raw_game_state":
                return await _bridge.GetRawStateJsonAsync(cancellationToken);
            case "get_available_actions":
                return await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
            case "wait_until_actionable":
                return await WaitUntilActionableJsonAsync(arguments, cancellationToken);
            case "get_game_data_item":
                return await _bridge.GetGameDataItemJsonAsync(
                    ReadString(arguments, "collection") ?? string.Empty,
                    ReadString(arguments, "item_id") ?? string.Empty,
                    cancellationToken);
            case "get_game_data_items":
                return await _bridge.GetGameDataItemsJsonAsync(
                    ReadString(arguments, "collection") ?? string.Empty,
                    GameDataFilter.ParseItemIds(ReadString(arguments, "item_ids")),
                    cancellationToken);
            case "get_relevant_game_data":
                return await _bridge.GetRelevantGameDataJsonAsync(
                    ReadString(arguments, "collection") ?? string.Empty,
                    GameDataFilter.ParseItemIds(ReadString(arguments, "item_ids")),
                    cancellationToken);
            case "act":
                return await ExecuteActAsync(arguments, cancellationToken);
            case "decide":
                return await DecideJsonAsync(cancellationToken);
            case "get_decision_log":
                // The key is read inline so test_native_tool_alignment can see it: a delegated
                // reader would be invisible to the argument-name comparison.
                return _decisions?.RenderJson(Math.Clamp(ReadInt(arguments, "limit") ?? 50, 1, 200))
                    ?? """{"decisions":[]}""";
            case "get_run_summary":
                return await GetRunSummaryJsonAsync(cancellationToken);
            case "get_scene_guidance":
                return await GetSceneGuidanceJsonAsync(cancellationToken);
            case "diff_state":
                // The three keys are named here rather than inside the helper so the argument-name
                // comparison in test_native_tool_alignment can see them.
                return JsonSerializer.Serialize(
                    StateViews.BuildStateDiff(
                        ReadObject(arguments, "before"),
                        ReadObject(arguments, "after"),
                        Math.Clamp(ReadInt(arguments, "limit") ?? StateViews.MaxDiffEntries, 1, 200)),
                    JsonOptions);
            case "get_planner_briefing":
                return GetPlannerBriefingJson();
            case "update_play_strategy":
                // The strategy keys are read inline so the argument-name comparison can see them.
                return UpdatePlayStrategyJson(
                    ReadString(arguments, "posture"),
                    ReadString(arguments, "instructions"),
                    ReadObject(arguments, "option_hints"));
            default:
                return JsonSerializer.Serialize(new
                {
                    error = AgentErrorEnvelope.ToPayload(
                        new ApiException(404, "unknown_tool", "Unknown tool '" + name + "'."))
                }, JsonOptions);
        }
    }

    private async Task<string> GetRunSummaryJsonAsync(CancellationToken cancellationToken)
    {
        var stateJson = await _bridge.GetRawStateJsonAsync(cancellationToken);
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson);
        var summary = StateViews.BuildRunSummary(document.RootElement);
        return JsonSerializer.Serialize(new { run = summary }, JsonOptionsKeepingNulls);
    }

    /// <summary>
    /// The strategy guidance for the screen the game is on, and how to drive that screen.
    /// </summary>
    /// <remarks>
    /// This surface serves what the mod itself ships: the embedded strategy and playbook references,
    /// sliced by screen exactly as the in-game loop receives them. The Python sidecar additionally
    /// looks up the generated per-option event risk index, which the mod does not carry — that
    /// difference is deliberate and documented rather than papered over, because shipping the index
    /// inside the mod is a packaging change, not a code one. Both surfaces answer the same four keys:
    /// <c>screen</c>, <c>scene</c>, <c>guidance</c>, and <c>playbook</c>.
    /// </remarks>
    private async Task<string> GetSceneGuidanceJsonAsync(CancellationToken cancellationToken)
    {
        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        return JsonSerializer.Serialize(
            BuildSceneGuidance(PlaybookSections.ScreenOfCompactState(stateJson)),
            JsonOptionsKeepingNulls);
    }

    /// <summary>
    /// The current play strategy and the dual-layer status, for an external planner about to steer the
    /// Jev execution model. Reads the injected store -- the same one the in-game planner writes -- so
    /// the briefing an MCP client sees is the strategy the decider is actually following. When the
    /// store was not bound (an offline harness), the tool says so rather than throwing.
    /// </summary>
    private string GetPlannerBriefingJson()
    {
        if (_strategyStore == null || _dualLayerStatus == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = AgentErrorEnvelope.ToPayload(
                    new ApiException(503, "unavailable", "The dual-layer strategy store is not bound on this instance."))
            }, JsonOptions);
        }

        var status = _dualLayerStatus();
        return JsonSerializer.Serialize(new
        {
            strategy = JsonSerializer.Deserialize<JsonElement>(_strategyStore.Current.ToJson()),
            dual_layer = status.DualLayer,
            jev_configured = status.JevConfigured
        }, JsonOptionsKeepingNulls);
    }

    /// <summary>
    /// Applies a planner's strategy update. Any field the caller omits keeps the current value, so a
    /// client can nudge the posture without restating the instructions; a body with no recognizable
    /// field at all is rejected rather than stored as an empty strategy.
    /// </summary>
    private string UpdatePlayStrategyJson(string? posture, string? instructions, JsonElement? optionHints)
    {
        if (_strategyStore == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = AgentErrorEnvelope.ToPayload(
                    new ApiException(503, "unavailable", "The dual-layer strategy store is not bound on this instance."))
            }, JsonOptions);
        }

        var current = _strategyStore.Current;
        var hints = current.OptionHints;
        if (optionHints is { ValueKind: JsonValueKind.Object } hintsElement)
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in hintsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    parsed[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            hints = parsed;
        }

        if (posture == null && instructions == null && optionHints == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = AgentErrorEnvelope.ToPayload(
                    new ApiException(400, "invalid_request", "Provide at least one of posture, instructions, or option_hints."))
            }, JsonOptions);
        }

        _strategyStore.Update(new PlayStrategy
        {
            Posture = posture ?? current.Posture,
            Instructions = instructions ?? current.Instructions,
            OptionHints = hints,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Source = "mcp"
        });

        return JsonSerializer.Serialize(new
        {
            strategy = JsonSerializer.Deserialize<JsonElement>(_strategyStore.Current.ToJson())
        }, JsonOptionsKeepingNulls);
    }

    /// <summary>
    /// State, the legal actions, and the guidance for the screen, from one state read.
    /// </summary>
    /// <remarks>
    /// The documented loop is <c>get_game_state</c> -> <c>get_available_actions</c> -> <c>act</c>,
    /// plus a guidance read on the screens with a real choice: three or four calls per decision, each
    /// rebuilding the whole state on the game thread. These are the same three answers from one read,
    /// which is what a client that wants to minimise calls should reach for.
    /// </remarks>
    private async Task<string> DecideJsonAsync(CancellationToken cancellationToken)
    {
        var snapshotJson = await _bridge.GetActionSnapshotJsonAsync(cancellationToken);
        using var snapshot = JsonDocument.Parse(string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson);
        var state = ReadObjectOrEmpty(snapshot.RootElement, "state");
        return JsonSerializer.Serialize(new
        {
            state,
            available_actions = ReadArrayOrEmpty(snapshot.RootElement, "available_actions"),
            scene_guidance = BuildSceneGuidance(ScreenOfState(state))
        }, JsonOptionsKeepingNulls);
    }

    /// <summary>The one guidance object both guidance-shaped answers are built from.</summary>
    private static object BuildSceneGuidance(string? screen) => new
    {
        screen,
        scene = GameDataFilter.DetectScene(screen),
        guidance = PlayPrompt.ScreenGuidance(screen),
        // Never empty: a screen with no section of its own gets the index of the sections, so the
        // model is never told there is nothing to know about the screen it is looking at.
        playbook = PlayPrompt.PlaybookGuidance(screen)
    };

    private static string? ScreenOfState(JsonElement state) =>
        state.ValueKind == JsonValueKind.Object &&
        state.TryGetProperty("screen", out var screen) &&
        screen.ValueKind == JsonValueKind.String
            ? screen.GetString()
            : null;

    private async Task<string> WaitUntilActionableJsonAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(ReadTimeoutSeconds(arguments));
        var actionable = await _bridge.WaitUntilActionableAsync(timeout, cancellationToken);
        // The state half of this tool's answer is the compact agent_view by default, exactly as
        // `act` answers; raw_state asks for the full payload instead.
        var stateJson = ReadBool(arguments, "raw_state")
            ? await _bridge.GetRawStateJsonAsync(cancellationToken)
            : await _bridge.GetCompactStateJsonAsync(cancellationToken);
        var actionsJson = await _bridge.GetAvailableActionsJsonAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            actionable,
            timeout_seconds = timeout.TotalSeconds,
            state = DeserializeOrEmpty(stateJson),
            actions = DeserializeOrEmptyArray(actionsJson)
        }, JsonOptions);
    }

    private async Task<string> ExecuteActAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var action = ReadString(arguments, "action")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
        {
            return JsonSerializer.Serialize(new
            {
                error = AgentErrorEnvelope.ToPayload(
                    new ApiException(400, "invalid_request", "action is required"))
            }, JsonOptions);
        }

        // One state read for the legality check and for the validator: the descriptors and the
        // state's own available_actions come from the same walk, and now from the same frame, so a
        // screen that changes mid-request cannot make the two disagree.
        var snapshotJson = await _bridge.GetActionSnapshotJsonAsync(cancellationToken);
        using var snapshot = JsonDocument.Parse(string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson);
        var state = ReadObjectOrEmpty(snapshot.RootElement, "state");
        var descriptors = ReadArrayOrEmpty(snapshot.RootElement, "available_actions");
        var legal = ReadActionNames(state);
        if (!legal.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                error = AgentErrorEnvelope.ToPayload(new ApiException(
                    409,
                    "invalid_action",
                    "Action is not in available_actions.",
                    new { action, available_actions = legal })),
                action,
                available_actions = legal
            }, JsonOptions);
        }

        var cardIndex = ReadInt(arguments, "card_index");
        var targetIndex = ReadInt(arguments, "target_index");
        var optionIndex = ReadInt(arguments, "option_index");
        var x = ReadInt(arguments, "x");
        var y = ReadInt(arguments, "y");
        var tool = ReadString(arguments, "tool");
        // The state half of this tool's answer is the compact agent_view by default; raw_state asks
        // for the full post-action payload instead.
        var rawState = ReadBool(arguments, "raw_state");
        // Metadata never enters the game action itself; it is recorded only after acceptance.
        var reason = ReadString(arguments, "reason")?.Trim();
        var indexError = ActIndexValidator.Validate(
            action,
            cardIndex,
            targetIndex,
            optionIndex,
            descriptors.GetRawText(),
            state.GetRawText());
        if (indexError != null)
        {
            // The same error object the HTTP boundary sends, plus the echo this surface has always
            // returned: code, message, details (field / submitted / valid_indices / ...), retryable.
            return JsonSerializer.Serialize(new
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
        }

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
        _decisions?.Record(
            "native_mcp",
            action,
            string.IsNullOrWhiteSpace(reason) ? null : reason,
            // The act's own pre-action snapshot names the run, so this surface needs no runtime
            // singleton to attribute the decision to it.
            runId: RunIdOf(state.GetRawText()));
        if (!ActIndexValidator.IsUnsettled(result))
        {
            return result;
        }

        var settled = await _bridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), cancellationToken);
        var latest = await _bridge.GetCompactStateJsonAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            action,
            status = settled ? "completed" : "pending",
            stable = settled,
            previous = DeserializeOrEmpty(result),
            state = DeserializeOrEmpty(latest)
        }, JsonOptions);
    }

    /// <summary>
    /// The <c>run_id</c> of a <c>/state</c> payload, or null when it is absent or is the mod's
    /// "no run identified yet" placeholder. Never throws: attribution is worth less than the action.
    /// </summary>
    private static string? RunIdOf(string? stateJson)
    {
        var runId = ReadString(DeserializeOrEmpty(stateJson), "run_id");
        return string.IsNullOrWhiteSpace(runId) || runId == "run_unknown" ? null : runId;
    }

    /// <summary>
    /// A tool failure as MCP error content: the same <c>{code, message, details, retryable}</c>
    /// object the HTTP and Python surfaces answer with, never a bare message string.
    /// </summary>
    /// <remarks>
    /// This used to collapse every exception to <c>ex.Message</c>, which dropped the <c>code</c>,
    /// <c>retryable</c>, and <c>details</c> an HTTP caller keeps -- so the shared play contract's
    /// rule "retry only when <c>retryable</c> is true" was unimplementable on this surface: the
    /// field it names was simply absent.
    /// </remarks>
    private static object ToolError(object errorPayload)
    {
        return new
        {
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { error = errorPayload }, JsonOptions) } },
            isError = true
        };
    }

    private static JsonElement ReadArguments(JsonElement args)
    {
        if (!args.TryGetProperty("arguments", out var arguments) ||
            arguments.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return EmptyObject;
        }

        if (arguments.ValueKind == JsonValueKind.String)
        {
            var raw = arguments.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return EmptyObject;
            }

            using var document = JsonDocument.Parse(raw);
            // A stringified array or scalar is not an arguments object; treat it like a malformed
            // one (empty) rather than letting TryGetProperty throw on a non-object root downstream.
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : EmptyObject;
        }

        return arguments.ValueKind == JsonValueKind.Object ? arguments : EmptyObject;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
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

    private static JsonElement ReadObject(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? value : default;
    }

    /// <summary>An object member of a parsed snapshot, or an empty object when it is missing or wrong.</summary>
    private static JsonElement ReadObjectOrEmpty(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : EmptyObject;
    }

    private static JsonElement ReadArrayOrEmpty(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value
            : EmptyArray;
    }

    /// <summary>The action names a compact state reports, which is the walk the descriptors come from.</summary>
    private static IReadOnlyList<string> ReadActionNames(JsonElement state)
    {
        if (!state.TryGetProperty("available_actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var item in actions.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                names.Add(item.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
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

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        // A JSON string is how several clients send booleans; anything else stays false, which is
        // the documented default for every flag this surface reads.
        return value.ValueKind == JsonValueKind.String &&
               bool.TryParse(value.GetString(), out var parsed) &&
               parsed;
    }

    private static double ReadTimeoutSeconds(JsonElement element)
    {
        if (!element.TryGetProperty("timeout_seconds", out var value))
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

    private static JsonElement DeserializeOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EmptyObject;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json, JsonOptions);
        }
    }

    private static JsonElement DeserializeOrEmptyArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EmptyArray;
        }

        return DeserializeOrEmpty(json);
    }

    private static bool LooksLikeError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("error", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
