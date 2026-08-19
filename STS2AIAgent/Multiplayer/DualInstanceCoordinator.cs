using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Game;

namespace STS2AIAgent.Multiplayer;

internal static class DualInstanceCoordinator
{
    private const string LogPrefix = "[STS2AIAgent.DualInstance]";

    public static async Task<string> HostLocalCoopAsync(CancellationToken cancellationToken)
    {
        var launch = await LocalDualInstanceLauncher.LaunchCompanionAsync(cancellationToken);
        if (!launch.Ok)
        {
            return launch.Message;
        }

        try
        {
            await OpenMultiplayerTestAsync(cancellationToken);
            await ExecuteActionAsync("host_multiplayer_lobby", cancellationToken);
            return launch.Message + "。本机已创建大厅，请选角色后 Ready。同伴实例会自动加入并开始游玩。";
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Host lobby failed: {ex.Message}");
            return launch.Message + "。大厅创建失败：" + ex.Message;
        }
    }

    public static async Task<bool> RunCompanionBootstrapAsync(CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion)
        {
            return false;
        }

        try
        {
            Log.Info($"{LogPrefix} Companion bootstrap starting");
            await WaitForScreenAsync(new[] { "MAIN_MENU", "MULTIPLAYER_LOBBY" }, TimeSpan.FromSeconds(90), cancellationToken);
            var screen = await GetScreenAsync();
            if (!string.Equals(screen, "MULTIPLAYER_LOBBY", StringComparison.Ordinal))
            {
                await OpenMultiplayerTestAsync(cancellationToken);
            }

            await WaitForScreenAsync(new[] { "MULTIPLAYER_LOBBY" }, TimeSpan.FromSeconds(30), cancellationToken);
            await WaitForAnyActionAsync(new[] { "join_multiplayer_lobby" }, TimeSpan.FromSeconds(30), cancellationToken);
            await ExecuteActionAsync("join_multiplayer_lobby", cancellationToken);
            var next = await WaitForAnyActionAsync(
                new[] { "select_character", "ready_multiplayer_lobby" },
                TimeSpan.FromSeconds(20),
                cancellationToken);
            if (string.Equals(next, "select_character", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteActionAsync("select_character", cancellationToken, optionIndex: 0);
                await WaitForAnyActionAsync(new[] { "ready_multiplayer_lobby" }, TimeSpan.FromSeconds(15), cancellationToken);
            }

            await ExecuteActionAsync("ready_multiplayer_lobby", cancellationToken);
            Log.Info($"{LogPrefix} Companion is ready; auto-play can start");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Companion bootstrap failed: {ex}");
            return false;
        }
    }

    private static async Task OpenMultiplayerTestAsync(CancellationToken cancellationToken)
    {
        await GameThread.InvokeAsync(() => GameActionService.ExecuteInternalConsoleCommandAsync("multiplayer test"));
        await WaitForScreenAsync(new[] { "MULTIPLAYER_LOBBY" }, TimeSpan.FromSeconds(20), cancellationToken);
    }

    private static Task ExecuteActionAsync(string action, CancellationToken cancellationToken, int? optionIndex = null)
    {
        _ = cancellationToken;
        return GameThread.InvokeAsync(() => GameActionService.ExecuteAsync(new ActionRequest
        {
            action = action,
            option_index = optionIndex,
            client_context = new { source = "dual_instance", instance_role = InstanceRole.Current }
        }));
    }

    private static Task<string> GetScreenAsync()
    {
        return GameThread.InvokeAsync(() => GameStateService.BuildStatePayload().screen);
    }

    private static Task<IReadOnlyList<string>> GetActionNamesAsync()
    {
        return GameThread.InvokeAsync(() =>
            (IReadOnlyList<string>)(GameStateService.BuildStatePayload().available_actions ?? Array.Empty<string>()));
    }

    private static async Task<string> WaitForAnyActionAsync(
        IReadOnlyList<string> actions,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = await GetActionNamesAsync();
            foreach (var action in actions)
            {
                if (names.Contains(action, StringComparer.OrdinalIgnoreCase))
                {
                    return action;
                }
            }

            await GameThread.WaitForNextFrameAsync();
        }

        throw new TimeoutException($"Timed out waiting for action {string.Join("/", actions)}.");
    }

    private static async Task WaitForScreenAsync(IReadOnlyList<string> screens, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screen = await GetScreenAsync();
            if (screens.Contains(screen, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            await GameThread.WaitForNextFrameAsync();
        }

        throw new TimeoutException($"Timed out waiting for screen {string.Join("/", screens)}.");
    }
}
