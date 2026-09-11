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
                Loc.T("关闭残留窗口后，回到主菜单再点「邀请 AI 队友」。"),
                s.StopDetail);
        }

        if (s.DualLaunching)
        {
            return new PlayerFacingView(
                "pairing",
                Loc.T("正在组队"),
                s.DualStatus,
                Loc.T("等待第二窗口连接。请勿重复点击邀请。"),
                null);
        }

        if (s.BudgetReason != null)
        {
            var canReset = SessionBudgetLimits.CanResetSessionStats(s.PlayRunning, s.PlayPhase);
            return new PlayerFacingView(
                "budget",
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
                Loc.T("打开「设置」修正端点、模型名或 Key，测试通过后再点「继续游玩」。不会自动重试。"),
                s.StopDetail);
        }

        if (s.StopKind == "run_end")
        {
            return new PlayerFacingView(
                "run_ended",
                Loc.T("对局已结束"),
                s.StopDetail ?? Loc.T("已离开当前对局，不会自动开新局。"),
                Loc.T("若要再打一局，先回到主菜单自行开局，再继续或重新邀请。"),
                null);
        }

        if (s.StopKind == "network")
        {
            return new PlayerFacingView(
                "needs_error",
                Loc.T("暂时连不上模型"),
                s.StopDetail ?? s.Status,
                Loc.T("检查网络或服务后，点「继续游玩」恢复。配置类错误不会无限重试。"),
                s.StopDetail);
        }

        if (s.StopKind == "failed")
        {
            return new PlayerFacingView(
                "needs_error",
                Loc.T("自动游玩已停止"),
                s.StopDetail ?? s.Status,
                Loc.T("查看当前局面后点「继续游玩」。"),
                s.StopDetail);
        }

        if (s.TeamControlPending && s.PlayPhase == "stopping")
        {
            return new PlayerFacingView(
                "pausing",
                Loc.T("正在暂停"),
                s.TeamControlStatus,
                Loc.T("已提交的动作会先完成，不会再派发新的游戏动作。"),
                null);
        }

        if (s.PlayPhase == "stopping" || s.FinishingSubmittedAction)
        {
            return new PlayerFacingView(
                "finishing_action",
                Loc.T("正在完成已提交的动作"),
                s.Status,
                Loc.T("请稍候。这不是已取消；完成后会显示已暂停。"),
                null);
        }

        if (s.PlayPhase == "paused" && s.CompanionConnected)
        {
            return new PlayerFacingView(
                "paused",
                Loc.T("队友已暂停"),
                s.TeamControlStatus,
                Loc.T("仍可聊天。确认配置与队友仍在后，点「继续游玩」。"),
                null);
        }

        if (!s.FirstRun.ReadyToInvite)
        {
            var kind = s.FirstRun.Phase == "failed" ? "needs_error" : "unconfigured";
            var next = s.FirstRun.Phase == "failed"
                ? Loc.T("打开「设置」，按失败用途修正后再测试。")
                : Loc.T("打开「设置」：添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试 → 邀请队友。");
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
                Loc.T("回到主菜单，点「邀请 AI 队友」。"),
                null);
        }

        if (s.WaitingForPlayer)
        {
            return new PlayerFacingView(
                "waiting_player",
                Loc.T("正在等你"),
                s.Status,
                Loc.T("在你的窗口完成选择。这是正常等待，不是故障。"),
                null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView(
                "waiting_game",
                Loc.T("正在等游戏"),
                s.Status,
                Loc.T("动画或转场结束后会继续。这是正常等待。"),
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
                Loc.T("可点「暂停队友」。暂停不会取消已经发出的模型请求，但不会再派发新动作。"),
                null);
        }

        if (s.PlayRunning)
        {
            return new PlayerFacingView(
                "running",
                Loc.T("队友正在行动"),
                s.Status,
                Loc.T("可随时暂停。你只操作自己的角色。"),
                null);
        }

        return new PlayerFacingView(
            "ready",
            Loc.T("队友已连接"),
            s.DualStatus,
            string.IsNullOrWhiteSpace(s.TeamControlStatus) ? Loc.T("需要时点「暂停队友」或继续聊天。") : s.TeamControlStatus,
            null);
    }

    public static string FormatUsage(bool known, LlmUsage usage, int requests)
    {
        var requestText = Loc.T("请求：{0} 次", requests);
        if (!known)
        {
            return requests == 0
                ? Loc.T("Token 消耗：尚无（未收到 usage） | {0}", requestText)
                : Loc.T("Token 消耗：未知（服务未返回 usage） | {0}", requestText);
        }

        return Loc.T("Token 消耗：{0} (Prompt: {1}, Completion: {2}) | {3}",
            usage.TotalTokens.ToString("N0"),
            usage.PromptTokens.ToString("N0"),
            usage.CompletionTokens.ToString("N0"),
            requestText);
    }

    private static PlayerFacingView ComposeCompanion(PlayerFacingSnapshot s)
    {
        if (s.PlayPhase == "stopping")
        {
            return new PlayerFacingView("pausing", Loc.T("正在暂停"), s.Status, Loc.T("等待当前任务结束。"), null);
        }

        if (s.PlayPhase == "paused")
        {
            return new PlayerFacingView("paused", Loc.T("已暂停自动游玩"), s.Status, Loc.T("主窗口点「继续游玩」后才会再行动。"), null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView("waiting_game", Loc.T("正在等游戏"), s.Status, Loc.T("正常等待。"), null);
        }

        return new PlayerFacingView("running", s.Status, s.Status, Loc.T("由主窗口控制暂停与继续。"), null);
    }

    private static string HeadlineForFirstRun(FirstRunStatus first) => first.Phase switch
    {
        "failed" => Loc.T("游玩配置验证失败"),
        "filled_unverified" => Loc.T("配置尚未验证"),
        "verified" => Loc.T("可以邀请队友"),
        _ => Loc.T("还没有配好模型")
    };
}
