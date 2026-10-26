using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Ui;

internal sealed partial class AgentOverlayHost
{
    /// <summary>Serialize the entire pause/confirm/save transition, not merely button clicks.</summary>
    private async Task ChangePlayModeAsync(int requestedIndex)
    {
        var runtime = AgentRuntime.Instance;
        var original = runtime.Settings.OverlayPlayMode == "coop" ? "coop" : "solo";
        var requested = requestedIndex == 1 ? "coop" : "solo";
        if (_playModeSwitching || original == requested)
        {
            if (_playModeSwitch != null && _playModeSwitch.IsInsideTree())
                _playModeSwitch.SetSelected(original == "coop" ? 1 : 0);
            return;
        }

        if (!runtime.TryBeginPlayModeSwitch(original))
        {
            if (_playModeSwitch != null && _playModeSwitch.IsInsideTree())
                _playModeSwitch.SetSelected(original == "coop" ? 1 : 0);
            return;
        }
        _playModeSwitching = true;
        _modeSwitchNotice = Loc.T("正在暂停…");
        try
        {
            await GameThread.InvokeAsync(RefreshDynamic);
            bool confirmed;
            if (original == "coop")
            {
                var connection = LocalDualInstanceLauncher.Connection;
                if (connection == null || LocalDualInstanceLauncher.CompanionProcessExited)
                {
                    // No running companion to pause; a launch still owns the teammate, so the
                    // switch fails closed instead of treating "not yet connected" as paused.
                    confirmed = !runtime.DualLaunching && !runtime.TeamControlPending;
                }
                else
                {
                    var outcome = await runtime.ControlTeammateResultAsync(false, CancellationToken.None);
                    confirmed = outcome.Ok && outcome.Phase == "paused";
                }
            }
            else
            {
                confirmed = await runtime.PauseForModeSwitchAsync(CancellationToken.None);
            }

            if (!PlayModeSwitchPolicy.ShouldCommit(original, requested, confirmed)
                || runtime.Settings.OverlayPlayMode != original)
            {
                _modeSwitchNotice = Loc.T("暂停未确认，模式未切换。");
                return;
            }

            var settings = CloneSettings(runtime.Settings);
            settings.OverlayPlayMode = requested;
            runtime.SaveSettings(settings);
            _modeSwitchNotice = Loc.T("已切换，另一模式不会自动开始。");
        }
        catch (Exception ex)
        {
            // Never show a transport failure that might contain a configured key or raw request.
            _modeSwitchNotice = ex is IOException or UnauthorizedAccessException
                ? Loc.T("保存失败，模式未切换。")
                : Loc.T("暂停未确认，模式未切换。");
        }
        finally
        {
            runtime.EndPlayModeSwitch();
            _playModeSwitching = false;
            try { await GameThread.InvokeAsync(RefreshDynamic); }
            catch (InvalidOperationException) { } // Overlay may be shutting down.
        }
    }

