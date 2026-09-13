using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Multiplayer;

/// <summary>
/// Game-side half of the co-op save precheck. It only reads: it opens the current profile's
/// multiplayer run save and hands the text to <see cref="CoopSavePrecheckPolicy"/>. It never
/// writes, renames or loads the save, so it can never trigger the destructive
/// <c>RenameBrokenMultiplayerRunSave</c> the check exists to avoid.
/// </summary>
internal static class CoopSaveProbe
{
    /// <summary>
    /// Reads <c>players[].net_id</c> out of the multiplayer run save. False (with an empty id list)
    /// means the ids could not be read - no profile, no save, an unreadable file, or a file without
    /// player ids - and every consumer treats that as "no evidence", never as a mismatch.
    /// </summary>
    public static bool TryReadMultiplayerSaveNetIds(out IReadOnlyList<ulong> netIds, out string? error)
    {
        netIds = Array.Empty<ulong>();

        try
        {
            var saveManager = SaveManager.Instance;
            if (!saveManager.IsProfileInitialized)
            {
                error = "save_profile_not_initialized";
                return false;
            }

            // RunSaveManager.multiplayerRunSaveFileName is a public const ("current_run_mp.save"),
            // verified against the sts2 assembly, so read it instead of a literal: a rename in a game
            // patch would otherwise make this probe quietly read the wrong file and fail open.
            var relativePath = Path.Combine(UserDataPathProvider.SavesDir, RunSaveManager.multiplayerRunSaveFileName);
            var scopedPath = saveManager.GetProfileScopedPath(relativePath);
            if (!Godot.FileAccess.FileExists(scopedPath))
            {
                error = "multiplayer_save_missing";
                return false;
            }

            // Same Godot user:// filesystem SaveManager writes through: reopening the file with
            // System.IO can fail while the engine still owns it even though Godot reads it fine.
            using var file = Godot.FileAccess.Open(scopedPath, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                error = "multiplayer_save_read_failed:Godot:" + Godot.FileAccess.GetOpenError();
                return false;
            }

            var ids = CoopSavePrecheckPolicy.ReadPlayerNetIds(file.GetAsText());
            if (ids.Count == 0)
            {
                error = "multiplayer_save_no_player_ids";
                return false;
            }

            netIds = ids;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            netIds = Array.Empty<ulong>();
            error = "multiplayer_save_read_failed:" + ex.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// The local player id the game will canonicalize the save against. Copied from the load button
    /// itself (<c>NMultiplayerSubmenu.StartLoad</c>): it picks <c>PlatformType.None</c> whenever the
    /// <c>-fastmp</c> argument is present, and this flow always injects that argument
    /// (<c>EnableFastMpENetHost</c>) before pressing the button - so the offline id is what the save
    /// is checked against even on a Steam-initialized host, where
    /// <c>PlatformUtil.PrimaryPlatform</c> would report the Steam id instead.
    /// </summary>
    public static ulong ResolveLoadLocalPlayerId()
    {
        return PlatformUtil.GetLocalPlayerId(PlatformType.None);
    }

    /// <summary>
    /// Companion id exactly as the launcher computes it, or the launcher's own message. Resolving it
    /// here rather than in the callers keeps <c>CommandLineHelper</c> out of the HTTP handler.
    /// </summary>
    public static bool TryResolveCompanionClientId(out ulong companionClientId, out string? error)
    {
        try
        {
            companionClientId = CoopLaunchPolicy.ResolveCompanionClientId(CommandLineHelper.GetValue("clientId"));
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            companionClientId = 0;
            error = Loc.T("无法计算队友启动参数：{0}", ex.Message);
            return false;
        }
    }
}
