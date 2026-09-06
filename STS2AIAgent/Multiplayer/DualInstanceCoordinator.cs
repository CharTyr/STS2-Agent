using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Game;

namespace STS2AIAgent.Multiplayer;

internal static class DualInstanceCoordinator
{
    private const string LogPrefix = "[STS2AIAgent.DualInstance]";

    public static async Task<string> HostLocalCoopAsync(CancellationToken cancellationToken)
    {
        if (await GetScreenAsync() != "MAIN_MENU")
        {
            return "请先回到主菜单，再邀请 AI 队友组队。";
        }

        try
        {
            await OpenLocalFourPlayerLobbyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} Local lobby failed: {ex.Message}");
            return "创建 4 人大厅失败：" + ex.Message;
        }

        var launch = await LocalDualInstanceLauncher.LaunchCompanionAsync(cancellationToken);
        if (!launch.Ok)
        {
            return launch.Message;
        }

        return launch.Message + "。本机已创建 4 人大厅，请选角色后 Ready。同伴实例会自动加入并开始游玩。";
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
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await GameThread.InvokeAsync(() =>
                {
                    GameStateService.EnsureFourPlayerLobby();
                    return true;
                });
                var snapshot = await GetBootstrapSnapshotAsync();
                if (CoopLaunchPolicy.CompanionHasJoinedRun(snapshot.Screen, snapshot.Actions))
                {
                    Log.Info($"{LogPrefix} Companion is ready; auto-play can start");
                    return true;
                }

                var next = CoopLaunchPolicy.NextCompanionBootstrapAction(
                    snapshot.Screen,
                    snapshot.Actions,
                    snapshot.HasLobby);
                if (next == null)
                {
                    await GameThread.WaitForNextFrameAsync();
                    continue;
                }

                try
                {
                    var option = string.Equals(next, "select_character", StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : (int?)null;
                    await ExecuteActionAsync(next, cancellationToken, option);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn($"{LogPrefix} Companion bootstrap action {next} failed: {ex.Message}");
                    await GameThread.WaitForNextFrameAsync();
                }
            }

            throw new TimeoutException("Timed out joining the local room.");
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Companion bootstrap failed: {ex}");
            return false;
        }
    }

    private static async Task OpenLocalFourPlayerLobbyAsync(CancellationToken cancellationToken)
    {
        EnableFastMpENetHost();
        try
        {
            await GameThread.InvokeAsync(() => GameActionService.StartLocalFourPlayerHostAsync());
            await GameThread.InvokeAsync(() =>
            {
                GameStateService.EnsureFourPlayerLobby();
                return true;
            });
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"{LogPrefix} FastHost ENet lobby failed, falling back to debug lobby: {ex.Message}");
        }

        await OpenMultiplayerTestAsync(cancellationToken);
        await ExecuteActionAsync("host_multiplayer_lobby", cancellationToken);
        await GameThread.InvokeAsync(() =>
        {
            GameStateService.EnsureFourPlayerLobby();
            return true;
        });
    }

    private static void EnableFastMpENetHost()
    {
        var field = typeof(CommandLineHelper).GetField("_args", BindingFlags.Static | BindingFlags.NonPublic)
            ?? typeof(CommandLineHelper).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.FieldType.Name.Contains("Dictionary", StringComparison.Ordinal));
        var args = field?.GetValue(null)
            ?? throw new InvalidOperationException("找不到 FastHost 命令行参数表。");

        if (args is Godot.Collections.Dictionary<string, string?> typedNullable)
        {
            typedNullable["fastmp"] = "host_standard";
        }
        else if (args is Godot.Collections.Dictionary<string, string> typed)
        {
            typed["fastmp"] = "host_standard";
        }
        else
        {
            var indexer = args.GetType().GetProperty("Item")
                ?? throw new InvalidOperationException("FastHost 命令行参数表类型无法写入：" + args.GetType().FullName);
            indexer.SetValue(args, "host_standard", new object[] { "fastmp" });
        }

        if (!CommandLineHelper.HasArg("fastmp"))
        {
            throw new InvalidOperationException("写入 -fastmp 后 HasArg 仍为 false。type=" + args.GetType().FullName);
        }

        Log.Info($"{LogPrefix} Injected -fastmp host_standard so Steam host uses ENet:33771 max=4");
    }

    private static async Task OpenMultiplayerTestAsync(CancellationToken cancellationToken)
    {
        await GameThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GameStateService.BuildStatePayload().screen != "MAIN_MENU")
            {
                throw new InvalidOperationException("请先回到主菜单，再邀请 AI 队友组队。");
            }

            return GameActionService.ExecuteInternalConsoleCommandAsync("multiplayer test");
        });
        await WaitForScreenAsync(new[] { "MULTIPLAYER_LOBBY" }, TimeSpan.FromSeconds(20), cancellationToken);
    }

    private static Task ExecuteActionAsync(string action, CancellationToken cancellationToken, int? optionIndex = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GameThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GameActionService.ExecuteAsync(new ActionRequest
            {
                action = action,
                option_index = optionIndex,
                client_context = new { source = "dual_instance", instance_role = InstanceRole.Current }
            });
        });
    }

    private static Task<string> GetScreenAsync()
    {
        return GameThread.InvokeAsync(() => GameStateService.BuildStatePayload().screen);
    }

    private static Task<CompanionBootstrapSnapshot> GetBootstrapSnapshotAsync()
    {
        return GameThread.InvokeAsync(() =>
        {
            var payload = GameStateService.BuildStatePayload();
            var hasLobby = payload.multiplayer_lobby?.has_lobby == true
                || string.Equals(payload.multiplayer?.net_game_type, "Client", StringComparison.OrdinalIgnoreCase)
                || string.Equals(payload.multiplayer?.net_game_type, "Host", StringComparison.OrdinalIgnoreCase)
                || (payload.character_select?.player_count ?? 0) >= 2;
            return new CompanionBootstrapSnapshot(
                payload.screen,
                payload.available_actions ?? Array.Empty<string>(),
                hasLobby);
        });
    }

    private readonly record struct CompanionBootstrapSnapshot(
        string Screen,
        IReadOnlyList<string> Actions,
        bool HasLobby);

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