    private async Task SaveSettingsAndSyncJevAsync()
    {
        await _companionSettingsGate.WaitAsync();
        _companionSettingsSyncing = true;
        try
        {
            await SyncCompanionJevSettingsAsync();
        }
        finally
        {
            _companionSettingsSyncing = false;
            _companionSettingsGate.Release();
            try { await GameThread.InvokeAsync(RefreshDynamic); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Store the host choice first, then confirm that the already-running companion applied it.</summary>
    private async Task SetCoopDualLayerAsync(bool enabled)
    {
        if (!_companionSettingsGate.Wait(0)) return;
        _companionSettingsSyncing = true;
        try
        {
            var next = CloneSettings(AgentRuntime.Instance.Settings);
            next.DualLayerCoopEnabled = enabled;
            AgentRuntime.Instance.SaveSettings(next);
            await SyncCompanionJevSettingsAsync();
        }
        catch (Exception ex)
        {
            _companionSettingsNotice = ex is IOException or UnauthorizedAccessException
                ? Loc.T("保存失败，队友设置未确认。")
                : Loc.T("队友设置未确认，请检查连接。");
        }
        finally
        {
            _companionSettingsSyncing = false;
            _companionSettingsGate.Release();
            try { await GameThread.InvokeAsync(RefreshDynamic); }
            catch (InvalidOperationException) { } // Overlay may be shutting down.
        }
    }

    /// <summary>Call after settings save as well as the coop toggle; never logs secret values.</summary>
    private static CompanionSettingsSnapshot CurrentCompanionSettings() => new(
        LocalDualInstanceLauncher.CompanionProcessExited ? null : LocalDualInstanceLauncher.Connection,
        AgentRuntime.Instance.Settings);

    private async Task SyncCompanionJevSettingsAsync()
    {
        try
        {
            var outcome = await _companionSettingsSynchronizer.SynchronizeAsync(CurrentCompanionSettings, CancellationToken.None);
            _companionSettingsNotice = outcome switch
            {
                CompanionSettingsSyncOutcome.Confirmed => Loc.T("队友设置已同步。"),
                CompanionSettingsSyncOutcome.Disconnected => Loc.T("队友未连接，下次邀请时生效。"),
                CompanionSettingsSyncOutcome.Superseded => Loc.T("设置已变更，等待队友确认…"),
                _ => Loc.T("队友 Jev 开关尚未同步。")
            };
            // The old request was acknowledged; a different desired snapshot now needs its own
            // confirmation. This does not replay an uncertain request or retry a failed control.
            if (outcome == CompanionSettingsSyncOutcome.Superseded) _ = SaveSettingsAndSyncJevAsync();
        }
        catch (Exception)
        {
            // A timeout is outcome-unknown. Never retry a control automatically or claim it succeeded.
            _companionSettingsSynchronizer.Reset();
            _companionSettingsNotice = Loc.T("队友设置未确认，请检查连接。");
        }
        try { await GameThread.InvokeAsync(RefreshDynamic); }
        catch (InvalidOperationException) { } // Overlay may be shutting down.
    }

    /// <summary>Never block the game tick on companion HTTP; drop stale connection results.</summary>
    private void RequestCompanionJevRefresh()
    {
        var connection = LocalDualInstanceLauncher.Connection;
        if (connection == null || LocalDualInstanceLauncher.CompanionProcessExited)
        {
            _companionSettingsSynchronizer.Reset();
            _companionJev = null;
            _companionJevSource = null;
            return;
        }
        var now = System.Environment.TickCount64;
        if (_companionJevSource != connection)
        {
            _companionJevSource = connection;
            _companionJev = null;
            _companionJevAtMs = 0;
            // Edits made while the second window was booting had no connection to push to.
            // Push the latest host settings once the verified connection becomes available.
            _ = SaveSettingsAndSyncJevAsync();
        }
        if (_companionJevRefreshing || now - _companionJevAtMs < 2000) return;
        _companionJevRefreshing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await connection.TryReadJevAsync(CancellationToken.None);
                if (ReferenceEquals(LocalDualInstanceLauncher.Connection, connection)) _companionJev = snapshot;
            }
            catch (Exception)
            {
                if (ReferenceEquals(LocalDualInstanceLauncher.Connection, connection)) _companionJev = null;
            }
            finally
            {
                _companionJevAtMs = System.Environment.TickCount64;
                _companionJevRefreshing = false;
                try { await GameThread.InvokeAsync(RefreshDynamic); }
                catch (InvalidOperationException) { } // Overlay may be shutting down.
            }
        });
    }

    private void RefreshModeControls(bool coopMode, bool dualEnabled)
    {
        if (_playModeSwitch is { } modeSwitch && modeSwitch.IsInsideTree()) modeSwitch.SetSelected(coopMode ? 1 : 0);
        if (_soloSection != null) _soloSection.Visible = !coopMode;
        if (_coopSection != null) _coopSection.Visible = coopMode;
        if (!coopMode && _companionJev != null) _companionJev = null;
        if (_modeSwitchStatus != null)
        {
            _modeSwitchStatus.Text = _modeSwitchNotice ?? string.Empty;
            _modeSwitchStatus.Visible = !string.IsNullOrWhiteSpace(_modeSwitchNotice);
        }
        if (_companionSettingsStatus != null)
        {
            _companionSettingsStatus.Text = _companionSettingsNotice ?? string.Empty;
            _companionSettingsStatus.Visible = !string.IsNullOrWhiteSpace(_companionSettingsNotice);
        }
        if (_companionMode != null) _companionMode.Disabled = LocalDualInstanceLauncher.CompanionProcessAlive || AgentRuntime.Instance.DualLaunching;
        if (_dualLayerCoopCheck != null) _dualLayerCoopCheck.Disabled = _companionSettingsSyncing;
        if (_jevPanel != null) _jevPanel.Visible = dualEnabled;
        if (_sendButton != null) _sendButton.Disabled = _playModeSwitching;
        if (_playToggle != null) _playToggle.Disabled = AgentRuntime.Instance.DualLaunching || _playModeSwitching || coopMode;
        var teamControlBlocked = InstanceRole.IsCompanion || AgentRuntime.Instance.TeamControlPending
            || AgentRuntime.Instance.DualLaunching || _playModeSwitching || !coopMode;
        if (_teamResume != null) _teamResume.Disabled = teamControlBlocked;
        if (_teamPause != null) _teamPause.Disabled = teamControlBlocked;
        RefreshContinueAvailability();
    }

