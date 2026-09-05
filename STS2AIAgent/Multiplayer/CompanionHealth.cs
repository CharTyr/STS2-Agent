using System.Text.Json;

namespace STS2AIAgent.Multiplayer;

internal static class CompanionHealth
{
    public static bool IsExpectedProcess(string json, int expectedPort, int expectedPid)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                HasString(data, "service", "sts2-ai-agent") &&
                HasString(data, "status", "ready") &&
                HasString(data, "instance_role", "companion") &&
                HasInt(data, "api_port", expectedPort) &&
                HasInt(data, "process_id", expectedPid);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasString(JsonElement data, string name, string expected)
    {
        return data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() == expected;
    }

    private static bool HasInt(JsonElement data, string name, int expected)
    {
        return data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) && number == expected;
    }
}
