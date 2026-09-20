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

    public string? AllowOrigin { get; init; }
}

internal sealed partial class NativeMcpServer
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

    /// <summary>
    /// Keeps an explicit null in the payload. <see cref="JsonOptions"/> drops null properties, which
    /// is right for the optional fields of most tools but wrong for a field whose absence is itself
    /// an answer: <c>get_run_summary</c> answers <c>{"run": null}</c> when the payload carries no run,
    /// and the Python surface does the same, so dropping the key would make the two surfaces disagree
    /// about whether the field exists at all.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptionsKeepingNulls = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    private static NativeMcpServer? _runtime;

    private readonly object _gate = new();
    private readonly IGameBridge _bridge;
    private readonly Func<object> _health;
    private readonly DecisionLog? _decisions;
    private readonly string _version;
    private bool _enabled;
    private string? _endpointUrl;
    private string? _sessionId;

    public NativeMcpServer(
        IGameBridge bridge,
        Func<object> health,
        string version,
        DecisionLog? decisions = null)
    {
        _bridge = bridge;
        _health = health;
        _decisions = decisions;
        _version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
    }

    public static NativeMcpServer? Runtime
    {
        get
        {
            return _runtime;
        }
    }

    public static NativeMcpServer BindRuntime(
        IGameBridge bridge,
        Func<object> health,
        string version,
        DecisionLog? decisions = null)
    {
        var server = new NativeMcpServer(bridge, health, version, decisions);
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
            cancellationToken,
            request.Headers["Origin"],
            request.Headers["Host"]);
        await WriteHttpAsync(response, result);
        return result.StatusCode;
    }

    public async Task<McpHttpResult> ProcessAsync(
        string httpMethod,
        string? accept,
        string? sessionHeader,
        string? body,
        CancellationToken cancellationToken,
        string? origin = null,
        string? host = null)
    {
        if (!Enabled)
        {
            return RestError(403, "mcp_disabled", "MCP is turned off. Enable it in the in-game overlay Connect tab.");
        }

        if (!TryAuthorizeOrigin(origin, host, EndpointUrl, out var allowOrigin))
        {
            return RestError(403, "origin_not_allowed", "Untrusted Origin is not allowed.");
        }

        var result = await ProcessAuthorizedAsync(httpMethod, accept, sessionHeader, body, cancellationToken);
        return StampAllowOrigin(result, allowOrigin);
    }

    private async Task<McpHttpResult> ProcessAuthorizedAsync(
        string httpMethod,
        string? accept,
        string? sessionHeader,
        string? body,
        CancellationToken cancellationToken)
    {
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
                    return new { jsonrpc = "2.0", id, result = new { resources = ListSkillResources() } };
                case "resources/read":
                    return ReadSkillResource(id, args);
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
                tools = new { listChanged = false },
                resources = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "sts2-ai-agent",
                version = _version,
                title = "STS2 AI Agent"
            },
            instructions = PlayPrompt.PlaySystem
        };
    }

    private static object[] ListSkillResources()
    {
        return PlayPrompt.SkillResources.Select(static resource => (object)new
        {
            uri = resource.Uri,
            name = resource.Name,
            description = resource.Description,
            mimeType = "text/markdown"
        }).ToArray();
    }

    private static object ReadSkillResource(object? id, JsonElement args)
    {
        var uri = ReadString(args, "uri")?.Trim();
        var resource = PlayPrompt.SkillResources.FirstOrDefault(item => string.Equals(item.Uri, uri, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(resource.Uri))
        {
            return RpcError(id, -32002, "Resource not found: " + uri);
        }

        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                contents = new[]
                {
                    new { uri = resource.Uri, mimeType = "text/markdown", text = resource.Text }
                }
            }
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
        if (!string.IsNullOrWhiteSpace(result.AllowOrigin))
        {
            response.Headers["Access-Control-Allow-Origin"] = result.AllowOrigin;
            response.Headers["Vary"] = "Origin";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, MCP-Protocol-Version, Mcp-Session-Id, Last-Event-ID";
            response.Headers["Access-Control-Expose-Headers"] = "MCP-Protocol-Version, Mcp-Session-Id";
        }
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

    private static McpHttpResult StampAllowOrigin(McpHttpResult result, string? allowOrigin)
    {
        if (string.IsNullOrWhiteSpace(allowOrigin))
        {
            return result;
        }

        return new McpHttpResult
        {
            StatusCode = result.StatusCode,
            ContentType = result.ContentType,
            Body = result.Body,
            SessionId = result.SessionId,
            ProtocolVersion = result.ProtocolVersion,
            AllowOrigin = allowOrigin
        };
    }

    // Native clients omit Origin and stay compatible. A present Origin is allowed
    // only when it matches the configured MCP endpoint authority. The request Host
    // is never an allow-list; if present it must also match that same authority.
    internal static bool TryAuthorizeOrigin(string? origin, string? host, string? endpointUrl, out string? allowOrigin)
    {
        allowOrigin = null;
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!TryParseSingleOrigin(origin, out var originUri) ||
            !TryGetEndpointAuthority(endpointUrl, out var endpoint) ||
            !OriginMatchesEndpoint(originUri, endpoint))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(host) && !HostMatchesEndpoint(host, endpoint))
        {
            return false;
        }

        allowOrigin = originUri.GetLeftPart(UriPartial.Authority);
        return allowOrigin.Length > 0;
    }

    private static bool IsHttpScheme(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSingleOrigin(string origin, out Uri originUri)
    {
        originUri = null!;
        var trimmed = origin.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (ch is ',' or ' ' or '\t' or '\r' or '\n')
            {
                return false;
            }
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !IsHttpScheme(uri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath is not ("/" or "") ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        originUri = uri;
        return true;
    }

    private static bool TryGetEndpointAuthority(string? endpointUrl, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri) || !IsHttpScheme(uri))
        {
            return false;
        }

        endpoint = uri;
        return true;
    }

    private static bool OriginMatchesEndpoint(Uri origin, Uri endpoint)
    {
        return origin.Scheme.Equals(endpoint.Scheme, StringComparison.OrdinalIgnoreCase) &&
               HostEquals(origin.Host, endpoint.Host) &&
               origin.Port == endpoint.Port;
    }

    private static bool HostMatchesEndpoint(string hostHeader, Uri endpoint)
    {
        return TryParseHostHeader(hostHeader, out var host, out var port) &&
               HostEquals(host, endpoint.Host) &&
               port == endpoint.Port;
    }

    private static bool TryParseHostHeader(string hostHeader, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return false;
        }

        if (!Uri.TryCreate("http://" + hostHeader.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port;
        return port > 0;
    }

    private static bool HostEquals(string left, string right)
    {
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
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
}

