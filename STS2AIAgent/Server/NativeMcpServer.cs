using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Server;

internal sealed class McpHttpResult
{
    public int StatusCode { get; init; }

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    public string? Body { get; init; }

    public string? SessionId { get; init; }

    public string ProtocolVersion { get; init; } = NativeMcpServer.DefaultProtocolVersion;
}

internal sealed class NativeMcpServer
{
    public const string DefaultProtocolVersion = "2025-03-26";

    private static readonly string[] SupportedProtocols =
    {
        "2024-11-05",
        "2025-03-26",
        "2025-06-18"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    private static NativeMcpServer? _runtime;

    private readonly object _gate = new();
    private readonly IGameBridge _bridge;
    private readonly Func<object> _health;
    private readonly string _version;
    private bool _enabled;
    private string? _endpointUrl;
    private string? _sessionId;

    public NativeMcpServer(IGameBridge bridge, Func<object> health, string version)
    {
        _bridge = bridge;
        _health = health;
        _version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
    }

    public static NativeMcpServer? Runtime
    {
        get
        {
            return _runtime;
        }
    }

    public static NativeMcpServer BindRuntime(IGameBridge bridge, Func<object> health, string version)
    {
        var server = new NativeMcpServer(bridge, health, version);
        _runtime = server;
        return server;
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public string? EndpointUrl
    {
        get
        {
            lock (_gate)
            {
                return _enabled ? _endpointUrl : null;
            }
        }
    }

    public void SetEnabled(bool enabled, string endpointUrl)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _endpointUrl = NormalizeEndpoint(endpointUrl);
            if (!enabled)
            {
                _sessionId = null;
            }
        }
    }

    public string BuildClientConfigJson()
    {
        return FormatClientConfigJson(EndpointUrl);
    }

    public static string FormatClientConfigJson(string? url)
    {
        var endpoint = NormalizeEndpoint(url) ?? "http://127.0.0.1:8080/mcp";
        return
            "{\n" +
            "  \"mcpServers\": {\n" +
            "    \"sts2-ai-agent\": {\n" +
            "      \"type\": \"http\",\n" +
            "      \"url\": \"" + endpoint + "\"\n" +
            "    }\n" +
            "  }\n" +
            "}";
    }

    public async Task<int> HandleHttpAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        string? body = null;
        if (request.HasEntityBody)
        {
            if (request.ContentLength64 > 1_000_000)
            {
                var tooLarge = RestError(413, "payload_too_large", "MCP request body exceeds 1 MB.");
                await WriteHttpAsync(response, tooLarge);
                return tooLarge.StatusCode;
            }

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var result = await ProcessAsync(
            request.HttpMethod,
            request.Headers["Accept"],
            request.Headers["Mcp-Session-Id"] ?? request.Headers["MCP-Session-Id"],
            body,
            cancellationToken);
        await WriteHttpAsync(response, result);
        return result.StatusCode;
    }

    public async Task<McpHttpResult> ProcessAsync(
        string httpMethod,
        string? accept,
        string? sessionHeader,
        string? body,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return RestError(403, "mcp_disabled", "MCP is turned off. Enable it in the in-game overlay Connect tab.");
        }

        RememberSession(sessionHeader);
        var method = (httpMethod ?? "POST").Trim().ToUpperInvariant();
        if (method == "OPTIONS")
        {
            return new McpHttpResult { StatusCode = 204, SessionId = CurrentSession() };
        }

        if (method == "GET")
        {
            return RestError(405, "method_not_allowed", "MCP uses Streamable HTTP. POST JSON-RPC to this URL.");
        }

        if (method == "DELETE")
        {
            lock (_gate)
            {
                _sessionId = null;
            }

            return JsonResult(200, new { ok = true });
        }

        if (method != "POST")
        {
            return RestError(405, "method_not_allowed", "Use POST JSON-RPC.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return RpcHttp(400, RpcError(null, -32700, "Parse error: empty body"), accept);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return RpcHttp(400, RpcError(null, -32700, "Parse error: " + ex.Message), accept);
        }

        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var batch = new List<object>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var outcome = await HandleMessageAsync(item, cancellationToken);
                    if (outcome != null)
                    {
                        batch.Add(outcome);
                    }
                }

                if (batch.Count == 0)
                {
                    return new McpHttpResult { StatusCode = 202, SessionId = CurrentSession() };
                }

