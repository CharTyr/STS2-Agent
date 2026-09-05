namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for Godot deck-grid selections. These screens cannot be
/// instantiated in the lightweight test process, so the tests verify that the
/// native private selection state is exposed and consumed by the action settle path.
/// </summary>
internal static class DeckSelectionContractTests
{
    public static void DeckGridPayloadReportsNativeSelectionProgress()
    {
        var rawStateSource = ReadSource(
            "STS2AIAgent/Game/GameStateService.cs");
        var stateSource = WithoutWhitespace(rawStateSource);
        var payloadBody = WithoutWhitespace(
            MethodBody(rawStateSource, "BuildSelectionPayload"));

        Assert.Contains("NDeckCardSelectScreen", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"_prefs\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"_selectedCards\"", stateSource, StringComparison.Ordinal);
        Assert.Contains("selectedCount++", stateSource, StringComparison.Ordinal);
        Assert.Contains(
            "selected_count=hasCombatHandSelection?combatHandSelection.SelectedCount:hasDeckCardSelection?deckCardSelection.SelectedCount:0",
            payloadBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "min_select=hasCombatHandSelection?combatHandSelection.MinSelect:hasDeckCardSelection?deckCardSelection.MinSelect:1",
            payloadBody,
            StringComparison.Ordinal);
    }

    public static void DeckGridClickSettlesInEitherDirectionBeforeConfirming()
    {
        var rawActionSource = ReadSource(
            "STS2AIAgent/Game/GameActionService.cs");
        var selectBody = WithoutWhitespace(
            MethodBody(rawActionSource, "ExecuteSelectDeckCardAsync"));
        var settleBody = WithoutWhitespace(
            MethodBody(
                rawActionSource, "SettleDeckCardSelectionClickAsync"));

        Assert.Contains("SettleDeckCardSelectionClickAsync", selectBody, StringComparison.Ordinal);
        Assert.Contains(
            "metadata.SelectedCount==previousSelectedCount",
            settleBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(),screen)",
            settleBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "metadata.SelectedCount<previousSelectedCount",
            settleBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "metadata.SelectedCount<metadata.MinSelect",
            settleBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "metadata.SelectedCount<metadata.MaxSelect",
            settleBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConfirmDeckSelectionAsync(screen,remaining)",
            settleBody,
            StringComparison.Ordinal);
        var stateSource = WithoutWhitespace(ReadSource("STS2AIAgent/Game/GameStateService.cs"));
        Assert.Contains("IsCardSelected(currentScreen,holder.CardModel!)", stateSource, StringComparison.Ordinal);
        Assert.Contains("selected=selected", stateSource, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }
        }

        throw new FileNotFoundException($"Could not locate source file: {relativePath}");
    }

    private static string WithoutWhitespace(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static string MethodBody(string source, string methodName)
    {
        var nameIndex = source.LastIndexOf($" {methodName}(", StringComparison.Ordinal);
        var openBrace = nameIndex < 0 ? -1 : source.IndexOf('{', nameIndex);
        if (openBrace < 0)
        {
            throw new InvalidOperationException($"Method body is missing: {methodName}");
        }

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[openBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method body is unterminated: {methodName}");
    }
}
