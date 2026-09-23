using Godot;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Game;

namespace STS2AIAgent.Vision;

internal static class ScreenshotService
{
    public const int DefaultMaxEdge = 1280;

    internal static Action? BeginCapture;

    internal static Action? EndCapture;

    /// <summary>
    /// The game-side half of a screenshot for <see cref="ScreenshotCapturePolicy"/>: the overlay
    /// hooks, a wait on a frame the renderer actually finished, and the viewport read-back.
    /// </summary>
    /// <remarks>
    /// The frame wait is the fix for the delivered JPEG that still carried the panel. Godot's
    /// SceneTree <c>ProcessFrame</c> (what <c>GameThread.WaitForNextFrameAsync</c> resolves on) is
    /// emitted while the frame is still being processed, before the RenderingServer updates the
    /// viewports for it, so a capture gated on it reads the previously drawn frame -- the one with
    /// the overlay still visible. <c>RenderingServer.FramePostDraw</c> is emitted "at the end of the
    /// frame, after the RenderingServer has finished updating all the Viewports", which is the frame
    /// whose pixels the capture is allowed to read.
    /// </remarks>
    private sealed class GodotCaptureHost : IScreenshotCaptureHost
    {
        /// <summary>
        /// How long to wait after hiding the overlay before capturing, if the caller's frame-wait
        /// signal times out. The policy races this against the frame signal itself, so this is only
        /// the failure path (a minimized or occluded window stops rendering frames): the capture
        /// returns null rather than reading a stale frame with the overlay on it.
        /// </summary>
        private static readonly TimeSpan FrameWaitCap = TimeSpan.FromMilliseconds(250);

        public bool IsOverlayVisible
        {
            get
            {
                var begin = BeginCapture;
                if (begin == null)
                {
                    // No overlay in this process (a companion instance, or the overlay failed to
                    // install). There is nothing to hide and nothing to restore.
                    return false;
                }

                return OverlayVisibleProbe?.Invoke() ?? true;
            }
        }

        public void HideOverlay()
        {
            BeginCapture?.Invoke();
        }

        public void RestoreOverlay()
        {
            EndCapture?.Invoke();
        }

        public async Task<bool> TryWaitForRenderedFrameAsync(TimeSpan frameBudget, CancellationToken cancellationToken)
        {
            var game = NGame.Instance;
            if (game == null || !GodotObject.IsInstanceValid(game))
            {
                return false;
            }

            var tree = game.GetTree();
            if (tree == null || !GodotObject.IsInstanceValid(tree))
            {
                return false;
            }

            var budget = frameBudget < FrameWaitCap ? frameBudget : FrameWaitCap;
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var signal = AwaitFramePostDraw(tree);
            var timeout = Task.Delay(budget, budgetCts.Token);
            var completed = await Task.WhenAny(signal, timeout).ConfigureAwait(true);
            if (completed != signal)
            {
                return false;
            }

            // Consume the signal's own exception (an invalidated target, a shutting-down tree) and
            // keep the continuation on the game thread, where the read-back below belongs.
            await signal.ConfigureAwait(true);
            budgetCts.Cancel();
            return true;
        }

        public byte[]? TryCaptureJpeg()
        {
            // The read-back touches Godot's live viewport and its render target, neither of which may
            // be reached from a background thread. Every wait above keeps its continuation on the game
            // thread (SignalAwaiter posts through Godot's SynchronizationContext), so landing here on
            // anything else is a bug worth failing loudly for: a capture from the wrong thread would
            // read a frame nobody is allowed to read and report it as a screenshot.
            if (!GameThread.IsCurrentThread)
            {
                // No screenshot rather than an exception: the route answers the documented 409
                // screenshot_unavailable instead of a 500, and the overlay is still restored.
                GD.PushWarning("[STS2AIAgent.Vision] Screenshot dropped: the frame wait resumed off the game thread.");
                return null;
            }

            return CaptureJpeg();
        }
    }

    /// <summary>
    /// Set by the overlay while it can answer "is the panel on screen right now". Null when no
    /// overlay is installed; see <see cref="GodotCaptureHost.IsOverlayVisible"/> for what that means.
    /// </summary>
    internal static Func<bool>? OverlayVisibleProbe;

    public static IScreenshotCaptureHost CreateCaptureHost()
    {
        return new GodotCaptureHost();
    }

    public static byte[]? CaptureJpeg(int maxEdge = DefaultMaxEdge, float quality = 0.72f)
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            return null;
        }

        var viewport = game.GetViewport();
        if (viewport == null || !GodotObject.IsInstanceValid(viewport))
        {
            return null;
        }

        var texture = viewport.GetTexture();
        if (texture == null)
        {
            return null;
        }

        var image = texture.GetImage();
        if (image == null || image.IsEmpty())
        {
            image?.Dispose();
            return null;
        }

        using (image)
        {
            var width = image.GetWidth();
            var height = image.GetHeight();
            var longest = Math.Max(width, height);
            if (longest > maxEdge && longest > 0)
            {
                var scale = maxEdge / (float)longest;
                var nextWidth = Math.Max(1, (int)Math.Round(width * scale));
                var nextHeight = Math.Max(1, (int)Math.Round(height * scale));
                image.Resize(nextWidth, nextHeight, Image.Interpolation.Lanczos);
            }

            return image.SaveJpgToBuffer(quality);
        }
    }

    /// <summary>
    /// Resolves on the signal the RenderingServer emits at the end of a frame, once every viewport has
    /// been updated: awaiting this is what makes the read-back see the frame drawn without the overlay.
    /// The SceneTree argument is only the signal target, so an invalid one fails fast instead of
    /// waiting for a frame that will never come.
    /// </summary>
    private static async Task AwaitFramePostDraw(SceneTree sceneTree)
    {
        await sceneTree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
