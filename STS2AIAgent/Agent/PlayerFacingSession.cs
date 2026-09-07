using STS2AIAgent.Config;
using STS2AIAgent.Llm;

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
                "队友窗口已退出",
                s.DualStatus,
                "关闭残留窗口后，回到主菜单再点「邀请 AI 队友」。",
                s.StopDetail);
        }

        if (s.DualLaunching)
        {
            return new PlayerFacingView(
                "pairing",
                "正在组队",
                s.DualStatus,
                "等待第二窗口连接。请勿重复点击邀请。",
                null);
        }

        if (s.BudgetReason != null)
        {
            return new PlayerFacingView(
                "budget",
                "已达到会话预算",
                s.BudgetReason,
                "在设置中提高上限或点重置统计后，再点「继续游玩」。",
                null);
        }

        if (s.StopKind == "config")
        {
            return new PlayerFacingView(
                "needs_error",
                "配置有误，已停止",
                s.StopDetail ?? s.Status,
                "打开「设置」修正端点、模型名或 Key，测试通过后再点「继续游玩」。不会自动重试。",
                s.StopDetail);
        }

        if (s.StopKind == "run_end")
        {
            return new PlayerFacingView(
                "run_ended",
                "对局已结束",
                s.StopDetail ?? "已离开当前对局，不会自动开新局。",
                "若要再打一局，先回到主菜单自行开局，再继续或重新邀请。",
                null);
        }

        if (s.StopKind == "network")
        {
            return new PlayerFacingView(
                "needs_error",
                "暂时连不上模型",
                s.StopDetail ?? s.Status,
                "检查网络或服务后，点「继续游玩」恢复。配置类错误不会无限重试。",
                s.StopDetail);
        }

        if (s.StopKind == "failed")
        {
            return new PlayerFacingView(
                "needs_error",
                "自动游玩已停止",
                s.StopDetail ?? s.Status,
                "查看当前局面后点「继续游玩」。",
                s.StopDetail);
        }

        if (s.TeamControlPending && s.PlayPhase == "stopping")
        {
            return new PlayerFacingView(
                "pausing",
                "正在暂停",
                s.TeamControlStatus,
                "已提交的动作会先完成，不会再派发新的游戏动作。",
                null);
        }

        if (s.PlayPhase == "stopping" || s.FinishingSubmittedAction)
        {
            return new PlayerFacingView(
                "finishing_action",
                "正在完成已提交的动作",
                s.Status,
                "请稍候。这不是已取消；完成后会显示已暂停。",
                null);
        }

        if (s.PlayPhase == "paused" && s.CompanionConnected)
        {
            return new PlayerFacingView(
                "paused",
                "队友已暂停",
                s.TeamControlStatus,
                "仍可聊天。确认配置与队友仍在后，点「继续游玩」。",
                null);
        }

        if (!s.FirstRun.ReadyToInvite)
        {
            var kind = s.FirstRun.Phase == "failed" ? "needs_error" : "unconfigured";
            var next = s.FirstRun.Phase == "failed"
                ? "打开「设置」，按失败用途修正后再测试。"
                : "打开「设置」：添加端点 → 添加模型并绑定 → 选择对话/游玩用途 → 测试 → 邀请队友。";
            return new PlayerFacingView(kind, HeadlineForFirstRun(s.FirstRun), s.FirstRun.Hint, next, null);
        }

        if (!s.CompanionConnected && !s.CompanionProcessAlive)
        {
            return new PlayerFacingView(
                "ready_to_invite",
                "可以邀请 AI 队友",
                s.FirstRun.Hint,
                "回到主菜单，点「邀请 AI 队友」。",
                null);
        }

        if (s.WaitingForPlayer)
        {
            return new PlayerFacingView(
                "waiting_player",
                "正在等你",
                s.Status,
                "在你的窗口完成选择。这是正常等待，不是故障。",
                null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView(
                "waiting_game",
                "正在等游戏",
                s.Status,
                "动画或转场结束后会继续。这是正常等待。",
                null);
        }

        if (s.RequestingModel || (s.PlayRunning && s.PlayPhase == "running"))
        {
            return new PlayerFacingView(
                "requesting_model",
                "正在请求模型",
                s.Status,
                "可点「暂停队友」。暂停不会取消已经发出的模型请求，但不会再派发新动作。",
                null);
        }

        if (s.PlayRunning)
        {
            return new PlayerFacingView(
                "running",
                "队友正在行动",
                s.Status,
                "可随时暂停。你只操作自己的角色。",
                null);
        }

        return new PlayerFacingView(
            "ready",
            "队友已连接",
            s.DualStatus,
            string.IsNullOrWhiteSpace(s.TeamControlStatus) ? "需要时点「暂停队友」或继续聊天。" : s.TeamControlStatus,
            null);
    }

    public static string FormatUsage(bool known, LlmUsage usage, int requests)
    {
        var requestText = $"请求：{requests} 次";
        if (!known)
        {
            return requests == 0
                ? "Token 消耗：尚无（未收到 usage） | " + requestText
                : "Token 消耗：未知（服务未返回 usage） | " + requestText;
        }

        return $"Token 消耗：{usage.TotalTokens:N0} (Prompt: {usage.PromptTokens:N0}, Completion: {usage.CompletionTokens:N0}) | {requestText}";
    }

    private static PlayerFacingView ComposeCompanion(PlayerFacingSnapshot s)
    {
        if (s.PlayPhase == "stopping")
        {
            return new PlayerFacingView("pausing", "正在暂停", s.Status, "等待当前任务结束。", null);
        }

        if (s.PlayPhase == "paused")
        {
            return new PlayerFacingView("paused", "已暂停自动游玩", s.Status, "主窗口点「继续游玩」后才会再行动。", null);
        }

        if (s.WaitingForGame)
        {
            return new PlayerFacingView("waiting_game", "正在等游戏", s.Status, "正常等待。", null);
        }

        return new PlayerFacingView("running", s.Status, s.Status, "由主窗口控制暂停与继续。", null);
    }

    private static string HeadlineForFirstRun(FirstRunStatus first) => first.Phase switch
    {
        "failed" => "游玩配置验证失败",
        "filled_unverified" => "配置尚未验证",
        "verified" => "可以邀请队友",
        _ => "还没有配好模型"
    };
}
