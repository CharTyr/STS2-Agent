namespace STS2AIAgent.Tests;

/// <summary>
/// The conversation as the decision flow, and the two defects the live pass found in it.
/// </summary>
/// <remarks>
/// Both were invisible to a screenshot: the reasoning switch wrote a setting nothing ever read, and a
/// refresh pass that threw in any earlier block left the conversation frozen at its last snapshot
/// while everything else on the page kept updating. Neither can be measured offline -- these pin the
/// wiring that makes each one work, in the files that own it.
/// </remarks>
internal static class OverlayChatStreamContractTests
{
    private const string Pages = "STS2AIAgent/Ui/AgentOverlayHost.Pages.cs";
    private const string ChatCard = "STS2AIAgent/Ui/AgentOverlayHost.ChatCard.cs";
    private const string Accounting = "STS2AIAgent/Agent/AgentRuntime.Accounting.cs";

    /// <summary>
    /// A turn's reasoning and the action it took become turns of the conversation, so the page reads
    /// as what the agent decided rather than as a chat held beside the decisions.
    /// </summary>
    public static void ReasoningAndActionsBecomeConversationTurns()
    {
        var runtime = AgentSourceFixture.Read(Accounting);
        var traces = AgentSourceFixture.MethodBody(runtime, "AppendTurnTraces");

        Assert.Contains("AddHistoryCore(\"thought\"", traces);
        Assert.Contains("AddHistoryCore(\"action\"", traces);
        // The reasoning is the model's scratchpad: it enters the stream only when the player asked.
        Assert.Contains("ShowsThinkingInChat()", traces);

        // Both turn-recording paths go through it: the play loop and the conversation itself.
        var play = AgentSourceFixture.MethodBody(runtime, "ApplyPlayResult");
        Assert.Contains("AppendTurnTraces(result);", play);
        var chat = AgentSourceFixture.MethodBody(AgentSourceFixture.ReadAgentRuntime(), "SendChatCoreAsync");
        Assert.Contains("AppendTurnTraces(result);", chat);
    }

