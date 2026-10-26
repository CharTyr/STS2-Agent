using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// run.players[] inventories: a populated read keeps every item, a failed read is null rather than
/// an empty inventory, and the failure is logged once per interval with the count held back.
/// </summary>
internal static class PartyInventoryReaderTests
{
    private sealed record Relic(string Id, string Name, bool Melted);

    private static readonly DateTime Start = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    public static void APopulatedInventoryKeepsEveryItemAndValue()
    {
        var reader = new PartyInventoryReader();
        var warnings = new List<string>();
        var relics = new[] { new Relic("BURNING_BLOOD", "Burning Blood", false), new Relic("ANCHOR", "Anchor", true) };

        var payload = reader.Read("relics", () => relics, relic => new PartyRelicPayloadProbe(relic.Id, relic.Name, relic.Melted), Start, warnings.Add);

        Assert.True(payload != null, "a readable inventory must not be null");
        Assert.Equal(2, payload!.Length);
        Assert.Equal("BURNING_BLOOD", payload[0].RelicId);
        Assert.Equal("Anchor", payload[1].Name);
        Assert.True(payload[1].IsMelted, "the melted flag must survive the read");
        Assert.Equal(0, warnings.Count);
    }

    public static void AnEmptyInventoryIsAnEmptyArrayNotNull()
    {
        var payload = new PartyInventoryReader().Read("potions", () => Array.Empty<Relic>(), relic => relic.Id, Start, _ => { });
        Assert.True(payload != null && payload.Length == 0, "holding nothing is an empty array");
    }

    public static void AFailedReadIsNullAndWarnsOncePerInterval()
    {
        var reader = new PartyInventoryReader();
        var warnings = new List<string>();
        IEnumerable<Relic> Broken() => throw new InvalidOperationException("relic list not ready");

        Assert.True(reader.Read("relics", Broken, relic => relic.Id, Start, warnings.Add) == null, "a failed read is null");
        Assert.True(reader.Read("relics", Broken, relic => relic.Id, Start.AddSeconds(5), warnings.Add) == null, "still null");
        Assert.Equal(1, warnings.Count);
        Assert.Contains("run.players[].relics is null", warnings[0], StringComparison.Ordinal);
        Assert.Contains("relic list not ready", warnings[0], StringComparison.Ordinal);

        // Another part has its own throttle.
        reader.Read("deck", Broken, relic => relic.Id, Start.AddSeconds(6), warnings.Add);
        Assert.Equal(2, warnings.Count);

        // After the interval the next warning reports what it held back.
        reader.Read("relics", Broken, relic => relic.Id, Start + PartyInventoryReader.WarningInterval + TimeSpan.FromSeconds(1), warnings.Add);
        Assert.Equal(3, warnings.Count);
        Assert.Contains("1 more failure(s) since the last warning", warnings[2], StringComparison.Ordinal);
    }

    public static void TheBuildersGoThroughTheReader()
    {
        var state = AgentSourceFixture.ReadStateService();
        Assert.Contains("PartyInventoryReader.Instance.Read(", state, StringComparison.Ordinal);
        foreach (var part in new[] { "\"deck\"", "\"relics\"", "\"potions\"" })
        {
            Assert.Contains(part + ",", state, StringComparison.Ordinal);
        }

        Assert.Contains("is_melted = relic.IsMelted", state, StringComparison.Ordinal);
    }

    private sealed record PartyRelicPayloadProbe(string RelicId, string Name, bool IsMelted);
}
