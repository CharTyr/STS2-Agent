using System.Text.RegularExpressions;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class EventOptionLocalizationTests
{
    private static readonly IReadOnlyDictionary<string, object> EventVariables =
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Relic"] = "Anchor",
            ["Gold"] = 75,
            ["HpLoss"] = 12,
            ["SmallChestGold"] = 40,
            ["LargeChestGold"] = 120,
            ["MaxHp"] = 8,
            ["Damage"] = 15,
            ["Heal"] = 24,
            ["FromCardChoiceCount"] = 3,
            ["CardChoiceCount"] = 1,
            ["RipHpLoss"] = 9
        };

    public static void AddsEventVariablesBeforeFormatting()
    {
        var descriptions = new[]
        {
            "Obtain {Relic}, gain {Gold}, lose {HpLoss} HP.",
            "Open chests containing {SmallChestGold} or {LargeChestGold} Gold.",
            "Lose {MaxHp} Max HP, take {Damage}, or heal {Heal}.",
            "Choose {CardChoiceCount} from {FromCardChoiceCount}; ripping costs {RipHpLoss} HP."
        };

        foreach (var description in descriptions)
        {
            var locString = new FakeLocString(description);

            var formatted = EventOptionLocalization.Format(
                locString,
                AddEventVariables,
                FormatOrThrowForMissingVariable);

            Assert.False(formatted.Contains('{', StringComparison.Ordinal));
        }
    }

    public static void MissingLocStringReturnsEmpty()
    {
        var addCalled = false;
        var formatCalled = false;

        var formatted = EventOptionLocalization.Format<FakeLocString>(
            null,
            _ => addCalled = true,
            _ =>
            {
                formatCalled = true;
                return "unexpected";
            });

        Assert.Equal(string.Empty, formatted);
        Assert.False(addCalled);
        Assert.False(formatCalled);
    }

    public static void FormatsSignatureFieldsWithEventVariables()
    {
        var title = new FakeLocString("Gain {Gold} Gold");
        var description = new FakeLocString("Lose {HpLoss} HP");

        var signature = string.Join(
            ":",
            EventOptionLocalization.Format(title, AddEventVariables, FormatOrThrowForMissingVariable),
            EventOptionLocalization.Format(description, AddEventVariables, FormatOrThrowForMissingVariable));

        Assert.Equal("Gain 75 Gold:Lose 12 HP", signature);
    }

    private static void AddEventVariables(FakeLocString locString)
    {
        foreach (var (name, value) in EventVariables)
        {
            locString.Variables[name] = value;
        }
    }

    private static string FormatOrThrowForMissingVariable(FakeLocString locString)
    {
        return Regex.Replace(
            locString.RawText,
            "\\{(?<name>[^{}]+)\\}",
            match =>
            {
                var name = match.Groups["name"].Value;
                if (!locString.Variables.TryGetValue(name, out var value))
                {
                    throw new InvalidOperationException($"Missing localization variable: {name}");
                }

                return value.ToString() ?? string.Empty;
            });
    }

    private sealed class FakeLocString(string rawText)
    {
        public string RawText { get; } = rawText;

        public Dictionary<string, object> Variables { get; } = new(StringComparer.Ordinal);
    }
}
