using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

internal sealed record PlayerFacingSnapshot
{
    public required FirstRunStatus FirstRun { get; init; }

    public required string PlayPhase { get; init; }

    public required bool PlayRunning { get; init; }

    public required string Status { get; init; }

    public required bool DualLaunching { get; init; }

    public required string DualStatus { get; init; }

    public required bool TeamControlPending { get; init; }

    public required string TeamControlStatus { get; init; }

    public required bool CompanionConnected { get; init; }

    public required bool CompanionProcessAlive { get; init; }

    public required bool CompanionProcessExited { get; init; }

    public required bool WaitingForGame { get; init; }

    public required bool WaitingForPlayer { get; init; }

    public required bool RequestingModel { get; init; }

    public required bool FinishingSubmittedAction { get; init; }

    public required string? StopKind { get; init; }

    public required string? StopDetail { get; init; }

    public required bool UsageKnown { get; init; }

    public required LlmUsage SessionUsage { get; init; }

    public required int SessionRequests { get; init; }

    public required string? BudgetReason { get; init; }

    public required bool IsCompanion { get; init; }
}

internal readonly record struct PlayerFacingView(
    string Kind,
    string Headline,
    string Detail,
    string NextAction,
    string? Technical);

// A completion callback belongs to both the task it observed and the runtime
// generation that created it.  A completed task may otherwise report after a
// newer automatic session has already started.
internal readonly record struct PlaySessionIdentity(long Generation, Task Task)
{
    public bool Matches(long generation, Task task) =>
        Generation == generation && ReferenceEquals(Task, task);
}

internal static class PlayerFacingSession
{
    /// <summary><see cref="PlayerFacingView.Kind"/> for a session that stopped at a budget cap.</summary>
    public const string BudgetKind = "budget";

    /// <summary>
    /// True when play stopped, or cannot start, for a reason the player has to deal with: a budget
    /// cap, a configuration or connection error, or a teammate window that went away.
    /// </summary>
    /// <remarks>
    /// The title bar and the edge tab only knew "playing" and "idle", so a run that stopped on an
    /// error looked exactly like one the player had paused -- and with the panel hidden, nothing on
    /// screen said the AI had stopped at all.
    /// </remarks>
    public static bool NeedsAttention(PlayerFacingView view) =>
        view.Kind is BudgetKind or "needs_error" or "companion_lost";

    internal static bool IsCurrentPlaySession(PlaySessionIdentity? current, PlaySessionIdentity observed)
    {
        return current is { } currentIdentity &&
            currentIdentity.Matches(observed.Generation, observed.Task);
    }

    internal static bool ShouldClearModelTestFailure(string? stopKind, string? stopRole, string role)
    {
        return (stopKind is "config" or "network") &&
            (stopRole == null || string.Equals(stopRole, role, StringComparison.OrdinalIgnoreCase));
    }

