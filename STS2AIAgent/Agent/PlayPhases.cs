using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The named stages of one play turn, as the status line shows them. A turn is a chain of waits
/// that used to leave the status on "requesting the model" from beginning to end; naming the stage
/// is what makes a stall point visible (and, with the elapsed suffix, measurable).
/// </summary>
internal static class PlayPhases
{
    public static string ReadingState => Loc.T("正在读取游戏状态…");

    public static string WaitingForGame => Loc.T("正在等待游戏可操作…");

    public static string AskingJev => Loc.T("正在请求 Jev 执行层…");

    public static string RequestingModel => Loc.T("正在请求模型…");

    public static string ExecutingAction => Loc.T("正在执行动作…");
}
