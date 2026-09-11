using System;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Localization;

/// <summary>
/// Gameside half of <see cref="Loc"/>: reads the running game's language and follows changes.
/// </summary>
/// <remarks>
/// Split out because it references Godot and the game assembly, which the offline test project
/// does not compile against.
/// </remarks>
internal static class LocSource
{
    private const string LogPrefix = "[STS2AIAgent]";

    private static bool _subscribed;
    private static bool _warned;
    private static int _retried;

    /// <summary>
    /// Reads the game's language now and subscribes for later changes. Safe to call repeatedly and
    /// from any point in startup: before the game's localization manager exists it falls back to the
    /// settings file, which is loaded earlier and carries the same value.
    /// </summary>
    public static void Initialize()
    {
        Report();
        Subscribe();
        RetrySubscribeAfterStartup();
    }

    /// <summary>Re-reads the game's language; harmless when no source is available yet.</summary>
    public static void Report()
    {
        var code = ReadLanguageCode();
        if (code == null)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warn($"{LogPrefix} No language source available yet; keeping {Loc.Current}");
            }

            return;
        }

        Loc.SetLanguage(code);
        Subscribe();
    }

    /// <summary>
    /// Startup runs before the game's localization manager exists, so the first subscribe attempt
    /// finds nothing. Godot runs this deferred call after the current frame, by which point the
    /// manager is up, which is what makes a mid-session language switch reach the mod.
    /// </summary>
    private static void RetrySubscribeAfterStartup()
    {
        if (_subscribed || Interlocked.Exchange(ref _retried, 1) != 0)
        {
            return;
        }

        try
        {
            Callable.From(Subscribe).CallDeferred();
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Could not queue the language-change subscription: {ex.GetType().Name}");
        }
    }

    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        try
        {
            var manager = MegaCrit.Sts2.Core.Localization.LocManager.Instance;
            if (manager == null)
            {
                // The mod initializes before the game's localization manager exists.
                return;
            }

            manager.SubscribeToLocaleChange(Report);
            _subscribed = true;
        }
        catch (Exception ex)
        {
            // Detection keeps working without the subscription; the mod just will not follow a
            // mid-session language switch.
            Log.Warn($"{LogPrefix} Could not subscribe to language changes: {ex.GetType().Name}");
        }
    }

    private static string? ReadLanguageCode()
    {
        // The localization manager is authoritative, but it does not exist yet while the mod
        // initializes; the settings file is loaded earlier and holds the same choice.
        try
        {
            var code = MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language;
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var code = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.SettingsSave?.Language;
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            return TranslationServer.GetLocale();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
