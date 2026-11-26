using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

internal sealed record TeamQuickCommand(
    string Id,
    string Label,
    string Message,
    TeamIntent? Intent);

/// <summary>
/// One-click tactical signals for the host's co-op chat panel so players can send structured
/// <see cref="TeamIntent"/> messages mid-fight without typing long sentences.
/// </summary>
internal static class TeamQuickCommands
{
    public static IReadOnlyList<TeamQuickCommand> BuildAll() => new[]
    {
        new TeamQuickCommand(
            "focus_0",
            Loc.T("我打 0 号（你打其他）"),
            Loc.T("这回合我来打 0 号敌人，请优先处理其他目标，避免重复溢出。"),
            TeamIntent.ForFocusFire(0)),
        new TeamQuickCommand(
            "target_0",
            Loc.T("一起集火 0 号"),
            Loc.T("这回合优先集火 0 号敌人，尽量一起先秒掉它。"),
            TeamIntent.ForTargetAnnounce(0)),
        new TeamQuickCommand(
            "play_safe",
            Loc.T("本回合优先防御"),
            Loc.T("这回合敌人伤害高，请优先叠格挡保血，必要时喝防御/回血药水。"),
            null),
        new TeamQuickCommand(
            "take_elites",
            Loc.T("血量健康打精英"),
            Loc.T("我们当前血量健康，接下来选路优先考虑精英和遗物收益。"),
            null)
    };
}
