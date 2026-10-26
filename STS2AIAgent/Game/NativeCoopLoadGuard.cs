using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using STS2AIAgent.Agent;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Ui;

namespace STS2AIAgent.Game;

/// <summary>Stops the Steam load path before it can invalidate an identified local co-op save.</summary>
internal static class NativeCoopLoadGuard
{
    public static void Install()
    {
        try
        {
            var method = ReflectedGameMembers.Method(typeof(NMultiplayerSubmenu), "StartLoad")
                ?? throw new MissingMethodException("NMultiplayerSubmenu.StartLoad");
            new Harmony("STS2AIAgent.local-save-guard").Patch(method,
                prefix: new HarmonyMethod(typeof(NativeCoopLoadGuard), nameof(BeforeLoad)));
        }
        catch (Exception ex)
        {
            Log.Warn("[STS2AIAgent] Native co-op load guard unavailable: " + ex.GetType().Name);
        }
    }

    private static bool BeforeLoad()
    {
        if (CommandLineHelper.HasArg("fastmp")) return true;
        try
        {
            if (!CoopSaveProbe.TryReadMultiplayerSaveNetIds(out var ids, out _)
                || !CoopSaveProbe.TryResolveCompanionClientId(out var companionId, out _)
                || !CoopSavePrecheckPolicy.NeedsLocalContinue(ids,
                    PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform),
                    CoopSaveProbe.ResolveLoadLocalPlayerId(), companionId)) return true;
        }
        catch (Exception ex)
        {
            Log.Warn("[STS2AIAgent] Native save identity probe unavailable: " + ex.GetType().Name);
            return true;
        }
        try
        {
            AgentRuntime.Instance.NotifyStatus(Loc.T("这是本地队友存档。已阻止会损坏存档的原生读档，请在模组多人页点击「继续上次联机对局」。"));
            AgentOverlayHost.ShowLocalCoopContinue();
        }
        catch (Exception ex) { Log.Warn("[STS2AIAgent] Local save preserved; overlay unavailable: " + ex.GetType().Name); }
        return false;
    }
}
