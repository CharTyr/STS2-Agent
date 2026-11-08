using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Settings;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Game;

/// <summary>
/// Turns the game's own animation speed up while the AI plays and gives the player their setting back
/// afterwards. Every member that touches the game must run on the game thread.
/// </summary>
/// <remarks>
/// This uses the game's official Fast Mode (<c>PrefsSave.FastMode</c>) -- the same switch as the
/// settings page and the <c>instant</c> console command -- rather than scaling waits with patches, so
/// it composes with other speed mods instead of multiplying with them.
///
/// The player's setting must never be lost:
/// 1. the original value is remembered in memory and persisted in our own settings while we hold the
///    speed (so a crash can be repaired on the next launch, see <see cref="RecoverAtStartup"/>);
/// 2. a Harmony guard on <c>PrefsSaveManager.SavePrefs</c> writes the player's value to
///    <c>prefs.save</c> whenever the game saves while we hold the speed, so the boosted level never
///    reaches the file;
/// 3. release only writes back when the current value is still the one we set: a player who changed
///    the speed mid-run keeps their choice.
/// We never call the game's save ourselves.
/// </remarks>
internal static class TurboController
{
    private const string LogPrefix = "[STS2AIAgent.Turbo]";
    private static readonly object Gate = new();
    private static TurboLevel? _applied;
    private static TurboLevel? _original;
    private static string _reason = "idle";
    private static string _guard = "not_installed";
    private static bool _guardInstalled;
    private static TurboLevel? _lastSeen;

    /// <summary>Whether a speed we set is currently in force.</summary>
    public static bool Engaged
    {
        get { lock (Gate) return _applied != null; }
    }

    /// <summary>
    /// Patch the game's prefs writer once. Safe to call repeatedly; a missing method leaves the guard
    /// "unavailable" and caps the speed at Fast (which the game itself never persists as Instant).
    /// </summary>
    public static void InstallPrefsGuard()
    {
        lock (Gate)
        {
            if (_guardInstalled) return;
            _guardInstalled = true;
        }

        try
        {
            var method = ReflectedGameMembers.Method(typeof(PrefsSaveManager), "SavePrefs")
                ?? throw new MissingMethodException("PrefsSaveManager.SavePrefs");
            new Harmony("STS2AIAgent.turbo-prefs-guard").Patch(method,
                prefix: new HarmonyMethod(typeof(TurboController), nameof(BeforeSavePrefs)),
                finalizer: new HarmonyMethod(typeof(TurboController), nameof(AfterSavePrefs)));
            lock (Gate) _guard = "installed";
        }
        catch (Exception ex)
        {
            lock (Gate) _guard = "unavailable";
            Log.Warn($"{LogPrefix} Prefs guard unavailable ({ex.GetType().Name}); game speed is capped at Fast.");
        }
    }

    /// <summary>
    /// Apply the configured speed. Returns the player's original level to persist (Format string), or
    /// null when nothing was changed. Game thread only.
    /// </summary>
    public static string? Engage(string? mode, string? persistedOriginal = null)
    {
        try
        {
            var prefs = SaveManager.Instance.PrefsSave;
            var current = ToLevel(prefs.FastMode);
            // An original still persisted from a session that never released (crash before recovery
            // ran) is the player's real value; the current one may be our leftover boost.
            lock (Gate)
            {
                if (_original == null && TurboPolicy.Parse(persistedOriginal) is { } unrecovered)
                {
                    _original = unrecovered;
                }
            }
            var multiplayer = IsMultiplayerRun();
            string? effectiveMode;
            lock (Gate)
            {
                // Without the guard the boosted value could reach prefs.save; Fast is what the game
                // itself restores Instant to at startup, so it is the safe ceiling.
                effectiveMode = _guard == "unavailable" && TurboPolicy.NormalizeMode(mode) == TurboPolicy.InstantMode
                    ? TurboPolicy.FastMode
                    : mode;
            }

            var decision = TurboPolicy.Decide(effectiveMode, multiplayer, current);
            lock (Gate)
            {
                _reason = decision.Reason;
                _lastSeen = current;
                if (decision.Target is not { } target)
                {
                    if (_applied == null && _original is { } unrecovered && current > unrecovered)
                    {
                        // The speed in force is a leftover of ours: own it so release restores it.
                        _applied = current;
                        return TurboPolicy.Format(unrecovered);
                    }

                    if (_applied == null)
                    {
                        _original = null;
                    }

                    return null;
                }

                // A second Engage while already engaged keeps the first original.
                _original ??= current;
                _applied = target;
                _lastSeen = target;
            }

            prefs.FastMode = ToGame(decision.Target.Value);
            Log.Info($"{LogPrefix} Game speed {TurboPolicy.Format(current)} -> {TurboPolicy.Format(decision.Target.Value)} ({decision.Reason}).");
            lock (Gate) return TurboPolicy.Format(_original!.Value);
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Could not apply game speed: {ex.GetType().Name}: {ex.Message}");
            lock (Gate) _reason = "error";
            return null;
        }
    }