    /// <summary>
    /// The in-memory history cap, the persisted tail and what the log draws are one number, and the
    /// log says so when the cap has dropped turns off the front.
    /// </summary>
    public static void OneCapForMemoryDiskAndTheLog()
    {
        var runtime = AgentSourceFixture.Read(Accounting);
        Assert.Contains("ChatHistoryLimit = PlaySessionStore.MaxChatTurns", runtime);
        Assert.Contains("HistoryTrimmedCount", runtime);

        var store = AgentSourceFixture.Read("STS2AIAgent/Agent/PlaySessionStore.cs");
        Assert.Contains("MaxChatTurns = 200", store);

        var session = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Session.cs");
        Assert.Contains("TakeLast(ChatHistoryLimit)", session);

        var log = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(ChatCard), "RefreshChatLog");
        Assert.Contains("HistoryTrimmedCount", log);
        Assert.Contains("ChatTailLimit", log);
    }

    /// <summary>
    /// The log draws the two stream roles distinctly, keeps the player and the assistant as they were,
    /// and follows the newest turn only while the reader is already at the bottom.
    /// </summary>
    /// <remarks>
    /// It used to clear, redraw and force the scroll to the bottom on every refresh and every 800 ms
    /// tick, so an earlier turn could not be read: the view was pulled back down within a second.
    /// </remarks>
    public static void TheLogDrawsEachRoleAndFollowsTheNewest()
    {
        var card = AgentSourceFixture.Read(ChatCard);
        var log = AgentSourceFixture.MethodBody(card, "RefreshChatLog");

        Assert.Contains("turn.Role == \"thought\"", log);
        Assert.Contains("turn.Role == \"action\"", log);
        // Only the player's switch decides whether reasoning is drawn; an entry recorded while it was
        // on must not stay visible after it is turned off.
        Assert.Contains("showThinking", log);
        Assert.Contains("FormatTurn(turn.Role == \"user\"", log);

        // The entries go through RichLog, never straight into the label.
        Assert.Contains("_chatLogView.Show(entries);", log);
        Assert.False(log.Contains("_chatLog.Clear()", StringComparison.Ordinal),
            "The conversation clears its label on every refresh again, which resets the reader's scroll.");
        Assert.False(log.Contains("ScrollChatToBottom", StringComparison.Ordinal),
            "The conversation forces its scroll to the bottom again.");

        var richLog = AgentSourceFixture.Read("STS2AIAgent/Ui/RichLog.cs");
        var show = AgentSourceFixture.MethodBody(richLog, "Show");
        // An unchanged list touches nothing; the bottom is followed only by a reader who was there.
        Assert.Contains("if (change == RichLogChange.None)", show);
        Assert.Contains("var atEdge = _atEdge;", show);
        Assert.Contains("bar.Value + bar.Page >= bar.MaxValue - EdgeSlack", richLog);
        Assert.Contains("if (_anchor == LogAnchor.Bottom && atEdge)", show);
        // Deferred, because the BBCode appended this frame has not been laid out yet.
        Assert.Contains(".CallDeferred();", show);
        Assert.Contains("label.ScrollFollowing = false;", richLog);
    }

    /// <summary>
    /// One throwing block cannot freeze the page: the chat is drawn first and the whole pass is
    /// guarded, and the panel tick redraws the chat too.
    /// </summary>
    public static void AThrowingBlockCannotFreezeTheConversation()
    {
        var overlay = AgentSourceFixture.ReadOverlayHost();
        var refresh = AgentSourceFixture.MethodBody(overlay, "RefreshDynamic");

        // Every section runs under its own guard, so one throwing section cannot stop the next.
        var guard = AgentSourceFixture.MethodBody(overlay, "Guarded");
        Assert.Contains("catch (Exception ex)", guard);
        Assert.Contains("Log.Warn(", guard);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(refresh, @"\btry\b"),
            "RefreshDynamic is one guarded block again; a throw in one section would freeze the rest.");

        // The conversation is reached before any block that reads game state.
        var chat = refresh.IndexOf("Guarded(\"chat\", RefreshChatLog);", StringComparison.Ordinal);
        var decisions = refresh.IndexOf("RefreshDecisionPage(view);", StringComparison.Ordinal);
        Assert.True(chat >= 0, "RefreshDynamic no longer redraws the conversation.");
        Assert.True(decisions > chat, "The conversation has to be drawn before the blocks that can throw.");

        var tick = AgentSourceFixture.MethodBody(overlay, "OnProcessFrame");
        Assert.Contains("RefreshChatLog();", tick);
    }

    /// <summary>
    /// The conversation has no "let the AI act" switch anywhere in its chain: acting is the auto-play
    /// and single-step controls' job, and a message that asks for a move is the only other opt-in.
    /// </summary>
    public static void TheConversationHasNoActSwitch()
    {
        Assert.False(
            AgentSourceFixture.ReadOverlayHost().Contains("AllowAct", StringComparison.Ordinal),
            "An act switch is back in the overlay; the conversation is read-only.");
        Assert.False(
            AgentSourceFixture.Read("STS2AIAgent/Agent/IGameBridge.cs").Contains("AllowAct", StringComparison.Ordinal),
            "ChatOptions still carries the act switch the UI no longer sets.");

        var chat = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Agent/AgentLoop.cs"), "ChatAsync");
        Assert.Contains("PlayIntent.Detect(userText)", chat);
        Assert.False(
            chat.Contains("options.AllowAct", StringComparison.Ordinal),
            "The chat turn reads the act switch again instead of the player's own words.");
    }

    /// <summary>
    /// The Jev panel reads the execution layer's own last reading, and only a dual-layer turn writes
    /// one.
    /// </summary>
    public static void TheJevPanelShowsWhatJevChose()
    {
        var accounting = AgentSourceFixture.Read(Accounting);
        var reading = AgentSourceFixture.MethodBody(accounting, "RecordJevReading");
        Assert.Contains("result.Confidence == null", reading);
        Assert.Contains("_lastJevChoice", reading);
        Assert.Contains("_lastJevProbabilities", reading);
        Assert.Contains("RecordJevReading(result);", AgentSourceFixture.MethodBody(accounting, "RecordTurnReceipt"));

        var refresh = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Pages), "RefreshDynamic");
        Assert.Contains("RefreshJevReading(coopMode, mode);", refresh);
        var modeReading = AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.PlayControl.cs"), "RefreshJevReading");
        Assert.Contains("companion?.Choice : AgentRuntime.Instance.LastJevChoice", modeReading);
        Assert.Contains("companion?.Probabilities : AgentRuntime.Instance.LastJevProbabilities", modeReading);
        Assert.Contains("companion?.Danger : AgentRuntime.Instance.LastJevDanger", modeReading);
        Assert.Contains("companion?.Latency : AgentRuntime.Instance.LastJevLatency", modeReading);
    }
}