                return RpcHttp(200, batch, accept, CurrentSession());
            }

            var single = await HandleMessageAsync(document.RootElement, cancellationToken);
            if (single == null)
            {
                return new McpHttpResult { StatusCode = 202, SessionId = CurrentSession() };
            }

            return RpcHttp(200, single, accept, CurrentSession());
        }
    }

    private async Task<object?> HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return RpcError(null, -32600, "Invalid Request.");
        }

        JsonElement? id = null;
        var hasId = message.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        if (hasId)
        {
            id = idElement.Clone();
        }

        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return hasId ? RpcError(id, -32600, "Invalid Request: method is required.") : null;
        }

        var method = methodElement.GetString() ?? string.Empty;
        var args = message.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object
            ? paramsElement
            : EmptyObject;

        try
        {
            switch (method)
            {
                case "initialize":
                    return new
                    {
                        jsonrpc = "2.0",
                        id,
                        result = Initialize(args)
                    };
                case "notifications/initialized":
                case "notifications/cancelled":
                    return hasId
                        ? new { jsonrpc = "2.0", id, result = new { } }
                        : null;
                case "ping":
                    return new { jsonrpc = "2.0", id, result = new { } };
                case "tools/list":
                    return new
                    {
                        jsonrpc = "2.0",
                        id,
                        result = new { tools = ListTools() }
                    };
                case "tools/call":
                    return new
                    {
                        jsonrpc = "2.0",
                        id,
                        result = await CallToolAsync(args, cancellationToken)
                    };
                case "resources/list":
                    return new { jsonrpc = "2.0", id, result = new { resources = Array.Empty<object>() } };
                case "prompts/list":
                    return new { jsonrpc = "2.0", id, result = new { prompts = Array.Empty<object>() } };
                case "logging/setLevel":
                    return new { jsonrpc = "2.0", id, result = new { } };
                default:
                    if (method.StartsWith("notifications/", StringComparison.Ordinal))
                    {
                        return hasId ? new { jsonrpc = "2.0", id, result = new { } } : null;
                    }

                    return hasId ? RpcError(id, -32601, "Method not found: " + method) : null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return hasId ? RpcError(id, -32603, ex.Message) : null;
        }
    }

    private object Initialize(JsonElement args)
    {
        var requested = ReadString(args, "protocolVersion");
        var protocol = !string.IsNullOrWhiteSpace(requested) &&
                       SupportedProtocols.Contains(requested, StringComparer.Ordinal)
            ? requested
            : DefaultProtocolVersion;
        lock (_gate)
        {
            _sessionId = Guid.NewGuid().ToString("N");
        }

        return new
        {
            protocolVersion = protocol,
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "sts2-ai-agent",
                version = _version,
                title = "STS2 AI Agent"
            },
            instructions = "Local Slay the Spire 2 mod MCP. Call get_game_state before acting. Recompute indexes from the latest state. Do not guess card_index or option_index."
        };
    }

    private static object[] ListTools()
    {
        return AgentTools.Mcp.Select(static tool => (object)new
        {
            name = tool.Name,
            description = tool.Description,
            inputSchema = tool.Parameters
        }).ToArray();
    }

    private async Task<object> CallToolAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var name = ReadString(args, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ToolError("Tool name is required.");
        }

        var arguments = ReadArguments(args);
        try
        {
            var text = await ExecuteToolAsync(name, arguments, cancellationToken);
            return new
            {
                content = new[] { new { type = "text", text } },
                isError = LooksLikeError(text)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolError(ex.Message);
        }
    }

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
            default:
                return JsonSerializer.Serialize(new { error = "Unknown tool '" + name + "'" }, JsonOptions);
        }
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

    private static object RpcError(object? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        };
    }

    private McpHttpResult RestError(int status, string code, string message)
    {
        return JsonResult(status, new
        {
            ok = false,
            error = new { code, message }
        });
    }

    private McpHttpResult JsonResult(int status, object payload)
    {
        return new McpHttpResult
        {
            StatusCode = status,
            Body = JsonSerializer.Serialize(payload, JsonOptions),
            SessionId = CurrentSession()
        };
    }

    private McpHttpResult RpcHttp(int status, object payload, string? accept, string? sessionId = null)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var sseOnly = WantsSseOnly(accept);
        return new McpHttpResult
        {
            StatusCode = status,
            ContentType = sseOnly ? "text/event-stream" : "application/json; charset=utf-8",
            Body = sseOnly ? "event: message\ndata: " + json + "\n\n" : json,
            SessionId = sessionId ?? CurrentSession()
        };
    }

    private static async Task WriteHttpAsync(HttpListenerResponse response, McpHttpResult result)
    {
        response.StatusCode = result.StatusCode;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, MCP-Protocol-Version, Mcp-Session-Id, Last-Event-ID";
        response.Headers["Access-Control-Expose-Headers"] = "MCP-Protocol-Version, Mcp-Session-Id";
        response.Headers["MCP-Protocol-Version"] = result.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(result.SessionId))
        {
            response.Headers["Mcp-Session-Id"] = result.SessionId;
        }

        if (result.StatusCode == 204 || result.Body == null)
        {
            response.ContentLength64 = 0;
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(result.Body);
        response.ContentType = result.ContentType;
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.LongLength;
        await response.OutputStream.WriteAsync(bytes);
    }

    private void RememberSession(string? sessionHeader)
    {
        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            return;
        }

        lock (_gate)
        {
            _sessionId ??= sessionHeader.Trim();
        }
    }

    private string? CurrentSession()
    {
        lock (_gate)
        {
            return _sessionId;
        }
    }

    private static string? NormalizeEndpoint(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return null;
        }

        return endpointUrl.Trim().TrimEnd('/');
    }

    private static bool WantsSseOnly(string? accept)
    {
        if (string.IsNullOrWhiteSpace(accept))
        {
            return false;
        }

        var hasSse = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        var hasJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        return hasSse && !hasJson;
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
