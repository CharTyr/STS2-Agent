using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The live reasoning stream: the buffer that holds a thinking model's partial thought, and the wiring
/// that carries it from the provider to the overlay without ever doubling a turn's reasoning.
/// </summary>
/// <remarks>
/// Two halves, because they fail differently. The buffer's own rules are executed here; the wiring that
/// uses it lives in files this suite cannot compile (they reach the game runtime and Godot), so it is
/// pinned as source text the way the other overlay contracts are. The gap this suite cannot close --
/// that Godot really renders the bubble -- is what a live pass is for.
/// </remarks>
internal static class LiveThoughtBufferTests
{
    /// <summary>
    /// The default configuration (reasoning display off) does not merely hide the scratchpad: nothing is
    /// stored, so there is nothing a later repaint, screenshot or export could pick up.
    /// </summary>
    public static void NothingIsStoredWhileTheSwitchIsOff()
    {
        var buffer = new LiveThoughtBuffer();
        buffer.Report("weigh the relic against the curse", enabled: false);
        Assert.Equal(string.Empty, buffer.Text);

        // And the switch is read per report, not captured: turning it on starts showing the same
        // request's reasoning from that point on.
        buffer.Report("weigh the relic against the curse", enabled: true);
        Assert.Equal("weigh the relic against the curse", buffer.Text);

        // Turning it off mid-request drops what was already shown: the switch off means nothing is
        // held, not merely that the current drawing is skipped.
        buffer.Report("weigh the relic against the curse and more", enabled: false);
        Assert.Equal(string.Empty, buffer.Text);
    }

    public static void BlankReasoningIsNotStored()
    {
        var buffer = new LiveThoughtBuffer();
        buffer.Report("  ", enabled: true);
        buffer.Report(null, enabled: true);
        buffer.Report("\n\n", enabled: true);
        Assert.Equal(string.Empty, buffer.Text);
    }

    /// <summary>
    /// The streamed text and the recorded bubble are clipped to the same budget, and a completed turn
    /// leaves the buffer empty -- which is what makes a second, duplicate reasoning bubble impossible.
    /// </summary>
    public static void StreamedTextIsClippedAndCleared()
    {
        var buffer = new LiveThoughtBuffer();
        var longThought = new string('x', LiveThoughtBuffer.MaxChars + 200);
        buffer.Report(longThought, enabled: true);
        Assert.Equal(LiveThoughtBuffer.MaxChars + 1, buffer.Text.Length);
        Assert.EndsWith("…", buffer.Text);

        buffer.Reset();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(600, LiveThoughtBuffer.MaxChars);
    }
}

/// <summary>
/// The wiring of the live stream across the files that own it: the client reports, the loop forwards, the
/// runtime gates and clears, and the overlay draws only what the player asked to see.
/// </summary>
internal static class LiveReasoningWiringTests
{
    private const string Loop = "STS2AIAgent/Agent/AgentLoop.cs";
    private const string Runtime = "STS2AIAgent/Agent/AgentRuntime.cs";
    private const string Accounting = "STS2AIAgent/Agent/AgentRuntime.Accounting.cs";
    private const string Status = "STS2AIAgent/Agent/AgentRuntime.Status.cs";
    private const string ChatCard = "STS2AIAgent/Ui/AgentOverlayHost.ChatCard.cs";
    private const string Session = "STS2AIAgent/Agent/AgentRuntime.Session.cs";

    /// <summary>
    /// The one request a player watches think carries the callback, and the runtime is what supplies it.
    /// </summary>
    public static void TheLiveCallbackTravelsFromTheRuntimeToTheRequest()
    {
        var loop = AgentSourceFixture.Read(Loop);
        Assert.Contains("OnReasoningDelta = _onReasoningDelta", loop);
        Assert.Contains("Action<string>? onReasoningDelta = null", loop);
        Assert.Contains(
            "OnReasoningDelta = _onReasoningDelta",
            AgentSourceFixture.MethodBody(loop, "CompleteWithToolsAsync"));

        var runtime = AgentSourceFixture.Read(Runtime);
        Assert.Contains("onReasoningDelta: ReportReasoningDelta", runtime);
        var save = AgentSourceFixture.MethodBody(runtime, "SaveSettings");
        Assert.Contains("if (!settings.ShowThinkingInChat) _liveThought.Reset();", save);
    }

