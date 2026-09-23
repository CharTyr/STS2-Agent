using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The conversation card: what it is built from, and how the turns the runtime records are drawn.
/// </summary>
/// <remarks>
/// Split out of <c>AgentOverlayHost.Pages.cs</c> when the conversation became the decision flow: the
/// stream now carries the model's reasoning and the action each turn took beside the player's own
/// messages. The cut is by what the members touch -- the chat log, its scroll bar, and the
/// chat-side option dials all belong to this one card and to nothing else on the page.
///
/// A turn's role decides how it is drawn -- "user" and "assistant" keep the accent speaker line the
/// co-op chat uses, "action" is the accent-coloured decision record, and "thought" is muted because it
/// is the model's scratchpad and must not compete with what it actually did.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    /// <summary>
    /// How many turns the chat log draws. The runtime keeps a longer history
    /// (<see cref="AgentRuntime.ChatHistoryLimit"/>) so the session file and the model's context
    /// survive; the drawn tail is smaller because the log is re-laid-out in BBCode on every refresh
    /// and tick, and that work runs on the game thread. The omitted-count line above the log covers
    /// the difference, so nothing is hidden without saying so.
    /// </summary>
    private const int ChatTailLimit = 80;

    /// <summary>The chat card's first-run state: shown while no turn has been recorded.</summary>
    private Control? _chatEmpty;

    /// <summary>The reasoning switch: whether "thought" turns are shown and recorded at all.</summary>
    private CheckBox? _showThinking;

    /// <summary>
    /// The conversation card: the log, the empty state, and the compose row, as one card inside the
    /// play page's solo section.
    /// </summary>
    /// <remarks>
    /// The conversation used to be its own tab with a fixed footer; it is now the centre of the solo
    /// play page, because the conversation is where the model's decisions and results are read. The
    /// empty state is a card, not a line of grey text: a first-run player opening the panel got an
    /// empty box and no idea what to type. It disappears the moment a turn exists.
    ///
    /// There is no "let the AI act" switch: the conversation is read-only, and acting is what the
    /// auto-play and single-step controls are for. A message that itself asks for a move still
    /// releases that one turn -- see <c>PlayIntent</c> -- which is the phrase the hint names.
    /// </remarks>
    private Control BuildChatCard()
    {
        var column = UiFactory.Column();

        _chatEmpty = UiFactory.Card(
            Loc.T("和 AI 聊聊这局"),
            UiFactory.Wrapped(Loc.T("问它这手牌怎么打、这个遗物值不值得买、刚才那步为什么那么出。")),
            UiFactory.Wrapped(Loc.T("对话默认只读；要它动手，用上方的「开始自动游玩」或「单步」，或者明确说「帮我打」。")));
        column.AddChild(_chatEmpty);

        _chatLog = UiFactory.Rich();
        _chatLog.FitContent = false;
        _chatLog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _chatLog.CustomMinimumSize = new Vector2(0, 160);
        column.AddChild(_chatLog);

        var settings = AgentRuntime.Instance.Settings;
        _attachState = UiFactory.Check(Loc.T("附带当前状态"), settings.AttachStateInChat);
        _attachShot = UiFactory.Check(Loc.T("附带截图（视觉）"), settings.AttachScreenshotInChat);
        _showThinking = UiFactory.Check(Loc.T("显示思考内容"), settings.ShowThinkingInChat);
        _attachState.Toggled += _ => PersistChatFlags();
        _attachShot.Toggled += _ => PersistChatFlags();
        _showThinking.Toggled += on =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.ShowThinkingInChat = on;
            AgentRuntime.Instance.SaveSettings(next);
            // The switch decides what the log draws, so the log redraws on the spot rather than on the
            // next turn: a player who turns reasoning on and sees nothing happen reads it as broken.
            RefreshChatLog();
        };

        column.AddChild(UiFactory.Row(_attachState, _attachShot));
        column.AddChild(_showThinking);

        // The two dials a player reaches for mid-run live with the conversation, not in the settings
        // form: how hard the model thinks, and which language it answers in. Both write through to
        // settings immediately and take effect on the next turn.
        column.AddChild(BuildChatOptionsRow(settings));
        _chatInput = UiFactory.Multiline("", 52);
        _chatInput.PlaceholderText = Loc.T("和 AI 说这局怎么打；它会参考你的话做下一次决策。");
        _chatInput.CustomMinimumSize = new Vector2(UiFactory.MinReflowWidth, 52);
        column.AddChild(_chatInput);
        _sendButton = UiFactory.Button(Loc.T("发送"), () => _ = SendChatAsync(), UiFactory.ButtonKind.Primary);
        var clear = UiFactory.Button(Loc.T("清空"), () => AgentRuntime.Instance.ClearChat(), UiFactory.ButtonKind.Ghost);
        column.AddChild(UiFactory.Row(_sendButton, clear));

        return UiFactory.Card(Loc.T("对话"), column);
    }

    /// <summary>
    /// The chat card's option row: reply language and thinking intensity. Both are per-turn settings
    /// the player tunes while watching the model play, so they live beside the conversation rather
    /// than behind the settings form's advanced toggle.
    /// </summary>
    private Control BuildChatOptionsRow(STS2AIAgent.Config.AgentSettings settings)
    {
        var language = UiFactory.Combo();
        foreach (var (id, label) in new[]
        {
            ("auto", Loc.T("跟随玩家")),
            ("zh", "中文"),
            ("en", "English")
        })
        {
            language.AddItem(label);
            language.SetItemMetadata(language.ItemCount - 1, id);
        }

        SelectComboByMetadata(language, settings.ReplyLanguage);
        language.ItemSelected += index =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.ReplyLanguage = language.GetItemMetadata((int)index).AsString();
            AgentRuntime.Instance.SaveSettings(next);
        };

        var intensity = UiFactory.Combo();
        foreach (var item in new[] { "off", "low", "medium", "high" })
        {
            intensity.AddItem(item);
        }

        // Thinking intensity belongs to the main model now that one model chats and plays; the combo
        // edits that model's own field, which is what the loop reads on the next request.
        var mainModel = settings.FindModel(settings.ConversationModelId);
        SelectByText(intensity, mainModel?.ThinkingIntensity ?? settings.ThinkingIntensity);
        intensity.ItemSelected += index =>
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            var target = next.FindModel(next.ConversationModelId);
            if (target != null)
            {
                target.ThinkingIntensity = intensity.GetItemText((int)index);
                AgentRuntime.Instance.SaveSettings(next);
            }
        };

        var row = UiFactory.Row();
        row.AddChild(UiFactory.Wrapped(Loc.T("回复语言"), 12));
        row.AddChild(language);
        row.AddChild(UiFactory.Wrapped(Loc.T("思考强度"), 12));
        row.AddChild(intensity);
        return row;
    }

    private static void SelectComboByMetadata(OptionButton combo, string? id)
    {
        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (string.Equals(combo.GetItemMetadata(i).AsString(), id, StringComparison.OrdinalIgnoreCase))
            {
                combo.Selected = i;
                return;
            }
        }

        combo.Selected = 0;
    }

    /// <summary>
    /// Redraws the conversation from the runtime's snapshot and parks the scroll at the newest turn.
    /// </summary>
    /// <remarks>
    /// Its own method rather than a block inside the refresh pass: the 800ms panel tick redraws the
    /// conversation too, and the two callers would otherwise be two copies of this loop. The scroll
    /// is deferred because the freshly appended BBCode has not been laid out yet, so the bar's maximum
    /// still describes the previous frame -- assigning to it here would land short of the bottom.
    /// </remarks>
    private void RefreshChatLog()
    {
        if (_chatLog == null)
        {
            return;
        }

        var history = AgentRuntime.Instance.History;
        var showThinking = AgentRuntime.Instance.Settings.ShowThinkingInChat;
        if (_chatEmpty != null)
        {
            _chatEmpty.Visible = history.Count == 0;
        }

        _chatLog.Clear();

        // Two windows hide earlier turns: the runtime's history cap, and this log's smaller drawn
        // tail. Saying so is the difference between a log that starts mid-sentence and one the
        // player knows is a window.
        var trimmed = AgentRuntime.Instance.HistoryTrimmedCount;
        var start = Math.Max(0, history.Count - ChatTailLimit);
        var omitted = trimmed + start;
        if (omitted > 0)
        {
            _chatLog.AppendText(Muted(Loc.T("已省略更早的 {0} 条消息", omitted)) + "\n\n");
        }

        for (var index = start; index < history.Count; index++)
        {
            var turn = history[index];
            if (turn.Role == "thought")
            {
                if (showThinking)
                {
                    _chatLog.AppendText(FormatChatBubble(Loc.T("思考"), UiFactory.Muted, turn.Text, mutedBody: true));
                }

                continue;
            }

            if (turn.Role == "action")
            {
                _chatLog.AppendText(FormatChatBubble(Loc.T("动作"), UiFactory.Accent, turn.Text, mutedBody: false));
                continue;
            }

            _chatLog.AppendText(FormatTurn(turn.Role == "user" ? Loc.T("你") : Loc.T("助手"), turn.Text));
        }

        Callable.From(ScrollChatToBottom).CallDeferred();
    }

    /// <summary>
    /// Pins the conversation to its newest line. Deferred from <see cref="RefreshChatLog"/>, so it
    /// re-reads the control and checks it is still in the tree: a rebuild between the two frames frees
    /// the label this closure captured.
    /// </summary>
    private void ScrollChatToBottom()
    {
        var log = _chatLog;
        if (log == null || !log.IsInsideTree())
        {
            return;
        }

        var bar = log.GetVScrollBar();
        bar.Value = bar.MaxValue;
    }

    /// <summary>
    /// One non-conversational turn as the log draws it: a coloured role line, then the body, with any
    /// BBCode in either neutralised.
    /// </summary>
    private static string FormatChatBubble(string speaker, Color tone, string text, bool mutedBody)
    {
        var body = mutedBody ? Muted(text) : Escape(text);
        return "[color=#" + tone.ToHtml(false) + "][b]" + Escape(speaker) + "[/b][/color]\n" + body + "\n\n";
    }
}
