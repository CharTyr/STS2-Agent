using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    internal const string ModVersion = "0.16.2";
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

            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/companion/jev")
            {
                if (!InstanceRole.IsCompanion || !request.IsLocal || !CompanionConnection.IsAuthorized(
                    Environment.GetEnvironmentVariable(CompanionConnection.TokenEnvironment), request.Headers[CompanionConnection.TokenHeader]))
                    throw new ApiException(403, "companion_session_required", "This endpoint requires the active local companion session.");
                await WriteJsonAsync(response, 200, new
                {
                    ok = true, request_id = requestId, data = AgentRuntime.Instance.CompanionJevStatus()
                });
                statusCode = 200;
                return;
            }

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
                    if (body.RootElement.ValueKind != JsonValueKind.Object)
                        throw new ApiException(400, "invalid_request", "A control object is required.");
                    if (body.RootElement.TryGetProperty("settings", out var patchElement))
                    {
                        // Settings-only patch: never serialize the secret-bearing input into a
                        // response, diagnostic or exception message. Legacy running requests follow
                        // their original path below, and mixed bodies are refused rather than guessed.
                        if (body.RootElement.TryGetProperty("running", out _)
                            || patchElement.ValueKind != JsonValueKind.Object)
                            throw new ApiException(400, "invalid_request", "Use either settings or running, not both.");
                        var patch = patchElement.Deserialize<CompanionSettingsPatch>();
                        if (patch == null || !patch.IsValid)
                            throw new ApiException(400, "invalid_request", "Invalid companion Jev settings.");
                        bool enabled;
                        try { enabled = AgentRuntime.Instance.ApplyCompanionSettings(patch); }
                        catch (ArgumentException) { throw new ApiException(400, "invalid_request", "Invalid companion Jev settings."); }
                        catch (InvalidOperationException) { throw new ApiException(409, "companion_not_ready", "Companion settings update unavailable."); }
                        await WriteJsonAsync(response, 200, new
                        {
                            ok = true, request_id = requestId,
                            data = new { phase = AgentRuntime.Instance.PlayPhase,
                                settings_applied = true, dual_layer_coop_enabled = enabled }
                        });
                        statusCode = 200;
                        return;
                    }
                    if (!body.RootElement.TryGetProperty("running", out var running) ||
                        running.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
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
                // The typed signal is optional and additive: a client that sends only text keeps
                // working, and a malformed signal is refused rather than dropped, because a dropped
                // instruction looks like a teammate ignoring it.
                TeamIntent? intent;
                try
                {
                    intent = TeamIntent.Parse(body.RootElement);
                }
                catch (ArgumentException ex)
                {
                    throw new ApiException(400, "invalid_request", ex.Message);
                }

                var reply = await AgentRuntime.Instance.ReplyToTeammateAsync(message.GetString()!, intent, cancellationToken);
                await WriteJsonAsync(response, 200, new { ok = true, request_id = requestId, data = new { reply } });
                statusCode = 200;
                return;
            }


            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/session/control")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "Session control is only available on loopback.");
                }

                if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
                {
                    throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
                }

                var control = await ReadJsonBodyAsync<SessionControlRequest>(request, cancellationToken);
                if (control?.running is null)
                {
                    throw new ApiException(400, "invalid_request", "running must be a boolean.");
                }

                string phase;
                try
                {
                    if (InstanceRole.IsCompanion)
                    {
                        phase = await AgentRuntime.Instance.SetCompanionRunningAsync(control.running.Value, cancellationToken);
                    }
                    else if (control.running.Value)
                    {
                        AgentRuntime.Instance.StartAutoPlay();
                        phase = AgentRuntime.Instance.PlayPhase;
                    }
                    else
                    {
                        AgentRuntime.Instance.StopAutoPlay();
                        phase = AgentRuntime.Instance.PlayPhase;
                    }
                }
                catch (TimeoutException)
                {
                    throw new ApiException(409, "pause_pending", "Pause requested but the current task has not finished yet.");
                }
                catch (InvalidOperationException ex)
                {
                    throw new ApiException(409, "session_not_ready", ex.Message);
                }

                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = new
                    {
                        phase,
                        play_running = AgentRuntime.Instance.PlayRunning,
                        play_phase = AgentRuntime.Instance.PlayPhase
                    }
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/mcp/control")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "MCP control is only available on loopback.");
                }

                if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
                {
                    throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
                }

                var mcpControl = await ReadJsonBodyAsync<SessionControlRequest>(request, cancellationToken);
                if (mcpControl?.running is null)
                {
                    throw new ApiException(400, "invalid_request", "running must be a boolean.");
                }

                AgentRuntime.Instance.SetMcpEnabled(mcpControl.running.Value);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = new
                    {
                        mcp_enabled = AgentRuntime.Instance.McpRunning
                    }
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/teammate/control")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "Teammate control is only available on loopback.");
                }

                if (InstanceRole.IsCompanion)
                {
                    throw new ApiException(409, "not_host", "Only the host window can control the AI teammate.");
                }

                if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
                {
                    throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
                }

                var teammateControl = await ReadJsonBodyAsync<SessionControlRequest>(request, cancellationToken);
                if (teammateControl?.running is null)
                {
                    throw new ApiException(400, "invalid_request", "running must be a boolean.");
                }

                var control = await AgentRuntime.Instance.ControlTeammateResultAsync(
                    teammateControl.running.Value, cancellationToken);
                if (!control.Ok)
                {
                    // Retryable on purpose: the usual causes are a launch still in progress, a
                    // previous control that has not finished, or a pause the teammate has not
                    // confirmed yet -- all of which clear on their own.
                    throw new ApiException(409, "teammate_control_failed", control.Message, retryable: true);
                }

                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = new
                    {
                        phase = control.Phase,
                        play_running = AgentRuntime.Instance.PlayRunning,
                        play_phase = AgentRuntime.Instance.PlayPhase,
                        companion_auto_play = AgentRuntime.Instance.CompanionAutoPlay
                    }
                });
                statusCode = 200;
                return;
            }

            // The redacted diagnostics export, for debugging without opening the overlay: the same
            // text the 导出诊断 button copies (no API keys, Authorization headers, session tokens,
            // or chat bodies). Local-only, like the other control routes.
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/diagnostics")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "Diagnostics are only available on loopback.");
                }

                var text = AgentRuntime.Instance.ExportDiagnostics();
                var bytes = Encoding.UTF8.GetBytes(text);
                response.StatusCode = 200;
                response.ContentType = "text/plain; charset=utf-8";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = bytes.LongLength;
                await response.OutputStream.WriteAsync(bytes, cancellationToken);
                statusCode = 200;
                return;
            }

            // The dual-layer strategy surface. An external planner reads the current strategy and a
            // briefing here, and writes a new one back; the in-game planner uses the same store, so the
            // two paths steer the same Jev decider. Local-only, like the other control routes.
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/strategy")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "The play strategy is only available on loopback.");
                }

                var runtime = AgentRuntime.Instance;
                var rawState = await GameThread.InvokeAsync(() =>
                {
                    var state = GameStateService.BuildStatePayload();
                    runtime.ObserveSessionStateSnapshot(state.run_id, state.screen, state.session.phase);
                    return JsonSerializer.Serialize(state);
                }, cancellationToken);
                var current = runtime.CurrentStrategy;
                var status = runtime.DualLayerStatus();
                var briefing = PlannerBriefingProjection.Build(rawState, current, status.DualLayer,
                    status.JevConfigured, runtime.DecisionLogForBriefing);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = briefing
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/strategy")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "The play strategy is only available on loopback.");
                }

                if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
                {
                    throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
                }

                var body = await ReadJsonBodyAsync<JsonElement>(request, cancellationToken);
                var strategyElement = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("strategy", out var nested)
                    ? nested
                    : body;
                // A partial update: omitted fields keep their current values, as documented. TryParse
                // would fill them with defaults and wipe the standing plan on a posture-only nudge.
                var update = PlayStrategyUpdate.TryRead(strategyElement);
                if (update == null)
                {
                    throw new ApiException(400, "invalid_request",
                        "strategy must be a JSON object with at least one of posture/goal/instructions/option_hints.");
                }

                AgentRuntime.Instance.UpdatePlayStrategy(update);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = new
                    {
                        strategy = JsonSerializer.Deserialize<JsonElement>(AgentRuntime.Instance.CurrentStrategy.ToJson())
                    }
                });
                statusCode = 200;
                return;
            }

            if (IsMcpPath(request.Url?.AbsolutePath))
            {
                var mcp = NativeMcpServer.Runtime;
                if (mcp == null || !mcp.Enabled)
                {
                    statusCode = 403;
                    await WriteErrorAsync(response, 403, "mcp_disabled", "MCP is turned off. Enable it in the in-game overlay Settings page, under Connect.", requestId);
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
                request.Url?.AbsolutePath == "/vision/screenshot")
            {
                if (!request.IsLocal)
                {
                    throw new ApiException(403, "local_only", "Screenshots are only available on loopback.");
                }

                // Through the bridge's capture rather than a direct viewport grab: the bridge hides
                // the overlay for one frame first, so an external client sees the game rather than
                // the mod's own panel drawn over it.
                var jpeg = await new GameBridge().CaptureScreenshotJpegAsync(cancellationToken);
                if (jpeg == null || jpeg.Length == 0)
                {
                    throw new ApiException(409, "screenshot_unavailable", "The game viewport did not produce a screenshot.");
                }

                response.StatusCode = 200;
                response.ContentType = "image/jpeg";
                response.ContentLength64 = jpeg.Length;
                await response.OutputStream.WriteAsync(jpeg, cancellationToken);
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/state")
            {
                var state = await GameThread.InvokeAsync(() =>
                {
                    var current = GameStateService.BuildStatePayload();
                    // Confirm on the game thread, before another request can observe a newer run.
                    AgentRuntime.Instance.ObserveSessionStateSnapshot(current.run_id, current.screen, current.session.phase);
                    return current;
                });
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
                request.Url?.AbsolutePath == "/decision-snapshot")
            {
                // One game-thread turn and one state build: the compact state and the action
                // descriptors come from the same action-surface enumeration, so a caller that would
                // otherwise read /state and then /actions/available cannot be handed two frames.
                // Both endpoints stay; this is the one to read when both halves are needed.
                var snapshot = await GameThread.InvokeAsync(GameStateService.BuildDecisionSnapshotPayload);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = snapshot
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/decisions")
            {
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = AgentRuntime.Instance.RecentDecisions(ReadDecisionLimit(request))
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
                RequireBoundedBody(request);
                var actionRequest = await ReadJsonBodyAsync<ActionRequest>(request, cancellationToken);
                if (actionRequest?.action == null)
                {
                    throw new ApiException(400, "invalid_request", "Request body must contain an action field.");
                }

                // Capture the run before an accepted action changes screens or ends the run. The
                // identity and action share one game-thread work unit; neither is inferred from the
                // autoplay boundary or from the action's post-transition state.
                var (actionRunId, actionResponse) = await GameThread.InvokeAsync(async () =>
                {
                    var before = GameStateService.BuildStatePayload();
                    AgentRuntime.Instance.ObserveSessionStateSnapshot(before.run_id, before.screen, before.session.phase);
                    var result = await GameActionService.ExecuteAsync(actionRequest);
                    return (before.run_id, result);
                });
                var decisionReason = DecisionContext.ReadReason(actionRequest.client_context);
                AgentRuntime.Instance.RecordDecision(
                    "http_api",
                    actionRequest.action,
                    decisionReason,
                    runId: actionRunId, runIdObserved: true);
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
        var dualLaunchOutcome = AgentRuntime.Instance.DualLaunchOutcome;
        var roleData = InstanceRole.IsCompanion
            ? HealthRoleData.NotApplicable
            : HealthRoleData.ForHost(
                LocalDualInstanceLauncher.CompanionProcessAlive,
                LocalDualInstanceLauncher.CompanionProcessExited,
                BuildCompanionSessionData(),
                AgentRuntime.Instance.DualStatus,
                dualLaunchOutcome == DualLaunchOutcome.Idle ? null : dualLaunchOutcome.ToString(),
                AgentRuntime.Instance.TeamControlStatus);
        return new
        {
            service = ServiceName,
            mod_version = ModVersion,
            protocol_version = ProtocolVersion,
            game_version = ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "unknown",
            status = ReflectedGameMembers.ResolveStatus(),
            api_host = HttpServer.Instance.Host,
            api_port = HttpServer.Instance.Port,
            process_id = Environment.ProcessId,
            instance_role = InstanceRole.Current,
            mcp_enabled = mcp?.Enabled == true,
            mcp_url = mcp?.EndpointUrl,
            play_running = AgentRuntime.Instance.PlayRunning,
            play_phase = AgentRuntime.Instance.PlayPhase,
            stop_kind = AgentRuntime.Instance.StopKind,
            session_requests = AgentRuntime.Instance.SessionRequests,
            companion_process_alive = roleData.companion_process_alive,
            companion_process_exited = roleData.companion_process_exited,
            companion = roleData.companion,
            dual_status = roleData.dual_status,
            dual_launch_outcome = roleData.dual_launch_outcome,
            team_control_status = roleData.team_control_status,
            compatibility = ReflectedGameMembers.BuildHealthSection(),
            state_build = StateBuildTiming.Instance.Snapshot()
        };
    }

    /// <summary>
    /// Present on the host window once a teammate session exists, so a caller can find the
    /// companion API and tell which route this launch took. The session token is deliberately
    /// absent: it authorizes control from this process only, and POST /teammate/control is the
    /// supported path for everyone else.
    /// </summary>
    private static object? BuildCompanionSessionData()
    {
        var connection = InstanceRole.IsCompanion ? null : LocalDualInstanceLauncher.Connection;
        // The launcher keeps the last session handle so a retry cannot start a third window; that
        // handle must not outlive the process it points at, or /health would advertise an API port
        // nobody is listening on.
        if (connection == null || LocalDualInstanceLauncher.CompanionProcessExited)
        {
            return null;
        }

        return new
        {
            api_host = "127.0.0.1",
            api_port = connection.Port,
            process_id = connection.ProcessId,
            auto_play = AgentRuntime.Instance.CompanionAutoPlay
        };
    }

    /// <summary>
    /// Reads the optional <c>limit</c> query parameter for <c>GET /decisions</c>, clamped so a
    /// caller cannot ask the mod to materialize an unbounded slice of the log.
    /// </summary>
    internal static int ReadDecisionLimit(HttpListenerRequest request)
    {
        var raw = request.QueryString["limit"];
        return int.TryParse(raw, out var parsed) ? Math.Clamp(parsed, 1, 200) : 50;
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

    // Every JSON route needs the same two guarantees: a bounded body, and malformed JSON reported as
    // a 400 request error instead of escaping as an unhandled 500.
    private static void RequireBoundedBody(HttpListenerRequest request)
    {
        if (request.ContentLength64 < 0 || request.ContentLength64 > 16000)
        {
            throw new ApiException(400, "invalid_request", "A bounded JSON body is required.");
        }
    }

    private static async Task<T?> ReadJsonBodyAsync<T>(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        try { return await JsonHelper.DeserializeAsync<T>(request.InputStream, cancellationToken); }
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
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeatCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (!await subscription.Reader.WaitToReadAsync(heartbeatCts.Token))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await WriteSseCommentAsync(response, "heartbeat");
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

    private sealed class SessionControlRequest
    {
        public bool? running { get; set; }
    }
}
