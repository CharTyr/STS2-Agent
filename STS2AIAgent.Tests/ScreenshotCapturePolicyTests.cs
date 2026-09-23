using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The sequencing of one screenshot, against a fake game-side host.
/// </summary>
/// <remarks>
/// The live defect this covers: <c>GET /vision/screenshot</c> returned a JPEG with the agent panel
/// drawn over the game (<c>build/live-2026-09-23/menu-capture.jpg</c>). Hiding the panel was never
/// the missing step -- waiting for the wrong frame was: the capture resolved on the SceneTree's
/// <c>ProcessFrame</c>, which is emitted before the RenderingServer draws the frame, so the pixels
/// read back belonged to the frame drawn with the overlay still visible.
/// <para>
/// The rule these tests pin is the seam's contract, not Godot's: the capture lease is taken before the
/// game thread (never inside it, where a held lease would freeze the game), the overlay is hidden
/// before waiting, at least one <i>drawn</i> frame passes before anything is read, nothing is read
/// when no such frame arrives, and the overlay is restored and the lease released on every path out.
/// That the host's wait is really <c>RenderingServer.FramePostDraw</c> is a source contract in
/// <c>SessionControlContractTests</c>, because this suite cannot compile Godot.
/// </para>
/// </remarks>
internal static class ScreenshotCapturePolicyTests
{
    /// <summary>At least one capture must be able to finish inside this deadline.</summary>
    private static readonly TimeSpan GenerousDeadline = TimeSpan.FromSeconds(5);

    public static async Task CaptureHidesWaitsForDrawnFramesAndRestores()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost();

        var jpeg = await capture.RunAsync(host, GenerousDeadline, CancellationToken.None);

