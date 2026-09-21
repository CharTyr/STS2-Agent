using System.Text;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// Behavioural coverage for the shared serializer options: PascalCase names on the wire, no layout,
/// case-insensitive reads on the way back in. <c>JsonHelper.cs</c> uses only
/// <c>System.Text.Json</c>, so it compiles into this assembly without the game dependencies.
/// </summary>
internal static class JsonHelperTests
{
    public static void SerializationKeepsPascalCaseAndDropsLayout()
    {
        var json = JsonHelper.Serialize(new JsonHelperSample { PascalName = "Alpha", ItemCount = 3 });

        Assert.Contains("\"PascalName\"", json);
        Assert.False(
            json.Contains("\"pascalName\"", StringComparison.Ordinal),
            "PropertyNamingPolicy must stay null so field names keep their declared casing.");

        // Indentation was 35.6% of the bytes across the docs' response examples (44% on the nested
        // combat payload), paid as tokens on every state read by a client that parses the body and
        // never looks at it. Compact is the contract now, so a future edit that turns it back on has
        // to argue with this assertion rather than with a comment.
        Assert.False(
            json.Contains('\n'),
            $"Responses must carry no layout, got: {json}");
        Assert.False(
            json.Contains(": ", StringComparison.Ordinal),
            $"Responses must not pad name-value separators, got: {json}");
        Assert.Contains("\"PascalName\":\"Alpha\"", json);
        Assert.Contains("\"ItemCount\":3", json);
    }

    public static void DeserializationIgnoresCase()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"pascalname\":\"Bravo\",\"itemcount\":7}"));
        var value = JsonHelper.DeserializeAsync<JsonHelperSample>(stream).GetAwaiter().GetResult();

        Assert.NotNull(value);
        Assert.Equal("Bravo", value!.PascalName);
        Assert.Equal(7, value.ItemCount);
    }
}

internal sealed class JsonHelperSample
{
    public string PascalName { get; set; } = string.Empty;

    public int ItemCount { get; set; }
}
