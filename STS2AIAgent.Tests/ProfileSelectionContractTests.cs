namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for selecting the native game profile through the
/// same game-thread API used by every other menu action.
/// </summary>
internal static class ProfileSelectionContractTests
{
    public static void NativeProfileIdentityAndSwitchAreWiredEndToEnd()
    {
        var actionSource = WithoutWhitespace(ReadSource("STS2AIAgent/Game/GameActionService.cs"));
        var stateSource = WithoutWhitespace(ReadSource("STS2AIAgent/Game/GameStateService.cs"));

        Assert.Contains("native_profile_id=SaveManager.Instance.CurrentProfileId", stateSource, StringComparison.Ordinal);
        Assert.Contains("names.Add(\"switch_profile\")", stateSource, StringComparison.Ordinal);
        Assert.Contains("\"switch_profile\"=>ExecuteSwitchProfileAsync(request)", actionSource, StringComparison.Ordinal);
        Assert.Contains("profileIdis<1or>3", actionSource, StringComparison.Ordinal);
        Assert.Contains("SaveManager.Instance.SwitchProfileId(profileId)", actionSource, StringComparison.Ordinal);
        Assert.Contains("SaveManager.Instance.InitPrefsData()", actionSource, StringComparison.Ordinal);
        Assert.Contains("SaveManager.Instance.InitProgressData()", actionSource, StringComparison.Ordinal);
        Assert.Contains("game.ReloadMainMenu()", actionSource, StringComparison.Ordinal);
        Assert.Contains("WaitForProfileSwitchAsync", actionSource, StringComparison.Ordinal);
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
}