    /// <summary>
    /// Auto-play can start at the main menu and only then enter a co-op run; once the run is known to
    /// be multiplayer, a held Instant drops to Fast. Cheap (one enum read); called once per turn on the
    /// game thread.
    /// </summary>
    public static void CapForMultiplayer()
    {
        lock (Gate)
        {
            if (_applied is not TurboLevel.Instant) return;
        }

        try
        {
            if (!IsMultiplayerRun()) return;
            var prefs = SaveManager.Instance.PrefsSave;
            lock (Gate)
            {
                if (ToLevel(prefs.FastMode) != _applied) return;
                _applied = TurboLevel.Fast;
                _lastSeen = TurboLevel.Fast;
                _reason = "multiplayer_cap";
            }

            prefs.FastMode = FastModeType.Fast;
            Log.Info($"{LogPrefix} Multiplayer run: game speed capped at Fast.");
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Multiplayer cap failed: {ex.GetType().Name}");
        }
    }

    private static bool IsMultiplayerRun() =>
        RunManager.Instance.DebugOnlyGetState() != null
        && RunManager.Instance.NetService?.Type.IsMultiplayer() == true;

    /// <summary>
    /// Give the player their speed back if it is still ours. Returns true when the persisted original
    /// should now be cleared. Game thread only.
    /// </summary>
    public static bool Release()
    {
        TurboLevel? applied, original;
        lock (Gate)
        {
            applied = _applied;
            original = _original;
            _applied = null;
            _original = null;
            _reason = "released";
        }

        if (applied == null || original == null)
        {
            return true;
        }

        try
        {
            var prefs = SaveManager.Instance.PrefsSave;
            var current = ToLevel(prefs.FastMode);
            if (TurboPolicy.ShouldRestore(current, applied.Value))
            {
                prefs.FastMode = ToGame(original.Value);
                current = original.Value;
                Log.Info($"{LogPrefix} Game speed restored to {TurboPolicy.Format(original.Value)}.");
            }
            else
            {
                Log.Info($"{LogPrefix} The player changed the speed to {TurboPolicy.Format(current)} meanwhile; keeping it.");
            }

            lock (Gate) _lastSeen = current;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Could not restore game speed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// A persisted original means the last session ended while our speed was applied (crash or kill).
    /// The guard kept the file clean, but the game may have started from a value we wrote before it,
    /// so put the player's value back. Returns true when the stored marker can be cleared.
    /// </summary>
    public static bool RecoverAtStartup(string? storedOriginal)
    {
        if (TurboPolicy.Parse(storedOriginal) is not { } original)
        {
            return true;
        }

        lock (Gate)
        {
            if (_applied != null) return false;
        }

        try
        {
            if (!SaveManager.Instance.IsPrefsLoaded) return false;
            var prefs = SaveManager.Instance.PrefsSave;
            // Only undo a boost that leaked: the game itself lowers Instant to Fast at startup, and
            // that slower value is the game's decision, not ours to revert.
            if (ToLevel(prefs.FastMode) > original)
            {
                prefs.FastMode = ToGame(original);
                Log.Info($"{LogPrefix} Restored the game speed left boosted by the previous session: {TurboPolicy.Format(original)}.");
            }

            lock (Gate) _lastSeen = ToLevel(prefs.FastMode);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Startup speed recovery failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// The <c>/health.turbo</c> section. Called off the game thread, so it reports the speed last seen
    /// on the game thread rather than reading the game's prefs here.
    /// </summary>
    public static object Snapshot(string? mode)
    {
        lock (Gate)
        {
            return new
            {
                mode = TurboPolicy.NormalizeMode(mode),
                applied = _applied != null,
                original = _original is { } original ? TurboPolicy.Format(original) : null,
                current = _lastSeen is { } seen ? TurboPolicy.Format(seen) : null,
                prefs_guard = _guard,
                reason = _reason,
                other_speed_mod = OtherSpeedModLoaded()
            };
        }
    }

    /// <summary>Whether a known third-party speed mod (SpeedX) is loaded; both work together.</summary>
    public static bool OtherSpeedModLoaded()
    {
        try
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetName().Name?.Contains("SpeedX", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void BeforeSavePrefs(PrefsSaveManager __instance, out TurboLevel? __state)
    {
        __state = null;
        try
        {
            TurboLevel? applied, original;
            lock (Gate)
            {
                applied = _applied;
                original = _original;
            }

            var prefs = __instance.Prefs;
            if (applied == null || original == null || prefs == null || ToLevel(prefs.FastMode) != applied)
            {
                return;
            }

            // Write the player's value; the finalizer puts ours back after the write.
            prefs.FastMode = ToGame(original.Value);
            __state = applied;
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Prefs guard prefix failed: {ex.GetType().Name}");
        }
    }

    private static Exception? AfterSavePrefs(PrefsSaveManager __instance, TurboLevel? __state, Exception? __exception)
    {
        if (__state is { } applied)
        {
            try
            {
                if (__instance.Prefs != null)
                {
                    __instance.Prefs.FastMode = ToGame(applied);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"{LogPrefix} Prefs guard finalizer failed: {ex.GetType().Name}");
            }
        }

        return __exception;
    }

    private static TurboLevel ToLevel(FastModeType value) => value switch
    {
        FastModeType.Normal => TurboLevel.Normal,
        FastModeType.Fast => TurboLevel.Fast,
        FastModeType.Instant => TurboLevel.Instant,
        _ => TurboLevel.None
    };

    private static FastModeType ToGame(TurboLevel level) => level switch
    {
        TurboLevel.Normal => FastModeType.Normal,
        TurboLevel.Fast => FastModeType.Fast,
        TurboLevel.Instant => FastModeType.Instant,
        _ => FastModeType.None
    };
}