    /// <summary>
    /// The player's switch is the gate, and the streamed partial is dropped before the recorded bubble is
    /// added, so one turn can never draw its reasoning twice.
    /// </summary>
    public static void TheStreamedBubbleIsClearedBeforeTheRecordedOne()
    {
        var accounting = AgentSourceFixture.Read(Accounting);
        var report = AgentSourceFixture.MethodBody(accounting, "ReportReasoningDelta");
        Assert.Contains("_liveThought.Report(accumulated, ShowsThinkingInChat())", report);

        var traces = AgentSourceFixture.MethodBody(accounting, "AppendTurnTraces");
        var cleared = traces.IndexOf("_liveThought.Reset();", StringComparison.Ordinal);
        var recorded = traces.IndexOf("AddHistoryCore(\"thought\"", StringComparison.Ordinal);
        Assert.True(cleared >= 0, "AppendTurnTraces no longer clears the streamed reasoning buffer.");
        Assert.True(
            recorded > cleared,
            "The streamed buffer has to be cleared before the recorded thought bubble is appended, or the "
            + "log draws the same reasoning twice.");
        Assert.Contains("public string LiveThought => _liveThought.Text;", accounting);

        // A turn that ends without a recorded bubble (pause, cancel, provider failure) clears too: the
        // turn's own finally sits before the receipt is committed, so the stream ends with the turn.
        var turn = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Runtime), "AutoPlayLoopAsync");
        Assert.Contains("ClearLiveThought();", turn);
        Assert.Contains("ClearLiveThought();", AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Runtime), "SendChatCoreAsync"));
        // Every other path that makes a model request through the loop streams into the same buffer:
        // single-step (which never reaches the auto-play turn's finally) and the proactive chat that
        // runs after a turn (whose error path appends nothing that could replace the partial).
        var step = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Runtime), "StepOnceCoreAsync");
        Assert.Contains("ClearLiveThought();", step[step.IndexOf("finally", StringComparison.Ordinal)..]);
        // One line, not a span: CI checks sources out with CRLF, and a "\n" in the needle never matches.
        Assert.Contains("finally { ClearLiveThought(); FlushSessionIfDirty(); _turnGate.Release(); }", turn);
        Assert.Contains("ClearLiveThought();", AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Status), "SetRequestingModelStatus"));
    }

    /// <summary>
    /// The conversation draws the partial after the recorded turns and only while the switch is on.
    /// </summary>
    public static void TheOverlayDrawsThePartialOnlyWhenThinkingIsShown()
    {
        var log = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(ChatCard), "RefreshChatLog");
        Assert.Contains("AgentRuntime.Instance.LiveThought", log);
        Assert.Contains("showThinking && liveThought.Length > 0", log);
        Assert.Contains("Loc.T(\"思考中\")", log);
    }

    /// <summary>
    /// A streamed scratchpad is display state, not a record: it must not reach the persisted session, the
    /// chat history or the decision log, or "show reasoning" would leak into files that outlive the turn.
    /// </summary>
    public static void ThePartialStaysOutOfEverythingPersisted()
    {
        Assert.False(
            AgentSourceFixture.Read(Session).Contains("LiveThought", StringComparison.Ordinal),
            "The streamed reasoning reached the session persistence path; it is transient display state.");
        Assert.False(
            AgentSourceFixture.Read("STS2AIAgent/Agent/PlaySessionRecord.cs").Contains("LiveThought", StringComparison.Ordinal),
            "The persisted record grew a field for the streamed reasoning.");
        Assert.False(
            AgentSourceFixture.Read("STS2AIAgent/Agent/PlaySessionStore.cs").Contains("LiveThought", StringComparison.Ordinal),
            "The session store writes the streamed reasoning.");
        Assert.False(
            AgentSourceFixture.Read("STS2AIAgent/Agent/DecisionLog.cs").Contains("LiveThought", StringComparison.Ordinal),
            "The decision log records the streamed reasoning.");
    }
}
