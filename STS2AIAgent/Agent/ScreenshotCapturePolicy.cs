namespace STS2AIAgent.Agent;

/// <summary>
/// Sequences one screenshot: serialize, remember visibility, hide the overlay, wait for a frame the
/// renderer actually finished, read the viewport back, and put the overlay back exactly as it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the wait moved.</b> The capture used to await <c>GameThread.WaitForNextFrameAsync</c>,
/// which resolves on the SceneTree's <c>ProcessFrame</c> -- emitted while the frame is still being
/// processed, <i>before</i> the RenderingServer updates the viewports for it. The pixels a capture
/// then read belonged to the frame that had already been drawn, the one rendered with the overlay
/// still visible, so hiding the panel changed nothing the caller could see. The live evidence is
/// <c>build/live-2026-09-23/menu-capture.jpg</c>: a clean main menu with the agent panel drawn over
/// it. The wait now ends on a frame that has been drawn since the hide
/// (<c>RenderingServer.FramePostDraw</c>, "after the RenderingServer has finished updating all the
/// Viewports"), and settles for more than one so the hide has provably reached a drawn frame rather
/// than the pending command list. Both mechanisms belong to the host; this class owns the order.
/// </para>
/// <para>
/// <b>Never a stall, never a wrong frame.</b> Every frame wait is bounded twice: the host races one
/// signal against <c>FrameBudget</c>, and this loop refuses to start a wait the remaining deadline
/// cannot cover. When frames stop -- a minimized or fully occluded window stops rendering -- the
/// answer is no screenshot (the HTTP boundary turns it into <c>screenshot_unavailable</c>), never a
/// stale frame with the overlay on it, and never a request that hangs on a frame that will not come.
/// </para>
/// <para>
/// <b>Overlay always restored.</b> Hide and restore sit in the same <c>finally</c> path, so a
/// timeout, a cancelled request, a locked renderer or a throw inside the capture all still restore.
/// The captured visibility is remembered rather than assumed, and the restore is a no-op for an
/// overlay that was already hidden.
/// </para>
/// <para>
/// <b>One capture at a time.</b> Hide/restore is a pair of global overlay states, so two overlapping
/// captures interleave them: the first restore shows the overlay while the second capture is still
/// waiting for its frame, and the second JPEG carries the panel. All captures share one gate, which
/// is acquired before anything is hidden and released only after the overlay is back. The gate is
/// owned by <c>GameBridge</c> and shared with every capture in the process, so a single screenshot is
/// held across the whole rendered-frame wait and two requests cannot fence each other's frames.
/// </para>
/// </remarks>
internal sealed class ScreenshotCapturePolicy
{
    /// <summary>
    /// How long one frame wait may take before it is treated as "the renderer produced nothing".
    /// Generous against a 60 Hz frame (16.7 ms) and short enough that a frozen renderer cannot hold
    /// the request: the whole capture is still bounded by the caller's deadline.
    /// </summary>
    private static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Drawn frames required after the hide before the pixels may be read. One is not enough: making
    /// a CanvasItem invisible is a command the RenderingServer consumes at the start of a subsequent
    /// frame, so an earlier frame can still be the one that was drawn with the overlay on it. Two
    /// guarantees the hide has reached a drawn frame, and costs one frame of latency.
    /// </summary>
    private const int FramesToSettleAfterHide = 2;

    private readonly SemaphoreSlim _gate;
    private readonly Action<string>? _logInfo;
    private readonly Action<string>? _logWarning;

    public ScreenshotCapturePolicy(
        SemaphoreSlim gate,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logInfo = logInfo;
        _logWarning = logWarning;
    }

    /// <summary>
    /// Captures one JPEG with the overlay hidden, or null when the capture had no time budget left, no
    /// drawn frame arrived in time, or the viewport produced nothing.
    /// </summary>
    /// <remarks>
    /// Call this on the game thread and nowhere else: hiding a CanvasItem, waiting on the renderer's
    /// signals and reading the viewport all belong to that thread. The gate must already be held when
    /// this is called, taken <b>before</b> the game-thread post on a thread that is allowed to block:
    /// waiting for it here would block the game thread for as long as another capture's frame wait
    /// lasts, which is a frozen game in exchange for a screenshot. <b>The caller owns the lease</b> and
    /// releases it; this method never releases it, because a release here plus the caller's own would
    /// push the semaphore past its maximum and throw out of a game-thread callback.
    /// </remarks>
    public async Task<byte[]?> CaptureAsync(
        IScreenshotCaptureHost host,
        TimeSpan renderFrameTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        // No budget, no capture -- and in particular no hide. A zero or negative budget means this
        // capture can never wait for a drawn frame, so the only outcomes left are a no-op or a stale
        // frame. Returning here keeps the overlay untouched instead of flickering it off and on for a
        // screenshot nobody can take.
        if (renderFrameTimeout <= TimeSpan.Zero)
        {
            _logWarning?.Invoke("Screenshot dropped: the capture had no time left to wait for a frame.");
            return null;
        }

        var deadline = DateTime.UtcNow + renderFrameTimeout;
        // Read once, before hiding: the restore below has to reproduce what the player was looking
        // at, not what this class assumes an overlay should be.
        var overlayWasVisible = host.IsOverlayVisible;
        var overlayHidden = false;
        try
        {
            if (overlayWasVisible)
            {
                host.HideOverlay();
                overlayHidden = true;
            }

            if (!await WaitForDrawnFrameAsync(host, deadline, cancellationToken).ConfigureAwait(true))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var jpeg = host.TryCaptureJpeg();
            if (jpeg == null || jpeg.Length == 0)
            {
                _logWarning?.Invoke("Screenshot dropped: the viewport produced no JPEG.");
                return null;
            }

            return jpeg;
        }
        finally
        {
            // Restored only when this capture is what hid it. An overlay the player had already
            // hidden stays hidden: a capture is not a reason to reopen it on top of the game, and
            // restoring an overlay nobody hid is how a hotkey-hidden panel flashed back on screen.
            if (overlayHidden)
            {
                host.RestoreOverlay();
            }

            // No gate release here on purpose: the caller owns the lease (see the remarks above).
        }
    }

    /// <summary>
    /// Waits until the renderer has drawn <see cref="FramesToSettleAfterHide"/> frames since the
    /// hide, or the deadline passes. False means no trustworthy frame exists, so nothing may be read.
    /// </summary>
    private async Task<bool> WaitForDrawnFrameAsync(
        IScreenshotCaptureHost host,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        for (var drawn = 0; drawn < FramesToSettleAfterHide; drawn++)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _logWarning?.Invoke(
                    "Screenshot dropped: the renderer drew no frame within the capture deadline "
                    + "(a minimized or occluded window stops rendering).");
                return false;
            }

            var budget = remaining < FrameBudget ? remaining : FrameBudget;
            if (!await host.TryWaitForRenderedFrameAsync(budget, cancellationToken).ConfigureAwait(true))
            {
                _logWarning?.Invoke(
                    "Screenshot dropped: no rendered frame arrived within " + budget.TotalMilliseconds.ToString("0")
                    + " ms; the overlay is being restored without reading a stale frame.");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        _logInfo?.Invoke("Screenshot captured after " + FramesToSettleAfterHide + " drawn frames.");
        return true;
    }
}
