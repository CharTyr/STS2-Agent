namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for <c>select_character</c> after the local player is ready. GameStateService
/// does not compile into this project (it needs the game assemblies), so the Ready backdoor is pinned
/// by reading the method body: availability must close on CanUnready / local isReady before it trusts
/// Godot button enablement or a populated lobby character list. The executor already re-checks
/// CanSelectCharacter; this file keeps that gate in place.
/// </summary>
internal static class CharacterSelectReadyContractTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";
    private const string SelectGuard = "if (CanSelectCharacter(currentScreen))";
    private const string UnreadyGuard = "if (CanUnready(currentScreen))";

    public static void ReadyGateClosesSelectCharacterBeforeButtonAndLobbyProbes()
    {
        var body = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(StatePath), "CanSelectCharacter");
        var flat = AgentSourceFixture.WithoutWhitespace(body);

        var unreadyGate = flat.IndexOf("if(CanUnready(currentScreen)){returnfalse;}", StringComparison.Ordinal);
        Assert.True(unreadyGate >= 0, "CanSelectCharacter must return false when CanUnready is true.");

        var lobbyReady = flat.IndexOf("!lobby.LocalPlayer.isReady", StringComparison.Ordinal);
        Assert.True(lobbyReady >= 0, "The multiplayer test lobby must not advertise select_character while LocalPlayer.isReady.");

        var characterReady = flat.IndexOf("characterSelect.Lobby.LocalPlayer.isReady", StringComparison.Ordinal);
        Assert.True(characterReady >= 0, "NCharacterSelectScreen must not advertise select_character while the local player is ready.");

        var buttons = flat.IndexOf("GetCharacterSelectButtons", StringComparison.Ordinal);
        Assert.True(buttons >= 0, "The unlocked-button probe must remain after the Ready gate.");
        Assert.True(
            unreadyGate < buttons && characterReady < buttons,
            "Ready checks must precede the Godot button enablement probe.");

        var lobbyCharacters = flat.IndexOf("GetMultiplayerLobbyCharacters()", StringComparison.Ordinal);
        Assert.True(lobbyCharacters >= 0, "The lobby character list probe must remain.");
        Assert.True(
            unreadyGate < lobbyCharacters && lobbyReady < lobbyCharacters,
            "Lobby Ready checks must precede the populated character-list probe.");
    }

    public static void AdvertisingStaysBehindTheProbeAndUnreadyStaysIndependent()
    {
        var state = AgentSourceFixture.Read(StatePath);

        var names = AgentSourceFixture.MethodBody(state, "BuildAvailableActionNames");
        Assert.Contains(SelectGuard, names, StringComparison.Ordinal);
        Assert.Contains("names.Add(" + Quote("select_character") + ")", names, StringComparison.Ordinal);
        Assert.Contains(UnreadyGuard, names, StringComparison.Ordinal);
        Assert.Contains("names.Add(" + Quote("unready") + ")", names, StringComparison.Ordinal);

        var descriptors = AgentSourceFixture.MethodBody(state, "BuildAvailableActionsPayload");
        Assert.Contains(SelectGuard, descriptors, StringComparison.Ordinal);
        Assert.Contains("name = " + Quote("select_character"), descriptors, StringComparison.Ordinal);
        Assert.Contains(UnreadyGuard, descriptors, StringComparison.Ordinal);
        Assert.Contains("name = " + Quote("unready"), descriptors, StringComparison.Ordinal);

        var namesFlat = AgentSourceFixture.WithoutWhitespace(names);
        var unreadyName = namesFlat.IndexOf("if(CanUnready(currentScreen)){names.Add(" + Quote("unready") + ");}", StringComparison.Ordinal);
        var selectName = namesFlat.IndexOf("if(CanSelectCharacter(currentScreen)){names.Add(" + Quote("select_character") + ");}", StringComparison.Ordinal);
        Assert.True(unreadyName >= 0 && selectName >= 0, "unready and select_character must stay independently advertised.");
        Assert.True(unreadyName != selectName, "unready must not be folded into the select_character gate.");
    }

    public static void ExecutorStillUsesCanSelectCharacter()
    {
        var action = AgentSourceFixture.Read(ActionPath);
        var characterSelect = AgentSourceFixture.MethodBody(action, "ExecuteSelectCharacterAsync");
        Assert.Contains("GameStateService.CanSelectCharacter(currentScreen)", characterSelect, StringComparison.Ordinal);

        var lobby = AgentSourceFixture.MethodBody(action, "ExecuteSelectMultiplayerLobbyCharacterAsync");
        Assert.Contains("GameStateService.CanSelectCharacter(", lobby, StringComparison.Ordinal);
    }

    private static string Quote(string value) => "\"" + value + "\"";
}
