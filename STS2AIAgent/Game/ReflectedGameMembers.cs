using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;

namespace STS2AIAgent.Game;

/// <summary>What the mod reads out of the game that the compiler cannot check for it.</summary>
/// <remarks>
/// Everything else this mod touches is compile-checked: the project references the game's
/// <c>sts2.dll</c>, so a renamed public type or member turns the build red. The exception is the
/// private members below, which are found by name at runtime. Those fail differently -- and far
/// worse: <c>GetField</c> returns null, the call site falls back to a default, and an agent is told
/// <c>max_players: 0</c> with no way to tell that from a lobby that really holds nobody.
///
/// Slay the Spire 2 is in early access. A private field gets renamed in a patch and the mod keeps
/// answering, confidently, with numbers it invented. This registry is how that becomes visible:
/// every entry is resolved once at startup and the misses are reported on <c>GET /health</c>.
///
/// The declaring type is a <c>typeof</c>, never a string, so getting a type wrong is a compile
/// error and only the member name -- the part that actually rots -- is text.
/// </remarks>
internal static class ReflectedGameMembers
{
    internal enum MemberKind
    {
        Field,
        Method,
    }

    /// <summary>One private game member the mod reads, and what stops working without it.</summary>
    internal sealed record Entry(Type DeclaringType, string MemberName, MemberKind Kind, bool Static, string Feature)
    {
        public string Id => $"{DeclaringType.Name}.{MemberName}";
    }

    /// <summary>The result of looking for one entry in the game assembly that is actually loaded.</summary>
    internal sealed record Probe(string Id, string Feature, bool Found);

    /// <summary>
    /// Every private game member the mod resolves by name, with the feature that degrades without
    /// it.
    ///
    /// The <c>Static</c> flag is part of the entry because it is part of the lookup: reading the
    /// metadata for a member's *name* is not enough. <c>_longPressDuration</c> is static, this
    /// registry first declared it as an instance member, and the probe correctly reported that the
    /// mod could not read it -- which turned out to be true of the reading code as well.
    /// </summary>
    private static readonly Entry[] Entries =
    {
        new(typeof(NEndTurnButton), "_longPressBar", MemberKind.Field, false, "end_turn long-press detection"),
        new(typeof(NEndTurnLongPressBar), "_enabled", MemberKind.Field, false, "end_turn long-press detection"),
        new(typeof(NEndTurnLongPressBar), "_longPressDuration", MemberKind.Field, true, "end_turn long-press detection"),
        new(typeof(NDevConsole), "_devConsole", MemberKind.Field, false, "run_console_command"),
        new(typeof(NMultiplayerSubmenu), "StartLoad", MemberKind.Method, false, "continue_ai_teammate"),
        new(typeof(StartRunLobby), "_maxPlayers", MemberKind.Field, false, "multiplayer_lobby.max_players"),
        new(typeof(NCrystalSphereScreen), "_entity", MemberKind.Field, false, "crystal sphere actions"),
        new(typeof(NPlayerHand), "_prefs", MemberKind.Field, false, "combat hand selection metadata"),
        new(typeof(NPlayerHand), "_selectedCards", MemberKind.Field, false, "combat hand selection metadata"),
        new(typeof(NMultiplayerTest), "_lobby", MemberKind.Field, false, "multiplayer test lobby"),
        new(typeof(CommandLineHelper), "_args", MemberKind.Field, true, "companion launch arguments"),
        new(typeof(NGameOverScreen), "_isAnimatingSummary", MemberKind.Field, false, "game_over.showing_summary"),
        new(typeof(NSingleplayerSubmenu), "_standardButton", MemberKind.Field, false, "continue_run"),
        new(typeof(NPatchNotesScreen), "_backButton", MemberKind.Field, false, "close_main_menu_submenu"),
        new(typeof(NPauseMenu), "_saveAndQuitButton", MemberKind.Field, false, "save_and_quit"),
        new(typeof(NUnlockScreen), "_unlockConfirmButton", MemberKind.Field, false, "confirm_unlock"),
        // The six unlock payload fields each live on their own concrete screen, which is why the
        // reader tries all six names against whatever screen is open and expects five to miss.
        new(typeof(NUnlockRelicsScreen), "_relics", MemberKind.Field, false, "unlock.items"),
        new(typeof(NUnlockCardsScreen), "_cards", MemberKind.Field, false, "unlock.items"),
        new(typeof(NUnlockPotionsScreen), "_potions", MemberKind.Field, false, "unlock.items"),
        new(typeof(NUnlockEpochScreen), "_unlockedEpochs", MemberKind.Field, false, "unlock.items"),
        new(typeof(NUnlockCharacterScreen), "_character", MemberKind.Field, false, "unlock.items"),
        new(typeof(NUnlockCharacterScreen), "_epoch", MemberKind.Field, false, "unlock.items"),
    };

    private static Probe[]? _probes;

    /// <summary>
    /// Resolves every entry against the loaded game assembly, once, and caches the answer.
    /// </summary>
    /// <remarks>
    /// Once per process is enough: the assembly cannot change while the game runs, and a probe that
    /// re-ran on every <c>/health</c> would put reflection on a request path for no new information.
    /// </remarks>
    internal static IReadOnlyList<Probe> Probes => _probes ??= Entries.Select(Resolve).ToArray();

    /// <summary>Entries the game no longer declares. Empty is the healthy answer.</summary>
    internal static IReadOnlyList<Probe> Missing => Probes.Where(probe => !probe.Found).ToArray();

    private static Probe Resolve(Entry entry)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
            (entry.Static ? BindingFlags.Static : BindingFlags.Instance);

        var found = entry.Kind switch
        {
            MemberKind.Field => entry.DeclaringType.GetField(entry.MemberName, flags) != null,
            MemberKind.Method => entry.DeclaringType.GetMethod(entry.MemberName, flags) != null,
            _ => false,
        };

        return new Probe(entry.Id, entry.Feature, found);
    }

    /// <summary>
    /// The <c>compatibility</c> block of <c>GET /health</c>: whether this mod can still read the
    /// game it is running inside, and what stops working if it cannot.
    /// </summary>
    internal static object BuildHealthSection()
    {
        var probes = Probes;
        var missing = Missing;
        return new
        {
            reflected_members_checked = probes.Count,
            reflected_members_missing = missing.Count,
            // Named, not counted: "three members are missing" tells a player nothing they can act
            // on, and tells whoever fixes the mod nothing about where to start.
            missing_members = missing
                .Select(probe => new { member = probe.Id, feature = probe.Feature })
                .ToArray(),
        };
    }

    /// <summary>
    /// The <c>status</c> of <c>GET /health</c>, derived rather than asserted.
    /// </summary>
    /// <remarks>
    /// This used to be the literal <c>"ready"</c>, which made it the one field on the endpoint that
    /// could never be wrong and never be useful.
    /// </remarks>
    internal static string ResolveStatus() => Missing.Count == 0 ? "ready" : "degraded";
}
