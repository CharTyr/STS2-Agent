using System.Text;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// Behavioural coverage for the shared serializer options: PascalCase names and indentation on the
/// wire, case-insensitive reads on the way back in. <c>JsonHelper.cs</c> uses only
/// <c>System.Text.Json</c>, so it compiles into this assembly without the game dependencies.
/// </summary>
internal static class JsonHelperTests
{
    public static void SerializationKeepsPascalCaseAndIndentation()
    {
        var json = JsonHelper.Serialize(new JsonHelperSample { PascalName = "Alpha", ItemCount = 3 });
        // Indentation newlines follow Environment.NewLine, so compare against a normalized copy.
        var normalized = json.Replace("\r\n", "\n");

        Assert.Contains("\"PascalName\"", normalized);
        Assert.False(
            normalized.Contains("\"pascalName\"", StringComparison.Ordinal),
            "PropertyNamingPolicy must stay null so field names keep their declared casing.");
        Assert.Contains("\n  \"PascalName\"", normalized);
        Assert.True(
            normalized.StartsWith("{\n", StringComparison.Ordinal),
            $"Expected an indented object, got: {normalized}");
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
