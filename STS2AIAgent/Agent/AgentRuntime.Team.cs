using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

/// <summary>
/// The AI teammate: what the human says to it, what it says back, and what its character is doing.
/// </summary>
/// <remarks>
/// Split out of <c>AgentRuntime.cs</c> when that file crossed its size budget. It is one subject --
/// the other instance of this mod -- and it is the only part of the runtime that talks to a process
/// rather than to the game.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>
    /// The teammate's live game summary, or null when it is unavailable. Read from a cached snapshot
    /// rather than fetched on read: the overlay asks for it on its frame tick, and an HTTP call there
    /// would run on the game thread.
    /// </summary>
    public string? TeammateLiveStatus => _teammateLiveStatus;

    /// <summary>
    /// Asks for a fresh teammate summary, at most once every two seconds.
    /// </summary>
    /// <remarks>
    /// Throttled and fire-and-forget because its caller is the overlay's tick: a companion that is
    /// mid-restart answers nothing, and that is a normal state rather than an error to surface. The
    /// identity check inside the connection still runs first, so a replacement process on a reused
    /// port is never reported as the teammate.
    /// </remarks>
    public void RequestTeammateStatusRefresh()
    {
        var connection = LocalDualInstanceLauncher.Connection;
        if (connection == null)
        {
            SetTeammateLiveStatus(null);
            return;
        }

        var now = Environment.TickCount64;
        if (_teammateStatusRefreshing || now - _teammateStatusAtMs < TeammateStatusRefreshMs)
        {
            return;
        }

        _teammateStatusRefreshing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var stateJson = await connection.TryReadStateAsync(CancellationToken.None);
                SetTeammateLiveStatus(TeammateStatus.Parse(stateJson).Describe());
            }
            finally
            {
                _teammateStatusAtMs = Environment.TickCount64;
                _teammateStatusRefreshing = false;
            }
        });
    }

    private void SetTeammateLiveStatus(string? text)
    {
        if (string.Equals(_teammateLiveStatus, text, StringComparison.Ordinal))
        {
            return;
        }

        _teammateLiveStatus = text;
        RaiseChanged();
    }

    public async Task SendTeamMessageAsync(string text, CancellationToken cancellationToken)    {
        if (!await _teamMessageGate.WaitAsync(0, cancellationToken)) return;
        _teamMessagePending = true;
        try
        {
            if (_dualLaunching) throw new InvalidOperationException(Loc.T("正在组队，请等待连接完成后发送消息。"));
            var connection = LocalDualInstanceLauncher.Connection
                ?? throw new InvalidOperationException(Loc.T("请先邀请 AI 队友。此处消息只发送给本次邀请的队友。"));
            _teamConversation.Add("user", text);
            _teamStatus = Loc.T("消息正在送往队友；若它正在行动，会在本次行动完成后回复。");
            RaiseChanged();
            var reply = await connection.SendMessageAsync(text, intent: null, cancellationToken);
            _teamConversation.Add("assistant", reply.Length > TeamConversation.MaxMessageLength ? reply[..TeamConversation.MaxMessageLength] : reply);
            _teamStatus = Loc.T("队友已回复。你的建议会作为后续决策的参考。");
        }
        catch (Exception ex)
        {
            _teamStatus = Loc.T("队伍消息未确认完成：{0}", ex.Message);
        }
        finally
        {
            _teamMessagePending = false;
            _teamMessageGate.Release();
            RaiseChanged();
        }
    }

    public async Task<string> ReplyToTeammateAsync(string text, TeamIntent? intent, CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion can receive team messages.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        cancellationToken = deadline.Token;
        // Share the turn gate with gameplay, but do not require autoplay to be
        // stopped: this request only reads state and adds conversational context.
        string? budgetBlocked;
        lock (_gate)
        {
            budgetBlocked = _budgetGuard.CheckBudget();
        }

        if (budgetBlocked != null)
        {
            throw new InvalidOperationException(budgetBlocked);
        }

        await _turnGate.WaitAsync(cancellationToken);
        try
        {
            var previous = _teamConversation.Snapshot();
            _teamConversation.Add("user", text, intent);
            var result = await _loop.ChatAsync(text, previous, new ChatOptions
            {
                TeammateConversation = true,
                AttachState = true
            }, cancellationToken);
            AccountTurn(result, recordBudget: true);
            if (result.Error != null)
            {
                RaiseChanged();
                throw new InvalidOperationException(result.Error);
            }
            if (string.IsNullOrWhiteSpace(result.AssistantText))
            {
                RaiseChanged();
                throw new InvalidOperationException(Loc.T("队友未返回文本回复；建议已记录供后续决策参考。"));
            }
            var reply = result.AssistantText;
            if (reply.Length > TeamConversation.MaxMessageLength) reply = reply[..TeamConversation.MaxMessageLength];
            _teamConversation.Add("assistant", reply);
            RaiseChanged();
            return reply;
        }
        finally
        {
            _turnGate.Release();
        }
    }
}
