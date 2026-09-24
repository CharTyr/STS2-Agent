using Godot;
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
        _modeSwitchNotice = Loc.T("正在暂停当前模式；已提交的动作会先完成…");
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
                _modeSwitchNotice = Loc.T("当前模式尚未确认暂停，仍保持原模式。");
                return;
            }

            var settings = CloneSettings(runtime.Settings);
            settings.OverlayPlayMode = requested;
            runtime.SaveSettings(settings);
            _modeSwitchNotice = Loc.T("已切换模式。另一模式不会自动开始。");
        }
        catch (Exception ex)
        {
            // Never show a transport failure that might contain a configured key or raw request.
            _modeSwitchNotice = ex is IOException or UnauthorizedAccessException
                ? Loc.T("保存模式失败，仍保持原模式。")
                : Loc.T("无法确认暂停，仍保持原模式。");
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
        if (!_companionSettingsGate.Wait(0)) return;
        try
        {
            var after = AgentRuntime.Instance.Settings;
            var newJev = CompanionSettingsPatch.From(after);
            if (ReferenceEquals(_lastConfirmedCompanionConnection, LocalDualInstanceLauncher.Connection) &&
                _lastConfirmedCompanionSettings == newJev.Fingerprint()) return;
            await SyncCompanionJevSettingsAsync(after);
        }
        finally { _companionSettingsGate.Release(); }
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
            await SyncCompanionJevSettingsAsync(next);
        }
        catch (Exception ex)
        {
            _companionSettingsNotice = ex is IOException or UnauthorizedAccessException
                ? Loc.T("保存 Jev 配置失败；队友配置未确认。")
                : Loc.T("队友配置未确认，请检查连接后重试。");
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
    private async Task SyncCompanionJevSettingsAsync(AgentSettings settings)
    {
        var connection = LocalDualInstanceLauncher.Connection;
        if (connection == null || LocalDualInstanceLauncher.CompanionProcessExited)
        {
            _lastConfirmedCompanionConnection = null;
            _lastConfirmedCompanionSettings = null;
            _companionSettingsNotice = Loc.T("队友未连接；设置已保存，下次邀请时生效。");
            return;
        }
        try
        {
            var patch = CompanionSettingsPatch.From(settings);
            var effective = await connection.ApplyJevSettingsAsync(patch, CancellationToken.None);
            if (effective == settings.DualLayerCoopEnabled && ReferenceEquals(LocalDualInstanceLauncher.Connection, connection))
            {
                _lastConfirmedCompanionConnection = connection;
                _lastConfirmedCompanionSettings = patch.Fingerprint();
            }
            _companionSettingsNotice = effective == settings.DualLayerCoopEnabled
                ? Loc.T("队友 Jev 配置已确认，下次决策生效。")
                : Loc.T("队友 Jev 开关与本机不一致，尚未确认生效。");
            if (effective != settings.DualLayerCoopEnabled) _lastConfirmedCompanionSettings = null;
        }
        catch (Exception)
        {
            // A timeout is outcome-unknown. Never retry a control automatically or claim it succeeded.
            _lastConfirmedCompanionSettings = null;
            _companionSettingsNotice = Loc.T("队友配置未确认，请检查连接后重试。");
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
            _lastConfirmedCompanionConnection = null;
            _lastConfirmedCompanionSettings = null;
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
        if (_dualLayerCoopCheck != null) _dualLayerCoopCheck.Disabled = _companionSettingsSyncing;
        if (_jevPanel != null) _jevPanel.Visible = dualEnabled;
        if (_sendButton != null) _sendButton.Disabled = _playModeSwitching;
        if (_playToggle != null) _playToggle.Disabled = AgentRuntime.Instance.DualLaunching || _playModeSwitching || coopMode;
        if (_teamResume != null) _teamResume.Disabled = AgentRuntime.Instance.TeamControlPending
            || AgentRuntime.Instance.DualLaunching || _playModeSwitching || !coopMode;
        if (_teamPause != null) _teamPause.Disabled = AgentRuntime.Instance.TeamControlPending
            || AgentRuntime.Instance.DualLaunching || _playModeSwitching || !coopMode;
        if (_dualLaunchButton != null) _dualLaunchButton.Disabled = AgentRuntime.Instance.DualLaunching
            || !CanOfferInvite() || _playModeSwitching || !coopMode;
        if (_dualContinueButton != null) _dualContinueButton.Disabled = AgentRuntime.Instance.DualLaunching
            || !CanOfferContinue() || _playModeSwitching || !coopMode;
    }

    private void RefreshJevReading(bool coopMode, AgentSettings settings)
    {
        if (_jevStatus == null) return;
        if (coopMode && settings.DualLayerCoopEnabled) RequestCompanionJevRefresh();
        var companion = coopMode ? _companionJev : null;
        var sourceAvailable = !coopMode || (companion != null && _companionJevSource == LocalDualInstanceLauncher.Connection);
        var togglePending = coopMode && companion?.DualLayerCoopEnabled != settings.DualLayerCoopEnabled;
        _jevStatus.Text = !settings.HasJevConfigured()
            ? Loc.T("Jev 未配置。到设置页填写 API Key 后即可用双层决策。")
            : coopMode && !sourceAvailable ? Loc.T("队友 Jev 状态暂不可用，请检查连接与配置同步。")
            : togglePending ? Loc.T("队友 Jev 开关与本机不一致，尚未确认生效。")
            : Loc.T("Jev 已配置（{0}）。双层决策开启后由 Jev 逐步操作，LLM 只调整策略。", settings.JevModel);
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
}
