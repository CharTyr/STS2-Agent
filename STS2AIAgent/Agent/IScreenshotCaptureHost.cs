namespace STS2AIAgent.Agent;

/// <summary>
/// The game-side half of a screenshot: hiding the mod's own overlay, waiting for a frame the renderer
/// actually finished, and reading the viewport back. Implemented once, in Godot terms, by
/// <c>GameBridge</c>; faked by the offline tests.
/// </summary>
/// <remarks>
/// Split out of the bridge so the sequencing that can go wrong -- hide, wait a *rendered* frame,
/// capture, restore -- is testable off the game thread. The wait answers a question the overlay
/// cannot: a frame was drawn after the overlay was hidden, so the pixels about to be read no longer
/// contain it.
/// </remarks>
internal interface IScreenshotCaptureHost
{
    /// <summary>
    /// Current overlay visibility. Read once, before hiding, so a capture never changes what the
    /// player sees: an overlay that was already hidden (the player pressed the hotkey) stays hidden.
    /// </summary>
    bool IsOverlayVisible { get; }

    /// <summary>Hides the overlay. Synchronous; the caller is already on the game thread.</summary>
    void HideOverlay();

    /// <summary>Puts the overlay back the way <see cref="IsOverlayVisible"/> found it.</summary>
    void RestoreOverlay();

    /// <summary>
    /// One bounded attempt to observe a frame the RenderingServer finished drawing. Returns true when
    /// a frame landed, false when none did inside <paramref name="frameBudget"/> -- the case a
    /// minimized or occluded window is in, and the case that must never turn into a hang, a stale
    /// frame, or a capture taken with the overlay still on screen.
    /// </summary>
    Task<bool> TryWaitForRenderedFrameAsync(TimeSpan frameBudget, CancellationToken cancellationToken);

    /// <summary>Reads the viewport back as JPEG bytes, or null when it produced no usable frame.</summary>
    byte[]? TryCaptureJpeg();
}
