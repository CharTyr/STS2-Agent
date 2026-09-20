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
            case "get_decision_log":
                // The key is read inline so test_native_tool_alignment can see it: a delegated
                // reader would be invisible to the argument-name comparison.
                return _decisions?.RenderJson(Math.Clamp(ReadInt(arguments, "limit") ?? 50, 1, 200))
                    ?? """{"decisions":[]}""";
            case "get_run_summary":
                return await GetRunSummaryJsonAsync(cancellationToken);
            case "diff_state":
                // The three keys are named here rather than inside the helper so the argument-name
                // comparison in test_native_tool_alignment can see them.
                return JsonSerializer.Serialize(
                    StateViews.BuildStateDiff(
                        ReadObject(arguments, "before"),
                        ReadObject(arguments, "after"),
                        Math.Clamp(ReadInt(arguments, "limit") ?? StateViews.MaxDiffEntries, 1, 200)),
                    JsonOptions);
            default:
                return JsonSerializer.Serialize(new { error = "Unknown tool '" + name + "'" }, JsonOptions);
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

    private async Task<string> WaitUntilActionableJsonAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(ReadTimeoutSeconds(arguments));
        var actionable = await _bridge.WaitUntilActionableAsync(timeout, cancellationToken);
        var stateJson = await _bridge.GetCompactStateJsonAsync(cancellationToken);
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
            return JsonSerializer.Serialize(new { error = "action is required" }, JsonOptions);
        }

        var legal = await _bridge.GetAvailableActionNamesAsync(cancellationToken);
        if (!legal.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                error = "Action is not in available_actions.",
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
        // Metadata never enters the game action itself; it is recorded only after acceptance.
        var reason = ReadString(arguments, "reason")?.Trim();
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
            return JsonSerializer.Serialize(new
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
        }

        var result = await _bridge.ActAsync(
            action,
            cardIndex,
            targetIndex,
            optionIndex,
            x,
            y,
            tool,
            cancellationToken);
        _decisions?.Record("native_mcp", action, string.IsNullOrWhiteSpace(reason) ? null : reason);
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

    private static object ToolError(string message)
    {
        return new
        {
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { error = message }, JsonOptions) } },
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
            return document.RootElement.Clone();
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
