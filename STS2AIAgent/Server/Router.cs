using System.Net;
using System.Diagnostics;
using System.Text;
using System.Threading;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Agent;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Server;

internal static class Router
{
    private const string ServiceName = "sts2-ai-agent";
    private const string ProtocolVersion = "2026-03-11-v1";
    internal const string ModVersion = "0.10.2";
    private const string LogPrefix = "[STS2AIAgent.Router]";

    private static long _requestCounter;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> RecentIds = new();

    internal static string[] RecentRequestIds() => RecentIds.ToArray();

    private static void NoteRequestId(string requestId)
    {
        RecentIds.Enqueue(requestId);
        while (RecentIds.Count > 16 && RecentIds.TryDequeue(out _))
        {
        }
    }

    public static async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var seq = Interlocked.Increment(ref _requestCounter);
        var requestId = $"req_{DateTime.UtcNow:yyyyMMdd_HHmmss_ffff}_{seq}";
        NoteRequestId(requestId);
        var request = context.Request;
        var response = context.Response;
        var stopwatch = Stopwatch.StartNew();
        var statusCode = 500;

        try
        {
            Log.Info($"{LogPrefix} {requestId} {request.HttpMethod} {request.Url?.AbsolutePath}");

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath is "/companion/message" or "/companion/control")
            {
                if (!InstanceRole.IsCompanion || !request.IsLocal || !CompanionConnection.IsAuthorized(
                    Environment.GetEnvironmentVariable(CompanionConnection.TokenEnvironment), request.Headers[CompanionConnection.TokenHeader]))
                    throw new ApiException(403, "companion_session_required", "This endpoint requires the active local companion session.");
                if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
                    throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
                using var body = await ReadCompanionBodyAsync(request, cancellationToken);
                if (request.Url.AbsolutePath == "/companion/control")
                {
                    if (body.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                        !body.RootElement.TryGetProperty("running", out var running) ||
                        running.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                        throw new ApiException(400, "invalid_request", "running must be a boolean.");
                    string phase;
                    try { phase = await AgentRuntime.Instance.SetCompanionRunningAsync(running.GetBoolean(), cancellationToken); }
                    catch (TimeoutException) { throw new ApiException(409, "pause_pending", "Pause requested but the current task has not finished yet."); }
                    catch (InvalidOperationException ex) { throw new ApiException(409, "companion_not_ready", ex.Message); }
                    await WriteJsonAsync(response, 200, new { ok = true, request_id = requestId, data = new { phase } });
                    statusCode = 200;
                    return;
                }
                if (body.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                    !body.RootElement.TryGetProperty("message", out var message) || message.ValueKind != System.Text.Json.JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(message.GetString()) || message.GetString()!.Length > TeamConversation.MaxMessageLength)
                    throw new ApiException(400, "invalid_request", "message must contain 1–2000 characters.");
                var reply = await AgentRuntime.Instance.ReplyToTeammateAsync(message.GetString()!, cancellationToken);
                await WriteJsonAsync(response, 200, new { ok = true, request_id = requestId, data = new { reply } });
                statusCode = 200;
                return;
            }

            if (IsMcpPath(request.Url?.AbsolutePath))
            {
                var mcp = NativeMcpServer.Runtime;
                if (mcp == null || !mcp.Enabled)
                {
                    statusCode = 403;
                    await WriteErrorAsync(response, 403, "mcp_disabled", "MCP is turned off. Enable it in the in-game overlay Connect tab.", requestId);
                    return;
                }

                statusCode = await mcp.HandleHttpAsync(context, cancellationToken);
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/health")
            {
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = BuildHealthData()
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/state")
            {
                var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = state
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/actions/available")
            {
                var payload = await GameThread.InvokeAsync(GameStateService.BuildAvailableActionsPayload);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = payload
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath is string dataPath &&
                dataPath.StartsWith("/data/", StringComparison.OrdinalIgnoreCase))
            {
                var collectionPath = dataPath.Substring("/data/".Length);

                try
                {
                    var data = await GameThread.InvokeAsync(() => GameDataExportService.ExportCollection(collectionPath));
                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        request_id = requestId,
                        data = data
                    });
                    statusCode = 200;
                    return;
                }
                catch (KeyNotFoundException)
                {
                    statusCode = 404;
                    await WriteErrorAsync(response, 404, "collection_not_found", $"Unknown data collection: {collectionPath}", requestId);
                    return;
                }
                catch (Exception ex)
                {
                    statusCode = 500;
                    await WriteErrorAsync(response, 500, "export_error", $"Failed to export {collectionPath}: {ex.Message}", requestId);
                    return;
                }
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/events/stream")
            {
                statusCode = await HandleEventStreamAsync(response, cancellationToken);
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/action")
            {
                var actionRequest = await JsonHelper.DeserializeAsync<ActionRequest>(request.InputStream, cancellationToken);
                if (actionRequest?.action == null)
                {
                    throw new ApiException(400, "invalid_request", "Request body must contain an action field.");
                }

                var actionResponse = await GameThread.InvokeAsync(() => GameActionService.ExecuteAsync(actionRequest));
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = actionResponse
                });
                statusCode = 200;
                return;
            }

            statusCode = 404;
            await WriteErrorAsync(response, statusCode, "not_found", "Route not found.", requestId);
        }
        catch (ApiException ex)
        {
            statusCode = ex.StatusCode;
            await WriteErrorAsync(response, ex.StatusCode, ex.Code, ex.Message, requestId, ex.Details, ex.Retryable);
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} {requestId} Failed: {ex}");
            statusCode = 500;
            await WriteErrorAsync(response, statusCode, "internal_error", "Unhandled server error.", requestId);
        }
        finally
        {
            Log.Info($"{LogPrefix} {requestId} Completed {statusCode} in {stopwatch.ElapsedMilliseconds}ms");
            response.Close();
        }
    }

    internal static object BuildHealthData()
    {
        var mcp = NativeMcpServer.Runtime;
        return new
        {
            service = ServiceName,
            mod_version = ModVersion,
            protocol_version = ProtocolVersion,
            game_version = ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "unknown",
            status = "ready",
            api_host = HttpServer.Instance.Host,
            api_port = HttpServer.Instance.Port,
            process_id = Environment.ProcessId,
            instance_role = InstanceRole.Current,
            mcp_enabled = mcp?.Enabled == true,
            mcp_url = mcp?.EndpointUrl
        };
    }

    private static bool IsMcpPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/mcp/", StringComparison.OrdinalIgnoreCase);
    }

    public static Task WriteErrorAsync(
        HttpListenerResponse response,
        int statusCode,
        string code,
        string message,
        string? requestId = null,
        object? details = null,
        bool retryable = false)
    {
        return WriteJsonAsync(response, statusCode, new
        {
            ok = false,
            request_id = requestId ?? $"req_{DateTime.UtcNow:yyyyMMdd_HHmmss_ffff}_{Interlocked.Increment(ref _requestCounter)}",
            error = new
            {
                code,
                message,
                details,
                retryable
            }
        });
    }

    private static async Task<System.Text.Json.JsonDocument> ReadCompanionBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        try { return await System.Text.Json.JsonDocument.ParseAsync(request.InputStream, cancellationToken: cancellationToken); }
        catch (System.Text.Json.JsonException) { throw new ApiException(400, "invalid_request", "Body must be valid JSON."); }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = JsonHelper.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.LongLength;

        await response.OutputStream.WriteAsync(bytes);
    }

    private static async Task<int> HandleEventStreamAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.ContentEncoding = Encoding.UTF8;
        response.SendChunked = true;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = GameEventService.Instance.Subscribe();

        try
        {
            await WriteSseCommentAsync(response, "stream opened");

            while (!cancellationToken.IsCancellationRequested)
            {
                var waitForEvent = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                var completedTask = await Task.WhenAny(waitForEvent, heartbeat);

                if (completedTask == heartbeat)
                {
                    await WriteSseCommentAsync(response, "heartbeat");
                    continue;
                }

                if (!await waitForEvent)
                {
                    break;
                }

                while (subscription.Reader.TryRead(out var envelope))
                {
                    await WriteSseEventAsync(response, envelope);
                }
            }

            return 200;
        }
        catch (OperationCanceledException)
        {
            return 200;
        }
        catch (HttpListenerException)
        {
            // Client disconnected.
            return 200;
        }
        catch (IOException)
        {
            // Client disconnected.
            return 200;
        }
        catch (ObjectDisposedException)
        {
            // Response stream is already closed.
            return 200;
        }
    }

    private static async Task WriteSseEventAsync(HttpListenerResponse response, GameEventEnvelope envelope)
    {
        await WriteSseRawAsync(response, $"id: {envelope.event_id}\n");
        await WriteSseRawAsync(response, $"event: {envelope.type}\n");

        var json = JsonHelper.Serialize(envelope);
        foreach (var line in json.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            await WriteSseRawAsync(response, $"data: {line}\n");
        }

        await WriteSseRawAsync(response, "\n");
        await response.OutputStream.FlushAsync();
    }

    private static async Task WriteSseCommentAsync(HttpListenerResponse response, string comment)
    {
        await WriteSseRawAsync(response, $": {comment}\n\n");
        await response.OutputStream.FlushAsync();
    }

    private static ValueTask WriteSseRawAsync(HttpListenerResponse response, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return response.OutputStream.WriteAsync(bytes);
    }
}
