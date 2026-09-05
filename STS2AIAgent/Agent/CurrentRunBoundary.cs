using System.Text.Json;

namespace STS2AIAgent.Agent;

// Scoped to one automatic session. Lobby setup is allowed until the first run is observed.
internal sealed class CurrentRunBoundary
{
    private bool _enteredRun;
    private string? _seed;

    public void Check(string stateJson)
    {
        using var document = JsonDocument.Parse(stateJson);
        var state = document.RootElement;
        var screen = ReadString(state, "screen");
        var phase = state.TryGetProperty("session", out var session) ? ReadString(session, "phase") : null;
        var seed = ReadString(state, "run_id");
        if (_enteredRun && (screen == "MAIN_MENU" || phase is "character_select" or "multiplayer_lobby"))
            throw new AutoPlayStoppedException("当前局已离开，自动游玩已停止。开始另一局需要手动继续。");

        // Unlock screens may outlive RunState; let the native unlock queue finish.
        if (phase == "run")
        {
            if (_seed != null && seed != null && seed != "run_unknown" && seed != _seed)
                throw new AutoPlayStoppedException("检测到对局标识变化，已停止自动游玩。请确认当前局后再继续。");
            _enteredRun = true;
            if (seed != "run_unknown") _seed ??= seed;
        }
        else if (screen is "GAME_OVER" or "UNLOCK")
        {
            _enteredRun = true;
        }
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;
}
