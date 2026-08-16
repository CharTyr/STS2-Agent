namespace STS2AIAgent.Agent;

internal static class PlayIntent
{
    private static readonly string[] Phrases =
    {
        "帮我打",
        "帮我出",
        "帮我玩",
        "帮我操作",
        "帮我行动",
        "帮我执行",
        "替我打",
        "替我出",
        "请出牌",
        "自动打",
        "你来打",
        "play for me",
        "play the game",
        "take a turn",
        "take the turn",
        "make a move",
        "play a card",
        "act for me"
    };

    public static bool Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.Trim().ToLowerInvariant();
        foreach (var phrase in Phrases)
        {
            if (lower.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
