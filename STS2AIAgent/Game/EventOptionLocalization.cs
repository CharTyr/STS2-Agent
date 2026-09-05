namespace STS2AIAgent.Game;

internal static class EventOptionLocalization
{
    public static string Format<TLocString>(
        TLocString? locString,
        Action<TLocString> addEventVariables,
        Func<TLocString, string> getFormattedText)
        where TLocString : class
    {
        if (locString == null)
        {
            return string.Empty;
        }

        addEventVariables(locString);
        return getFormattedText(locString);
    }
}
