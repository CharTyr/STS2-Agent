namespace STS2AIAgent.Tests;

internal static class SessionControlContractTests
{
    public static void RouterExposesLocalSessionControl()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        Assert.Contains("/session/control", source, StringComparison.Ordinal);
        Assert.Contains("local_only", source, StringComparison.Ordinal);
        Assert.Contains("SetCompanionRunningAsync", AgentSourceFixture.MethodBody(source, "HandleAsync"), StringComparison.Ordinal);
        Assert.Contains("play_phase = AgentRuntime.Instance.PlayPhase", source, StringComparison.Ordinal);
        Assert.Contains("session_requests = AgentRuntime.Instance.SessionRequests", source, StringComparison.Ordinal);
        Assert.Contains("LocalDualInstanceLauncher.CompanionProcessExited", source, StringComparison.Ordinal);
        Assert.Contains("companion_process_exited = roleData.companion_process_exited", source, StringComparison.Ordinal);
    }

    public static void RouterExposesLocalMcpControl()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var body = AgentSourceFixture.MethodBody(source, "HandleAsync");
        Assert.Contains("/mcp/control", body, StringComparison.Ordinal);
        Assert.Contains("AgentRuntime.Instance.SetMcpEnabled(mcpControl.running.Value)", body, StringComparison.Ordinal);
        Assert.Contains("mcp_enabled = AgentRuntime.Instance.McpRunning", body, StringComparison.Ordinal);
        Assert.Contains("MCP control is only available on loopback.", body, StringComparison.Ordinal);
    }

    public static void RouterExposesLocalScreenshot()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var body = AgentSourceFixture.MethodBody(source, "HandleAsync");
        Assert.Contains("/vision/screenshot", body, StringComparison.Ordinal);
        // The bridge's capture hides the overlay for a frame; a direct CaptureJpeg drew the mod's panel.
        Assert.Contains("CaptureScreenshotJpegAsync", body, StringComparison.Ordinal);
        Assert.False(body.Contains("ScreenshotService.CaptureJpeg", StringComparison.Ordinal),
            "The HTTP screenshot must go through GameBridge.CaptureScreenshotJpegAsync so the overlay is hidden.");
        Assert.Contains("image/jpeg", body, StringComparison.Ordinal);
        Assert.Contains("Screenshots are only available on loopback.", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The live defect: a JPEG delivered with the agent panel drawn over the game
    /// (<c>build/live-2026-09-23/menu-capture.jpg</c>) even though the route went through the bridge.
    /// </summary>
    /// <remarks>
    /// The bridge was hiding the overlay all along; it waited for the wrong frame. Godot's SceneTree
    /// <c>ProcessFrame</c> (what <c>WaitForNextFrameAsync</c> resolves on) is emitted before the
    /// RenderingServer updates the viewports for that frame, so the capture read the frame that had
    /// been drawn with the panel still on it. These contracts pin the things the offline suite cannot
    /// execute: the wait ends on a drawn frame, the bridge runs the tested capture policy, and the
    /// capture lease has exactly one owner.
    /// </remarks>
    public static void ScreenshotWaitsForADrawnFrameAndRestoresTheOverlay()
    {
        var bridge = AgentSourceFixture.Read("STS2AIAgent/Agent/GameBridge.cs");
        var capture = AgentSourceFixture.MethodBody(bridge, "CaptureScreenshotJpegAsync");
        Assert.Contains("ScreenshotPolicy.CaptureAsync", capture, StringComparison.Ordinal);
        Assert.Contains("ScreenshotService.CreateCaptureHost()", capture, StringComparison.Ordinal);
        Assert.False(capture.Contains("ScreenshotService.BeginCapture", StringComparison.Ordinal),
            "the capture sequence belongs to the tested policy, not to the bridge");
        Assert.False(capture.Contains("WaitForNextFrameAsync", StringComparison.Ordinal),
            "WaitForNextFrameAsync resolves on ProcessFrame, which is emitted before the frame is drawn");
        // The lease is taken on the calling thread, before the game-thread post: waiting for it inside
        // the game thread would freeze the game for the length of another capture's frame wait.
        Assert.Contains("using var gate = await ScreenshotGateLeaseAsync(cancellationToken)", capture, StringComparison.Ordinal);
        // One process-wide gate and policy: overlapping captures must not interleave their
        // hide/restore pairs, and a per-request instance would do exactly that.
        Assert.Contains("private static readonly SemaphoreSlim ScreenshotGate = new(1, 1);", bridge, StringComparison.Ordinal);
        Assert.Contains("private static readonly ScreenshotCapturePolicy ScreenshotPolicy", bridge, StringComparison.Ordinal);

        var service = AgentSourceFixture.Read("STS2AIAgent/Vision/ScreenshotService.cs");
        Assert.Contains("RenderingServer.SignalName.FramePostDraw", service, StringComparison.Ordinal);
        Assert.False(service.Contains("SignalName.ProcessFrame", StringComparison.Ordinal),
            "the read-back must wait for the frame the RenderingServer finished, not for ProcessFrame");
        // The wait is bounded so a minimized or occluded window fails the capture instead of hanging
        // the request or handing back a stale frame.
        Assert.Contains("Task.WhenAny", service, StringComparison.Ordinal);
        Assert.Contains("IsOverlayVisible", service, StringComparison.Ordinal);
        Assert.Contains("RestoreOverlay", service, StringComparison.Ordinal);

        var policy = AgentSourceFixture.Read("STS2AIAgent/Agent/ScreenshotCapturePolicy.cs");
        Assert.Contains("FramesToSettleAfterHide", policy, StringComparison.Ordinal);
        var finallyBody = policy[policy.IndexOf("finally", StringComparison.Ordinal)..];
        Assert.Contains("RestoreOverlay", finallyBody, StringComparison.Ordinal);
        // Exactly one owner for the lease, and it is the bridge. A release in the policy as well as
        // the bridge's own disposal raised the semaphore past its maximum and threw out of a
        // game-thread callback.
        Assert.False(policy.Contains("Release()", StringComparison.Ordinal),
            "the capture lease is released by the caller's lease object, never by the policy");
        Assert.Contains("ScreenshotGateLease", bridge, StringComparison.Ordinal);

        // A read-back that somehow resumed off the game thread is "no screenshot" (the documented
        // 409 screenshot_unavailable), not an exception the route turns into a 500.
        var readBack = AgentSourceFixture.MethodBody(service, "TryCaptureJpeg");
        Assert.False(readBack.Contains("throw new", StringComparison.Ordinal),
            "TryCaptureJpeg throws again; the route maps that to a 500 instead of screenshot_unavailable");

        // A theme or language rebuild during a capture keeps the capture-hidden panel hidden until the
        // capture's own restore, instead of reading the hidden panel as "the player closed it".
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        var rebuild = AgentSourceFixture.MethodBody(overlay, "RebuildInPlace");
        Assert.Contains("_captureHidden", rebuild, StringComparison.Ordinal);
    }

    public static void WorkshopStagingKeepsLocalCandidate()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs");
        Assert.Contains("Keeping the already-installed local STS2AIAgent copy", source, StringComparison.Ordinal);
        var localGuard = source.IndexOf("var flatDll = Path.Combine", StringComparison.Ordinal);
        var workshopLookup = source.IndexOf("FindSubscribedWorkshopModDir()", StringComparison.Ordinal);
        Assert.True(localGuard >= 0 && workshopLookup > localGuard, "local candidate guard must run before Workshop staging");
    }

    /// <summary>
    /// The step button spends session budget too. It used to record only the display counter, so the
    /// guard never advanced and stepping repeatedly could pass the configured request cap.
    /// </summary>
    public static void StepOnceRecordsTheSessionBudget()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        var step = AgentSourceFixture.MethodBody(source, "StepOnceCoreAsync");
        Assert.Contains("_budgetGuard.Observe(result)", step, StringComparison.Ordinal);
        Assert.Contains("SetStop(StopKindPolicy.Budget", step, StringComparison.Ordinal);
    }
}
