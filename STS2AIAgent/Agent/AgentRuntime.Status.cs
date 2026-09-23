using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The status line's writers: the settled status, the in-flight model request, and the per-phase
/// elapsed clock the play page renders.
/// </summary>
/// <remarks>
/// Its own partial because of what these members touch: <c>_status</c>, <c>_requestingModelStatus</c>
/// and the phase clock, all read by the overlay's status line and nothing in the play loop's
/// decision path. The phase clock exists because a turn is a chain of waits that each used to leave
/// the status on the previous stage's text, so a stall was indistinguishable from a slow answer.
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>When the current in-flight phase started, so the status line can show elapsed time.</summary>
    private DateTimeOffset _phaseStartedAt;

    private void SetStatus(string status)
    {
        _status = status;
        _requestingModelStatus = false;
        // A settled status ends the phase clock with it: otherwise the elapsed counter would keep
        // growing beside a line that is no longer in flight.
        _phaseStartedAt = default;
        RaiseChanged();
    }

    // The status line is the only player-visible record that a model request is in flight, and the
    // chat path never sets _requestingModel, so the flag is kept next to the text it belongs to.
    private void SetRequestingModelStatus()
    {
        // A new request starts here, so the previous one's streamed reasoning is over: without this a
        // partial left behind by a canceled chat turn would reappear beside the next request's text.
        ClearLiveThought();
        _status = Loc.T("正在请求模型…");
        _requestingModelStatus = true;
        _phaseStartedAt = DateTimeOffset.UtcNow;
        RaiseChanged();
    }

    /// <summary>
    /// Reports which stage of a turn the loop is in ("reading state", "waiting for the game",
    /// "requesting the model", "asking Jev", "executing the action"). The elapsed suffix makes a
    /// stuck stage visible as a growing counter rather than frozen text.
    /// </summary>
    /// <remarks>
    /// Deliberately does not touch <c>_requestingModelStatus</c>: that flag feeds the player-facing
    /// "requesting the model" headline, and a turn reading state or waiting for the game is doing
    /// neither. The phase text itself is the status line; the elapsed counter rides
    /// <c>_phaseStartedAt</c> alone.
    /// </remarks>
    private void SetPlayPhase(string phase)
    {
        _phaseStartedAt = DateTimeOffset.UtcNow;
        _status = phase;
        RaiseChanged();
    }

    /// <summary>The status text with the current phase's elapsed time appended while a turn runs.</summary>
    public string StatusWithElapsed
    {
        get
        {
            var status = Status;
            if (_phaseStartedAt == default)
            {
                return status;
            }

            var elapsed = DateTimeOffset.UtcNow - _phaseStartedAt;
            return elapsed.TotalSeconds < 2
                ? status
                : Loc.T("{0}（已等待 {1} 秒）", status, (int)elapsed.TotalSeconds);
        }
    }
}
