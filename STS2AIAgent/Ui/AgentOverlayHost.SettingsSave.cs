using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// Committing the settings form: save, add and remove endpoints and models, the dirty flag the
/// editors raise, and the notice line that reports how the last save went.
/// </summary>
/// <remarks>
/// Moved out of <c>AgentOverlayHost.cs</c>, which held these between the window chrome and the panel
/// tick. The form that builds the editors and the harvest that reads them stay in
/// <c>AgentOverlayHost.Settings.cs</c>.
/// </remarks>
internal sealed partial class AgentOverlayHost
{

    private void AddEndpoint()
    {
        var settings = HarvestSettings();
        settings.Endpoints.Add(new LlmEndpoint { Name = Loc.T("新端点") });
        PersistHarvested(settings);
    }

    private void AddModel()
    {
        var settings = HarvestSettings();
        settings.Models.Add(new LlmModelConfig
        {
            EndpointId = settings.Endpoints.FirstOrDefault()?.Id ?? string.Empty,
            Model = "gpt-4o",
            DisplayName = Loc.T("新模型"),
            ThinkingIntensity = "medium"
        });
        PersistHarvested(settings);
    }

    private void RemoveEndpoint(int index)
    {
        var settings = HarvestSettings();
        if (index < 0 || index >= settings.Endpoints.Count)
        {
            return;
        }

        var impact = SettingsBinding.EndpointRemoval(settings, settings.Endpoints[index].Id);
        if (impact.Blocked)
        {
            ShowDeleteWarning(impact.Message);
            return;
        }

        settings.Endpoints.RemoveAt(index);
        ModelRoleProbe.InvalidateMismatched(settings);
        PersistHarvested(settings);
    }

    private void RemoveModel(int index)
    {
        var settings = HarvestSettings();
        if (index < 0 || index >= settings.Models.Count)
        {
            return;
        }

        var impact = SettingsBinding.ModelRemoval(settings, settings.Models[index].Id);
        if (impact.Blocked)
        {
            ShowDeleteWarning(impact.Message);
            return;
        }

        settings.Models.RemoveAt(index);
        ModelRoleProbe.InvalidateMismatched(settings);
        PersistHarvested(settings);
    }

    private void ShowDeleteWarning(string message)
    {
        if (_deleteWarning != null)
        {
            _deleteWarning.Text = message;
            _deleteWarning.Visible = !string.IsNullOrEmpty(message);
        }
    }

    private void SaveSettingsFromUi()
    {
        _lastSettingsSaveSucceeded = false;
        AgentSettings settings;
        try { settings = HarvestSettings(); }
        catch (ArgumentException ex) { SetSaveStatus(ex.Message); return; }
        ModelRoleProbe.InvalidateMismatched(settings);
        var saveText = string.IsNullOrWhiteSpace(_budgetInputError)
            ? Loc.T("已保存")
            : Loc.T("已保存（预算未改：{0}）", _budgetInputError);
        if (!PersistHarvested(settings))
        {
            return;
        }

        // The saved host settings are authoritative. Forward the whitelisted play and Jev fields
        // to an already-running companion, then display its independent confirmation status.
        _lastSettingsSaveSucceeded = true;
        _ = SaveSettingsAndSyncJevAsync();

        if (_saveStatus != null)
        {
            _saveStatus.Text = saveText;
        }

        if (_budgetHint != null && !string.IsNullOrWhiteSpace(_budgetInputError))
        {
            _budgetHint.Text = _budgetInputError;
            _budgetHint.Visible = true;
        }
    }

    private bool PersistHarvested(AgentSettings settings)
    {
        try
        {
            var themeChanged = !string.Equals(
                OverlayThemeCatalog.Normalize(settings.OverlayTheme),
                UiFactory.Palette.Id,
                StringComparison.Ordinal);
            AgentRuntime.Instance.SaveSettings(settings);
            _settingsDirty = false;
            RebuildSettingsForm();
            if (themeChanged)
            {
                // The palette is baked into every stylebox at build time, so a saved theme needs a
                // rebuild rather than a repaint. Same path a language change takes, so the window
                // keeps its position, its visibility and the tab the player is on.
                RebuildInPlace();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveStatus != null)
            {
                _saveStatus.Text = FormatSettingsNotice(fallback: Loc.T("保存失败，原配置未改动。"));
            }

            return false;
        }
    }

    private void ResetSessionStatsFromUi()
    {
        AgentRuntime.Instance.TryResetSessionStats(out _);
        RefreshDynamic();
    }

    private static string FormatSettingsNotice(string? fallback = null)
    {
        var notice = AgentRuntime.Instance.SettingsNotice;
        if (notice.HasMessage)
        {
            var text = notice.Message;
            if (!string.IsNullOrWhiteSpace(notice.BackupPath))
            {
                text += Loc.T(" 备份：{0}", notice.BackupPath);
            }

            return text;
        }

        return fallback ?? "";
    }

    private void MarkSettingsDirty()
    {
        if (_rebuildingSettings)
        {
            return;
        }

        _settingsDirty = true;
        if (_saveStatus != null)
        {
            _saveStatus.Text = Loc.T("未保存");
        }
    }

    private void WatchLine(LineEdit edit)
    {
        edit.TextChanged += _ => MarkSettingsDirty();
    }

    private void WatchCheck(CheckBox check)
    {
        check.Toggled += _ => MarkSettingsDirty();
    }

    private void WatchCombo(OptionButton combo)
    {
        combo.ItemSelected += _ => MarkSettingsDirty();
    }

    private void FlushSettingsIfDirty()
    {
        if (!_settingsDirty || _settingsBody == null)
        {
            return;
        }

        SaveSettingsFromUi();
    }
}
