namespace STS2AIAgent.Tests;

/// <summary>
/// Registration of the 2026-10-28 experience-overhaul tests, kept in one place so
/// <see cref="TestRunner"/> gains a single line instead of one per test.
/// </summary>
internal static class ExperienceTestCatalog
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("Experience.Settings.DefaultsAreOn", () => Task.Run(ExperienceSettingsTests.DefaultsAreOn));
        yield return ("Experience.Settings.LegacyFileGetsTheNewDefaults", () => Task.Run(ExperienceSettingsTests.LegacyFileGetsTheNewDefaults));
        yield return ("Experience.Settings.JunkIsNormalised", () => Task.Run(ExperienceSettingsTests.JunkIsNormalised));
        yield return ("Experience.Settings.CloneCarriesEveryNonDefault", () => Task.Run(ExperienceSettingsTests.CloneCarriesEveryNonDefault));
        yield return ("Experience.Settings.TeammateGetsPlaySwitchesButNotLocalOnes", () => Task.Run(ExperienceSettingsTests.TeammateGetsPlaySwitchesButNotLocalOnes));
        yield return ("Experience.Settings.TeammateRejectsAnUnknownSpeed", () => Task.Run(ExperienceSettingsTests.TeammateRejectsAnUnknownSpeed));
    }
}
