using System.Text.Json;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class CombatPowerPayloadTests
{
    public static void SerializesNativeCounterSeparatelyFromAmount()
    {
        var payload = new CombatPowerPayload
        {
            power_id = "HARDENED_SHELL_POWER",
            amount = 50,
            display_amount = 17,
        };
        using var json = JsonDocument.Parse(JsonHelper.Serialize(payload));

        Assert.Equal(50, json.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal(17, json.RootElement.GetProperty("display_amount").GetInt32());
    }

    public static void PreservesExhaustedNativeCounter()
    {
        var payload = new CombatPowerPayload { amount = 50, display_amount = 0 };
        using var json = JsonDocument.Parse(JsonHelper.Serialize(payload));

        Assert.Equal(0, json.RootElement.GetProperty("display_amount").GetInt32());
    }

    public static void KeepsUnavailableNativeCounterUnknown()
    {
        var payload = new CombatPowerPayload { amount = 50 };
        using var json = JsonDocument.Parse(JsonHelper.Serialize(payload));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("display_amount").ValueKind);
        Assert.Equal(50, json.RootElement.GetProperty("amount").GetInt32());
    }

    public static void StateReadsTheNativeCounterWithoutFallback()
    {
        // State construction requires Godot; keep the live wiring connected to
        // the separately tested payload and existing nullable native reader.
        var source = AgentSourceFixture.ReadStateService();
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(source, "BuildCreaturePowerPayloads"));

        Assert.Contains("vardisplayAmount=SafeReadNullableInt(()=>power.DisplayAmount);", body);
        Assert.Contains("display_amount=displayAmount,", body);
        Assert.False(body.Contains("displayAmount??", StringComparison.Ordinal));
    }
}
