using System.Globalization;
using System.Text.Json;

namespace STS2AIAgent.Agent;

// The one reading of the `timeout_seconds` tool argument, shared by the in-game agent and the
// native MCP surface. A model or client may send the number as a JSON string, so the string form is
// parsed with the invariant culture (a comma-decimal Windows locale would otherwise turn "1.5" into
// 15), and a non-finite value ("NaN", "Infinity") falls back to the default instead of reaching
// TimeSpan.FromSeconds, which throws on NaN.
internal static class ToolTimeout
{
    public const double DefaultSeconds = 20;
    public const double MinSeconds = 1;
    public const double MaxSeconds = 120;

    public static double ReadSeconds(JsonElement arguments)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.TryGetProperty("timeout_seconds", out var value)
            ? FromValue(value)
            : DefaultSeconds;
    }

    /// <summary>The clamped seconds for one <c>timeout_seconds</c> value, however the client typed it.</summary>
    public static double FromValue(JsonElement value)
    {
        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => DefaultSeconds
        };

        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            seconds = DefaultSeconds;
        }

        return Math.Clamp(seconds, MinSeconds, MaxSeconds);
    }
}
