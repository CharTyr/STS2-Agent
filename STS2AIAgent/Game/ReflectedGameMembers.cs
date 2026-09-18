using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
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
/// every entry is resolved once when the mod loads, the misses are logged, and they are reported on
/// <c>GET /health</c>.
///
/// **It is also the only place those members are looked up.** Call sites ask <see cref="Field"/> or
/// <see cref="Method"/> for the member instead of calling <c>GetField</c> themselves. That is the
/// point, not a convenience: the first version of this registry resolved its own copy of each
/// member while the call sites resolved theirs, with their own binding flags, and the two could
/// disagree. They did -- <c>_longPressDuration</c> is static, the reader asked for an instance
/// field, and the probe would have reported <c>ready</c> the moment the registry was written
/// correctly while the reader went on falling back to a number it invented. One lookup, one set of
/// flags, and what the probe reports is what the mod actually gets.
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
        Property,
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
        // Private getter, so it cannot be compile-checked. Without it the readiness check answers
        // "ready" unconditionally -- the same kind of invented answer as the rest of this list.
        new(typeof(NEndTurnButton), "CanTurnBeEnded", MemberKind.Property, false, "combat.action_readiness end_turn"),
        new(typeof(NEndTurnLongPressBar), "_enabled", MemberKind.Field, false, "end_turn long-press detection"),
        new(typeof(NEndTurnLongPressBar), "_longPressDuration", MemberKind.Field, true, "end_turn long-press detection"),
        new(typeof(NDevConsole), "_devConsole", MemberKind.Field, false, "run_console_command"),
        new(typeof(NMultiplayerSubmenu), "StartLoad", MemberKind.Method, false, "continue_ai_teammate"),
        new(typeof(StartRunLobby), "_maxPlayers", MemberKind.Field, false, "multiplayer_lobby.max_players"),
        new(typeof(NCrystalSphereScreen), "_entity", MemberKind.Field, false, "crystal sphere actions"),
        new(typeof(NPlayerHand), "_prefs", MemberKind.Field, false, "combat hand selection metadata"),
        new(typeof(NPlayerHand), "_selectedCards", MemberKind.Field, false, "combat hand selection metadata"),
        new(typeof(NMultiplayerTest), "_lobby", MemberKind.Field, false, "multiplayer test lobby"),
        // Found by a helper that took the name as a parameter, which is how they stayed out of this
        // list: the contract looked for literals passed to GetMethod, and these never were.
        new(typeof(NMultiplayerTest), "StartHost", MemberKind.Method, false, "host_multiplayer_lobby"),
        new(typeof(NMultiplayerTest), "ReadyButtonPressed", MemberKind.Method, false, "ready_multiplayer_lobby"),
        new(typeof(NMultiplayerTest), "Disconnect", MemberKind.Method, false, "disconnect_multiplayer_lobby"),
        new(typeof(CommandLineHelper), "_args", MemberKind.Field, true, "companion launch arguments"),
        new(typeof(NGameOverScreen), "_isAnimatingSummary", MemberKind.Field, false, "game_over.showing_summary"),
        new(typeof(NSingleplayerSubmenu), "_standardButton", MemberKind.Field, false, "continue_run"),
        new(typeof(NPatchNotesScreen), "_backButton", MemberKind.Field, false, "close_main_menu_submenu"),
        new(typeof(NPauseMenu), "_saveAndQuitButton", MemberKind.Field, false, "save_and_quit"),
        new(typeof(NPauseMenu), "CloseToMenu", MemberKind.Method, false, "save_and_quit"),
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

    private static readonly Lazy<IReadOnlyDictionary<(Type, string), (Entry Entry, MemberInfo? Member)>> Resolved =
        new(() => Entries.ToDictionary(entry => (entry.DeclaringType, entry.MemberName), entry => (entry, Resolve(entry))));

    /// <summary>The field this registry resolved, or null if this game build no longer has it.</summary>
    /// <exception cref="InvalidOperationException">The member is not registered.</exception>
    /// <remarks>
    /// Throwing for an unregistered name is deliberate: a call site that could fall back to its own
    /// <c>GetField</c> would bring back the second, independent lookup this registry exists to remove.
    /// </remarks>
    internal static FieldInfo? Field(Type declaringType, string memberName) =>
        Lookup(declaringType, memberName, MemberKind.Field) as FieldInfo;

    /// <summary>The method this registry resolved, or null if this game build no longer has it.</summary>
    /// <exception cref="InvalidOperationException">The member is not registered.</exception>
    internal static MethodInfo? Method(Type declaringType, string memberName) =>
        Lookup(declaringType, memberName, MemberKind.Method) as MethodInfo;

    /// <summary>The property this registry resolved, or null if this game build no longer has it.</summary>
    /// <exception cref="InvalidOperationException">The member is not registered.</exception>
    internal static PropertyInfo? Property(Type declaringType, string memberName) =>
        Lookup(declaringType, memberName, MemberKind.Property) as PropertyInfo;

    /// <summary>
    /// The unlock screens' item fields. Each is declared on a different concrete screen, so a reader
    /// holding one screen tries all six and keeps the one whose declaring type matches.
    /// </summary>
    internal static IReadOnlyList<FieldInfo> UnlockItemFields => new[]
        {
            Field(typeof(NUnlockRelicsScreen), "_relics"),
            Field(typeof(NUnlockCardsScreen), "_cards"),
            Field(typeof(NUnlockPotionsScreen), "_potions"),
            Field(typeof(NUnlockEpochScreen), "_unlockedEpochs"),
            Field(typeof(NUnlockCharacterScreen), "_character"),
            Field(typeof(NUnlockCharacterScreen), "_epoch"),
        }
        .Where(field => field != null)
        .Select(field => field!)
        .ToArray();

    private static MemberInfo? Lookup(Type declaringType, string memberName, MemberKind kind)
    {
        if (!Resolved.Value.TryGetValue((declaringType, memberName), out var resolved) || resolved.Entry.Kind != kind)
        {
            throw new InvalidOperationException(
                $"{declaringType.Name}.{memberName} is not a registered {kind.ToString().ToLowerInvariant()}. "
                + "Add it to ReflectedGameMembers.Entries so the startup probe covers it.");
        }

        return resolved.Member;
    }

    /// <summary>Every entry and whether this game build still has it.</summary>
    internal static IReadOnlyList<Probe> Probes =>
        Resolved.Value.Values
            .Select(resolved => new Probe(resolved.Entry.Id, resolved.Entry.Feature, resolved.Member != null))
            .ToArray();

    /// <summary>Entries the game no longer declares. Empty is the healthy answer.</summary>
    internal static IReadOnlyList<Probe> Missing => Probes.Where(probe => !probe.Found).ToArray();

    private static MemberInfo? Resolve(Entry entry)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
            (entry.Static ? BindingFlags.Static : BindingFlags.Instance);

        return entry.Kind switch
        {
            MemberKind.Field => entry.DeclaringType.GetField(entry.MemberName, flags),
            MemberKind.Method => entry.DeclaringType.GetMethod(entry.MemberName, flags),
            MemberKind.Property => entry.DeclaringType.GetProperty(entry.MemberName, flags),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves every entry when the mod loads and writes the misses to the game log.
    /// </summary>
    /// <remarks>
    /// <c>GET /health</c> alone is not enough: a player reporting a problem attaches the log, not a
    /// curl of an endpoint they have never heard of. A renamed member has to be in the file they
    /// actually send.
    /// </remarks>
    internal static void ProbeAtStartup()
    {
        var missing = Missing;
        if (missing.Count == 0)
        {
            Log.Info($"[STS2AIAgent] Compatibility: all {Probes.Count} reflected game members resolved.");
            return;
        }

        foreach (var probe in missing)
        {
            Log.Warn($"[STS2AIAgent] Compatibility: {probe.Id} is missing in this game build; {probe.Feature} will fall back to defaults.");
        }

        Log.Warn($"[STS2AIAgent] Compatibility: {missing.Count} of {Probes.Count} reflected game members are missing. GET /health reports status \"degraded\".");
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