    public static PlayerFacingView Compose(PlayerFacingSnapshot s)
    {
        if (s.IsCompanion)
        {
            return ComposeCompanion(s);
        }

        if (s.CompanionProcessExited)
        {
            return new PlayerFacingView(
                "companion_lost",
                Loc.T("队友窗口已退出"),
                s.DualStatus,
                Loc.T("关闭残留窗口，回主菜单重新邀请。"),
                s.StopDetail);
        }

        if (s.DualLaunching)
        {
            return new PlayerFacingView(
                "pairing",
                Loc.T("正在组队"),
                s.DualStatus,
                Loc.T("等待队友窗口连接，请勿重复邀请。"),
                null);
        }

        if (s.BudgetReason != null)
        {
            var canReset = SessionBudgetLimits.CanResetSessionStats(s.PlayRunning, s.PlayPhase);
            return new PlayerFacingView(
                BudgetKind,
                Loc.T("已达到会话预算"),
                s.BudgetReason,
                SessionBudgetLimits.BudgetRecoveryNextAction(canReset),
                null);
        }

        if (s.StopKind == "config")
        {
            return new PlayerFacingView(
                "needs_error",
                Loc.T("配置有误，已停止"),
                s.StopDetail ?? s.Status,
                Loc.T("到「设置」修正端点、模型或 Key，测试通过后点「继续游玩」。"),
                s.StopDetail);
        }

        if (s.StopKind == "run_end")
        {
            return new PlayerFacingView(
                "run_ended",
                Loc.T("对局已结束"),
                s.StopDetail ?? Loc.T("已离开当前对局。"),
                Loc.T("新开一局后，再继续或重新邀请。"),
                null);
        }

        if (s.StopKind == "network")
        {
            return new PlayerFacingView(
                "needs_error",
                Loc.T("暂时连不上模型"),
                s.StopDetail ?? s.Status,
                Loc.T("检查网络或服务后点「继续游玩」。"),
                s.StopDetail);
        }

        if (s.StopKind == "failed")
        {
            return new PlayerFacingView(
                "needs_error",
                Loc.T("自动游玩已停止"),
                s.StopDetail ?? s.Status,
                Loc.T("看一眼局面，再点「继续游玩」。"),
                s.StopDetail);
        }

        if (s.TeamControlPending && s.PlayPhase == "stopping")
        {
            return new PlayerFacingView(
                "pausing",
                Loc.T("正在暂停"),
                s.TeamControlStatus,
                Loc.T("已提交的动作会先完成。"),
                null);
        }

        if (s.PlayPhase == "stopping" || s.FinishingSubmittedAction)
        {
            return new PlayerFacingView(
                "finishing_action",
                Loc.T("正在完成已提交的动作"),
                s.Status,
                Loc.T("请稍候，完成后显示已暂停。"),
                null);
        }

        if (s.PlayPhase == "paused" && s.CompanionConnected)
        {
            return new PlayerFacingView(
                "paused",
                Loc.T("队友已暂停"),
                s.TeamControlStatus,
                Loc.T("仍可聊天。准备好后点「继续游玩」。"),
                null);
        }

        if (!s.FirstRun.ReadyToInvite)
        {
            var kind = s.FirstRun.Phase == "failed" ? "needs_error" : "unconfigured";
            var next = s.FirstRun.Phase == "failed"
                ? Loc.T("到「设置」修正后重新测试。")
                : Loc.T("到「设置」配置并测试模型。");
            return new PlayerFacingView(kind, HeadlineForFirstRun(s.FirstRun), s.FirstRun.Hint, next, null);
        }

        // Inviting a teammate only makes sense before or after a session. While auto-play is running
        // this guard used to win over every running branch, so a solo session showed "可以邀请 AI 队友"
        // and never reported what the loop was actually doing.
        if (!s.CompanionConnected && !s.CompanionProcessAlive && !s.PlayRunning)
        {
            return new PlayerFacingView(
                "ready_to_invite",
                Loc.T("可以邀请 AI 队友"),
                s.FirstRun.Hint,
                Loc.T("回主菜单邀请 AI 队友。"),
                null);
        }

        if (s.WaitingForPlayer)
        {
            return new PlayerFacingView(
                "waiting_player",
                Loc.T("正在等你"),
                s.Status,
                Loc.T("请在你的窗口完成选择。"),
                null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView(
                "waiting_game",
                Loc.T("正在等游戏"),
                s.Status,
                Loc.T("动画结束后继续。"),
                null);
        }

        // Only a model round is "requesting the model". The earlier guards already cover stopping,
        // pausing and waiting, so a plain PlayRunning here means the loop is between rounds and the
        // player should see "队友正在行动" instead. The old extra clause made that branch dead code.
        if (s.RequestingModel)
        {
            return new PlayerFacingView(
                "requesting_model",
                Loc.T("正在请求模型"),
                s.Status,
                Loc.T("可点「暂停队友」，已发出的请求不会被取消。"),
                null);
        }

        if (s.PlayRunning)
        {
            return new PlayerFacingView(
                "running",
                Loc.T("队友正在行动"),
                s.Status,
                Loc.T("可随时暂停。"),
                null);
        }

        return new PlayerFacingView(
            "ready",
            Loc.T("队友已连接"),
            s.DualStatus,
            string.IsNullOrWhiteSpace(s.TeamControlStatus) ? Loc.T("可点「暂停队友」或聊天。") : s.TeamControlStatus,
            null);
    }

    public static string FormatUsage(bool known, LlmUsage usage, int requests)
    {
        var requestText = Loc.T("{0} 次请求", requests);
        if (!known)
        {
            return requests == 0
                ? Loc.T("暂无 Token · {0}", requestText)
                : Loc.T("Token 未知 · {0}", requestText);
        }

        return Loc.T("{0} Token · {3}",
            usage.TotalTokens.ToString("N0"),
            usage.PromptTokens.ToString("N0"),
            usage.CompletionTokens.ToString("N0"),
            requestText);
    }

    /// <summary>
    /// The usage block the overlay shows above the decision log: the same token/request line as
    /// <see cref="FormatUsage"/>, plus the budget reason when the session stopped at a cap. The
    /// reason is the one <see cref="Compose"/> already carries, so the overlay and the AI teammate
    /// tab cannot tell the player two different stories about the same cap.
    /// </summary>
    /// <remarks>
    /// A session with no usage reported reads as unknown, never as 0: "the service returned nothing"
    /// and "this session spent nothing" are different facts, and only one of them is a reason to
    /// keep playing.
    /// </remarks>
    public static string FormatUsageSummary(bool known, LlmUsage usage, int requests, PlayerFacingView facing)
    {
        var line = FormatUsage(known, usage, requests);
        return facing.Kind == BudgetKind ? line + "\n" + facing.Detail : line;
    }

    /// <summary>
    /// The current run's own spend, shown above the decision log next to the session totals.
    /// </summary>
    /// <remarks>
    /// A session can outlive a run, so a session total answers a different question from "what has
    /// this run cost". Token spend is unknown until a model reports usage, and it stays unknown here
    /// rather than reading as 0. A run with no decisions yet says so instead of showing "0 tokens",
    /// which would look like a run that played for free.
    /// </remarks>
    public static string FormatRunSpend(string? runId, int decisions, long tokens, bool tokensKnown)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Loc.T("尚未进入对局");
        }