        Assert.NotNull(jpeg);
        Assert.Equal(1, host.HideCount);
        Assert.Equal(1, host.RestoreCount);
        // More than one drawn frame, not one: rasterizing a frame lags the CPU frame by at least one
        // (the render thread consumes the frame's commands after the game thread has run the frame
        // that issued them), so the first post-draw a hide can observe may still be the frame
        // assembled before the hide. Settling past that boundary is what makes the read-back
        // trustworthy; the exact count is the policy's, the requirement these tests pin is "> 0 and
        // read only after them".
        Assert.True(
            host.FrameWaitCount >= 2,
            $"{host.FrameWaitCount} drawn frame(s) were waited for; a hide needs more than one to be "
            + "provably on screen before pixels are read");
        Assert.Equal(host.FrameWaitCount, host.FramesDrawnSinceHide);
        // Exactly one hide/restore pair, and the read-back happened strictly between them.
        Assert.True(host.CapturedWhileHidden, "the capture must be read while the overlay is hidden");
        Assert.Equal(
            "hide," + string.Join(",", Enumerable.Repeat("frame", host.FrameWaitCount)) + ",capture,restore",
            string.Join(",", host.Events));
        Assert.Equal(1, capture.GateCount, "a capture that finished must hand the lease back");
    }

    public static async Task AnOverlayThePlayerHadHiddenIsNotReopened()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost { OverlayVisible = false };

        var jpeg = await capture.RunAsync(host, GenerousDeadline, CancellationToken.None);

        Assert.NotNull(jpeg);
        Assert.Equal(0, host.HideCount);
        Assert.Equal(0, host.RestoreCount);
        Assert.False(host.Visible, "a capture must reproduce the player's view, not impose its own");
    }

    public static async Task NoDrawnFrameYieldsNoScreenshotAndStillRestores()
    {
        // A minimized or fully occluded window stops rendering: the frame signal never arrives. The
        // capture must give up visibly instead of reading the last frame, which still has the panel.
        var capture = NewFixture();
        var host = new FakeCaptureHost { FrameWaitReturns = false };

        var jpeg = await capture.RunAsync(host, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Assert.Null(jpeg);
        Assert.Equal(0, host.CaptureCount);
        Assert.Equal(1, host.HideCount);
        Assert.Equal(1, host.RestoreCount);
        Assert.True(host.Visible, "the overlay has to come back even when the capture fails");
        Assert.Equal(1, capture.GateCount, "a failed capture must hand the lease back");
    }

    public static async Task ACaptureThatNeverGetsAFrameDoesNotStall()
    {
        // Every frame wait blocks for its own budget and then reports that nothing was drawn -- the
        // shape a frozen renderer takes. The deadline, not the renderer, has to end the capture.
        var capture = NewFixture();
        var host = new FakeCaptureHost { FrameWaitReturns = false, FrameWaitBlocks = true };

        var started = DateTime.UtcNow;
        var jpeg = await capture.RunAsync(host, TimeSpan.FromMilliseconds(150), CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        Assert.Null(jpeg);
        Assert.Equal(0, host.CaptureCount);
        Assert.Equal(1, host.RestoreCount);
        Assert.True(
            elapsed < TimeSpan.FromSeconds(5),
            $"the capture took {elapsed.TotalMilliseconds:0} ms; the deadline has to end it");
    }

    public static async Task CancellationRestoresTheOverlayAndCapturesNothing()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost { FrameWaitReturns = false, FrameWaitBlocks = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        // A cancelled request is surfaced as cancellation, not as an anonymous failure; what matters
        // here is that the overlay is back and no pixels were read.
        await AssertCanceled(() => capture.RunAsync(host, GenerousDeadline, cts.Token));

        Assert.Equal(0, host.CaptureCount);
        Assert.Equal(1, host.HideCount);
        Assert.Equal(1, host.RestoreCount);
        Assert.True(host.Visible, "a cancelled capture must still put the overlay back");
        Assert.Equal(1, capture.GateCount, "a cancelled capture must hand the lease back");
    }

    public static async Task AnEmptyViewportReadIsNoScreenshotAndStillRestores()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost { Jpeg = null };

        var jpeg = await capture.RunAsync(host, GenerousDeadline, CancellationToken.None);

        Assert.Null(jpeg);
        Assert.Equal(1, host.HideCount);
        Assert.Equal(1, host.RestoreCount);
        Assert.Equal(1, capture.GateCount);
    }

    /// <summary>
    /// The deadline check inside the policy: with an expired deadline the capture returns before it
    /// touches the overlay at all, while the lease the caller took is still handed straight back.
    /// </summary>
    /// <remarks>
    /// The lease is taken outside the policy because the real bridge takes it before its game-thread
    /// post. Establishing that here also keeps this test honest in a way a wait with a zero timeout is
    /// not: <c>SemaphoreSlim.WaitAsync(TimeSpan.Zero, ...)</c> returns <c>true</c> when the gate is
    /// free (measured), so "zero timeout" is not a way to reach the policy's own deadline branch.
    /// </remarks>
    public static async Task AnExpiredDeadlineCapturesNothingAndHoldsNothing()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost();

        Assert.Equal(1, capture.GateCount);
        await capture.AcquireAsync();
        Assert.Equal(0, capture.GateCount);
        try
        {
            var jpeg = await capture.Policy.CaptureAsync(host, TimeSpan.Zero, CancellationToken.None);

            Assert.Null(jpeg);
            Assert.Equal(0, host.HideCount);
            Assert.Equal(0, host.FrameWaitCount);
        }
        finally
        {
            capture.Release();
        }

        // A capture that gave up hands nothing held to the next request.
        Assert.Equal(1, capture.GateCount);
    }

    /// <summary>
    /// Overlapping requests queue on the lease, so their hide/restore pairs cannot interleave: the
    /// first restore would show the overlay while the second capture is still waiting for its frame,
    /// and the second JPEG would carry the panel. Serialization only holds if the lease is taken on the
    /// caller side, which is what the real bridge does and what the fixture models.
    /// </summary>
    public static async Task OverlappingCapturesAreSerialized()
    {
        var capture = NewFixture();
        var host = new FakeCaptureHost { FrameWaitDelay = TimeSpan.FromMilliseconds(20) };

        var first = capture.RunAsync(host, GenerousDeadline, CancellationToken.None);
        var second = capture.RunAsync(host, GenerousDeadline, CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.Equal(2, host.HideCount);
        Assert.Equal(2, host.RestoreCount);
        Assert.Equal(4, host.FrameWaitCount);
        Assert.Equal("hide,frame,frame,capture,restore,hide,frame,frame,capture,restore", string.Join(",", host.Events));
        Assert.Equal(1, capture.GateCount);
    }

    /// <summary>
    /// While one capture holds the lease, a second one gives up visibly inside its own deadline instead
    /// of queueing behind it forever. Queueing is what the lease is for; the bound is what keeps a hung
    /// capture from turning every later screenshot into a hang.
    /// </summary>
    public static async Task AHeldGateTimesOutTheWaiterInsteadOfStallingIt()
    {
        var capture = NewFixture();
        var waiterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = new FakeCaptureHost
        {
            FrameWaitReturns = false,
            FrameWaitBlocks = true,
            FrameWaitEntered = waiterEntered
        };
        var waiter = new FakeCaptureHost();
        using var holderCts = new CancellationTokenSource();

        var holding = capture.RunAsync(holder, GenerousDeadline, holderCts.Token);
        await waiterEntered.Task;

        var waited = await capture.RunAsync(waiter, TimeSpan.FromMilliseconds(150), CancellationToken.None);

        Assert.Null(waited);
        Assert.Equal(0, waiter.HideCount);
        Assert.Equal(0, waiter.FrameWaitCount);
        Assert.Equal(0, capture.GateCount, "the holder still owns the lease");

        // Cancelling the holder releases the lease to that capture's finally, never twice.
        holderCts.Cancel();
        await AssertCanceled(() => holding);
        Assert.Equal(1, capture.GateCount);
    }

    private static async Task AssertCanceled(Func<Task<byte[]?>> body)
    {
        try
        {
            await body();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new Exception("Expected the capture to surface the caller's cancellation.");
    }

    private static CaptureFixture NewFixture() => new();

    /// <summary>
    /// One capture, the way the bridge runs it: take the lease on the calling thread, then capture with
    /// the policy. The lease belongs to this fixture -- the policy never releases it, which is what
    /// keeps the bridge's own lease from being released a second time.
    /// </summary>
    private sealed class CaptureFixture
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public CaptureFixture()
        {
            Policy = new ScreenshotCapturePolicy(_gate);
        }

        public ScreenshotCapturePolicy Policy { get; }

        /// <summary>The lease, as the outside world can see it: 1 means free.</summary>
        public int GateCount => _gate.CurrentCount;

        /// <summary>Takes the lease the way a screenshot request does before posting to the game thread.</summary>
        public async Task AcquireAsync()
        {
            if (!await _gate.WaitAsync(GenerousDeadline).ConfigureAwait(true))
            {
                throw new Exception("The capture lease was not free; this fixture expects an uncontended gate.");
            }
        }

        /// <summary>Hands the lease back, the way the bridge's own lease disposal does.</summary>
        public void Release()
        {
            _gate.Release();
        }

        public async Task<byte[]?> RunAsync(
            IScreenshotCaptureHost host,
            TimeSpan renderFrameTimeout,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bool acquired;
            try
            {
                acquired = await _gate.WaitAsync(renderFrameTimeout, timeout.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The lease wait's own deadline, not the caller giving up.
                return null;
            }

            if (!acquired)
            {
                return null;
            }

            try
            {
                return await Policy.CaptureAsync(host, renderFrameTimeout, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// The game-side half of a capture, in memory: it records the order of hide/frame/capture/restore
    /// and can be told to stop producing frames.
    /// </summary>
    private sealed class FakeCaptureHost : IScreenshotCaptureHost
    {
        private readonly object _sync = new();
        private readonly List<string> _events = new();

        private bool _visible = true;

        /// <summary>Whether the overlay is on screen when the capture starts.</summary>
        public bool OverlayVisible
        {
            get => _visible;
            set => _visible = value;
        }

        public bool Visible => _visible;

        public byte[]? Jpeg { get; set; } = new byte[] { 1, 2, 3 };

        /// <summary>When false, the host never observes a drawn frame (a window that stopped drawing).</summary>
        public bool FrameWaitReturns { get; set; } = true;

        /// <summary>When true, the frame wait hangs until its caller's budget cancels it.</summary>
        public bool FrameWaitBlocks { get; set; }

        /// <summary>Completed once the frame wait is entered, so a test can order captures precisely.</summary>
        public TaskCompletionSource? FrameWaitEntered { get; set; }

        public TimeSpan FrameWaitDelay { get; set; } = TimeSpan.Zero;

        public int HideCount { get; private set; }

        public int RestoreCount { get; private set; }

        public int FrameWaitCount { get; private set; }

        public int CaptureCount { get; private set; }

        public int FramesDrawnSinceHide { get; private set; }

        public bool CapturedWhileHidden { get; private set; }

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_sync)
                {
                    return _events.ToArray();
                }
            }
        }

        public bool IsOverlayVisible => _visible;

        public void HideOverlay()
        {
            lock (_sync)
            {
                _events.Add("hide");
                HideCount++;
                _visible = false;
                FramesDrawnSinceHide = 0;
            }
        }

        public void RestoreOverlay()
        {
            lock (_sync)
            {
                _events.Add("restore");
                RestoreCount++;
                _visible = true;
            }
        }

        public async Task<bool> TryWaitForRenderedFrameAsync(TimeSpan frameBudget, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _events.Add("frame");
                FrameWaitCount++;
            }

            FrameWaitEntered?.TrySetResult();

            if (!FrameWaitReturns)
            {
                if (!FrameWaitBlocks)
                {
                    return false;
                }

                // Blocks for the caller's frame budget and reports that no frame arrived -- exactly
                // what a frozen renderer does to a bounded wait. A frame budget that expires is not
                // the caller giving up, so it is reported as "no frame" and not as an exception.
                try
                {
                    await Task.Delay(frameBudget, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The capture's own token was cancelled: let the policy see that, not a timeout.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }

                return false;
            }

            if (FrameWaitDelay > TimeSpan.Zero)
            {
                await Task.Delay(FrameWaitDelay, cancellationToken).ConfigureAwait(false);
            }

            lock (_sync)
            {
                FramesDrawnSinceHide++;
            }

            return true;
        }

        public byte[]? TryCaptureJpeg()
        {
            lock (_sync)
            {
                _events.Add("capture");
                CaptureCount++;
                CapturedWhileHidden = !_visible;
            }

            return Jpeg;
        }
    }
}