    private void RefreshJevReading(bool coopMode, AgentSettings settings)
    {
        if (_jevStatus == null) return;
        if (coopMode && settings.DualLayerCoopEnabled) RequestCompanionJevRefresh();
        var companion = coopMode ? _companionJev : null;
        var sourceAvailable = !coopMode || (companion != null && _companionJevSource == LocalDualInstanceLauncher.Connection);
        var togglePending = coopMode && companion?.DualLayerCoopEnabled != settings.DualLayerCoopEnabled;
        _jevStatus.Text = !settings.HasJevConfigured()
            ? Loc.T("未配置 Jev，请先在「设置」填写。")
            : coopMode && !sourceAvailable ? Loc.T("队友 Jev 状态不可用，请检查连接。")
            : togglePending ? Loc.T("队友 Jev 开关尚未同步。")
            : Loc.T("Jev：{0}", settings.JevModel);
        if (_jevLastChoice != null)
            _jevLastChoice.Text = sourceAvailable ? Trim(coopMode ? companion?.Choice : AgentRuntime.Instance.LastJevChoice, 90) : "-";
        if (_jevProbabilities != null)
            _jevProbabilities.Text = sourceAvailable ? Trim(coopMode ? companion?.Probabilities : AgentRuntime.Instance.LastJevProbabilities, 90) : "-";
        if (_jevDanger != null)
            _jevDanger.Text = sourceAvailable ? Trim(coopMode ? companion?.Danger : AgentRuntime.Instance.LastJevDanger, 45) : "-";
        if (_jevLatency != null)
            _jevLatency.Text = sourceAvailable ? Trim(coopMode ? companion?.Latency : AgentRuntime.Instance.LastJevLatency, 45) : "-";
        if (_jevFallbackRate != null)
            _jevFallbackRate.Text = coopMode ? "-" : AgentRuntime.Instance.JevFallbackRate;
    }

    private async Task LaunchDualAsync()
    {
        FlushSettingsIfDirty();
        if (_settingsDirty) return;
        var settings = HarvestSettings();
        var companionAutoPlay = settings.CompanionControlMode != "external";
        await AgentRuntime.Instance.LaunchDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        RefreshDynamic();
    }

    private async Task ContinueDualAsync()
    {
        FlushSettingsIfDirty();
        if (_settingsDirty) return;
        var settings = HarvestSettings();
        var companionAutoPlay = settings.CompanionControlMode != "external";
        _continueLaunching = true;
        try
        {
            RefreshDynamic();
            await AgentRuntime.Instance.ContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        }
        finally
        {
            _continueLaunching = false;
        }
        RefreshDynamic();
    }

    private static string DualHintText()
    {
        var settings = AgentRuntime.Instance.Settings;
        if (settings.CompanionControlMode == "external")
            return Loc.T("外部 Agent 模式：队友入队后等待接管，不会自己出牌。");
        var ready = FirstRunSetup.Evaluate(settings);
        if (!ready.ReadyToInvite) return ready.Hint;
        return settings.CompanionAutoSelectCharacter
            ? Loc.T("在主菜单邀请，队友会自动选角并游玩。")
            : Loc.T("在主菜单邀请，到第二窗口为队友选角色。");
    }

    /// <summary>Continue is offered only where continue_ai_teammate would be accepted: host main menu with a co-op save on disk.</summary>
    private static bool CanOfferContinue()
    {
        if (InstanceRole.IsCompanion) return false;
        try
        {
            return GameStateService.CanContinueAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen());
        }
        catch (Exception ex)
        {
            WarnOnce("continue availability check failed", ex);
            return false;
        }
    }

    /// <summary>Invite is offered only where invite_ai_teammate would be advertised, including DualLaunching and PlayRunning.</summary>
    private static bool CanOfferInvite()
    {
        if (InstanceRole.IsCompanion) return false;
        try
        {
            return GameStateService.CanInviteAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen());
        }
        catch (Exception ex)
        {
            WarnOnce("invite availability check failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The screen behind the button changes with no runtime event the overlay could subscribe to, so
    /// availability is re-read on the panel tick as well as whenever the tab comes into view. Without
    /// the tick, a panel already sitting on this tab kept the button greyed out across the boot modal
    /// clearing, and only a tab switch brought it back.
    /// </summary>
    private void RefreshContinueAvailability()
    {
        // The mode switch gates both buttons too. This runs on the tick between refresh passes, and
        // the version that only re-read the screen re-enabled them in the middle of a mode switch.
        var blocked = AgentRuntime.Instance.DualLaunching
            || _playModeSwitching
            || AgentRuntime.Instance.Settings.OverlayPlayMode != "coop";
        if (_dualLaunchButton != null)
        {
            var canInvite = CanOfferInvite();
            _dualLaunchButton.Disabled = blocked || !canInvite;
        }

        if (_dualContinueButton == null)
        {
            return;
        }

        var canContinue = CanOfferContinue();
        _dualContinueButton.Disabled = blocked || !canContinue;
        _dualContinueButton.TooltipText = canContinue ? "" : Loc.T("主菜单有联机存档时可用");
    }
}