        if (decisions == 0)
        {
            return Loc.T("暂无决策", runId);
        }

        var spend = tokensKnown
            ? Loc.T("{1} 次决策 · {2} Token", runId, decisions, tokens.ToString("N0"))
            : Loc.T("{1} 次决策 · Token 未知", runId, decisions);
        return spend;
    }

    /// <summary>
    /// The dual-layer split for the Jev panel: turns Jev actually executed versus turns the LLM
    /// finished after a failed attempt. Zero/zero stays a dash -- the panel has nothing to say until
    /// the first dual-layer turn, and "Jev 0 次 · 回退 0 次" would just be noise.
    /// </summary>
    public static string FormatJevRate(long jevAccepted, long fallbacks)
    {
        if (jevAccepted <= 0 && fallbacks <= 0)
        {
            return "-";
        }

        return Loc.T("快速判断 {0} 次 · LLM 接手 {1} 次", jevAccepted, fallbacks);
    }

    private static PlayerFacingView ComposeCompanion(PlayerFacingSnapshot s)
    {
        if (s.PlayPhase == "stopping")
        {
            return new PlayerFacingView("pausing", Loc.T("正在暂停"), s.Status, Loc.T("等待当前任务结束"), null);
        }

        if (s.PlayPhase == "paused")
        {
            return new PlayerFacingView("paused", Loc.T("已暂停自动游玩"), s.Status, Loc.T("点「继续游玩」后继续。"), null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView("waiting_game", Loc.T("正在等游戏"), s.Status, Loc.T("正常等待"), null);
        }

        return new PlayerFacingView("running", s.Status, s.Status, Loc.T("由主窗口控制"), null);
    }

    private static string HeadlineForFirstRun(FirstRunStatus first) => first.Phase switch
    {
        "failed" => Loc.T("游玩配置验证失败"),
        "filled_unverified" => Loc.T("配置尚未验证"),
        "verified" => Loc.T("可以邀请队友"),
        _ => Loc.T("还没有配好模型")
    };
}
