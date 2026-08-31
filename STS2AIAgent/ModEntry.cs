using System.Threading;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2AIAgent.Agent;
using STS2AIAgent.Game;
using STS2AIAgent.Server;
using STS2AIAgent.Ui;

namespace STS2AIAgent;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string LogPrefix = "[STS2AIAgent]";

    private static int _shutdownHooksRegistered;

    public static void Initialize()
    {
        Log.Info($"{LogPrefix} Initializing");
        RegisterShutdownHooks();
        GameThread.Initialize();
        GameEventService.Instance.Start();
        GameFactInstrumentation.Install();
        OptionalAiTeammateTelemetry.Install();
        HttpServer.Instance.Start();
        AgentRuntime.Instance.Initialize();
        AgentOverlayHost.Install();
        Log.Info($"{LogPrefix} Ready on {HttpServer.Instance.Prefix}");
    }

    private static void RegisterShutdownHooks()
    {
        if (Interlocked.Exchange(ref _shutdownHooksRegistered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => Shutdown();
    }

    private static void Shutdown()
    {
        try
        {
            AgentRuntime.Instance.Shutdown();
            AgentOverlayHost.Uninstall();
            OptionalAiTeammateTelemetry.Uninstall();
            GameFactInstrumentation.Uninstall();
            GameEventService.Instance.Stop();
            HttpServer.Instance.Stop();
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Failed during shutdown: {ex}");
        }
    }
}
