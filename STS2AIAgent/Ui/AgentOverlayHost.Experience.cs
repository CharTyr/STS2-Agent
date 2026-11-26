using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// The settings card for the 2026-10 experience overhaul: game speed, forced moves, whole-turn plans,
/// combat arithmetic, screenshots, the in-game intent layer, the pause key and the run journal.
/// </summary>
/// <remarks>
/// These save on the spot, like the theme: each is a switch whose effect the player sees at the next
/// decision, and a Save button far below would be easy to miss. The form's own save clones the
/// persisted settings before overlaying its fields (<see cref="HarvestSettings"/>), so it never undoes
/// a choice made here.
/// </remarks>
internal sealed partial class AgentOverlayHost
{
    private static readonly string[] TurboModes = { AgentSettings.TurboOff, AgentSettings.TurboFast, AgentSettings.TurboInstant };
    private static readonly string[] VisionModes = { AgentSettings.VisionOff, AgentSettings.VisionOnDemand, AgentSettings.VisionEveryStep };
    private static readonly string[] HudModes = { AgentSettings.HudOff, AgentSettings.HudMinimal, AgentSettings.HudFull };
    private static readonly string[] PauseKeys = { "F7", "F6", "" };
    private static readonly int[] SpectatorDwells = { 0, 400, 800 };

    private Control? _experienceCard;
    private TextEdit? _playerPreferencesEdit;
    private Label? _playerPreferencesCounter;
    private Button? _economyPresetButton;
    private Button? _balancedPresetButton;
    private Button? _pushPresetButton;

    private Control BuildExperienceCard(AgentSettings settings, string? preservedPreferencesDraft = null)
    {
        var controls = new List<Control>();

        var activePreset = PlayStylePresetPolicy.Detect(settings);
        controls.Add(UiFactory.Label(Loc.T("一键游玩预设"), UiFactory.FontBody));
        _economyPresetButton = UiFactory.Button(
            PresetButtonText(Loc.T("省钱速通"), activePreset == PlayStylePresetPolicy.Economy),
            () => ApplyExperiencePreset(PlayStylePresetPolicy.Economy),
            activePreset == PlayStylePresetPolicy.Economy ? UiFactory.ButtonKind.Primary : UiFactory.ButtonKind.Normal);
        _balancedPresetButton = UiFactory.Button(
            PresetButtonText(Loc.T("均衡游玩"), activePreset == PlayStylePresetPolicy.Balanced),
            () => ApplyExperiencePreset(PlayStylePresetPolicy.Balanced),
            activePreset == PlayStylePresetPolicy.Balanced ? UiFactory.ButtonKind.Primary : UiFactory.ButtonKind.Normal);
        _pushPresetButton = UiFactory.Button(
            PresetButtonText(Loc.T("冲层观战"), activePreset == PlayStylePresetPolicy.Push),
            () => ApplyExperiencePreset(PlayStylePresetPolicy.Push),
            activePreset == PlayStylePresetPolicy.Push ? UiFactory.ButtonKind.Primary : UiFactory.ButtonKind.Normal);
        controls.Add(UiFactory.Row(_economyPresetButton, _balancedPresetButton, _pushPresetButton));

        controls.Add(UiFactory.Label(Loc.T("游戏速度"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { Loc.T("关闭"), Loc.T("快速"), Loc.T("极速") },
            Math.Max(0, Array.IndexOf(TurboModes, settings.TurboMode)),
            index => PersistExperience(s => s.TurboMode = TurboModes[index])));
        if (TurboController.OtherSpeedModLoaded())
        {
            controls.Add(UiFactory.Wrapped(Loc.T("检测到另一个加速模组（SpeedX），两者可以同时使用。"), 11));
        }

        controls.Add(ExperienceCheck(Loc.T("小事自动处理（不问模型）"), settings.AutopilotEnabled, (s, on) => s.AutopilotEnabled = on));
        controls.Add(ExperienceCheck(Loc.T("整回合规划（一次请求打完一回合）"), settings.TurnPlansEnabled, (s, on) => s.TurnPlansEnabled = on));
        controls.Add(ExperienceCheck(Loc.T("战斗算术（来袭伤害、斩杀、格挡）"), settings.CombatMathEnabled, (s, on) => s.CombatMathEnabled = on));

        controls.Add(UiFactory.Label(Loc.T("截图"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { Loc.T("不截图"), Loc.T("按需"), Loc.T("每一步") },
            Math.Max(0, Array.IndexOf(VisionModes, settings.VisionMode)),
            index => PersistExperience(s => s.VisionMode = VisionModes[index])));

        controls.Add(UiFactory.Label(Loc.T("游戏画面提示"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { Loc.T("关闭"), Loc.T("仅横幅"), Loc.T("完整") },
            Math.Max(0, Array.IndexOf(HudModes, settings.HudMode)),
            index => PersistExperience(s => s.HudMode = HudModes[index])));

        controls.Add(UiFactory.Label(Loc.T("出牌前高亮停顿"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { Loc.T("关闭"), Loc.T("短暂"), Loc.T("观战") },
            SpectatorDwellIndex(settings.SpectatorHighlightMs),
            index => PersistExperience(s => s.SpectatorHighlightMs = SpectatorDwells[index])));

        controls.Add(UiFactory.Label(Loc.T("暂停热键"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { "F7", "F6", Loc.T("不用热键") },
            Math.Max(0, Array.IndexOf(PauseKeys, settings.PauseHotkey ?? string.Empty)),
            index => PersistExperience(s => s.PauseHotkey = PauseKeys[index])));

        controls.Add(ExperienceCheck(Loc.T("记录战绩并提供给模型"), settings.RunJournalEnabled, (s, on) => s.RunJournalEnabled = on));

        controls.Add(UiFactory.Label(Loc.T("跨局风格偏好（可选）"), UiFactory.FontBody));
        var initialPreferences = preservedPreferencesDraft ?? settings.PlayerPreferences ?? string.Empty;
        var prefEdit = UiFactory.Multiline(initialPreferences, 60);
        prefEdit.PlaceholderText = Loc.T("例如：优先精简牌库、Act 1 血量健康尽量打精英、不盲拿高费牌…");
        prefEdit.CustomMinimumSize = new Vector2(UiFactory.MinReflowWidth, 60);
        _playerPreferencesCounter = UiFactory.Wrapped("", UiFactory.FontCaption);
        UpdatePreferencesCounter(initialPreferences);
        prefEdit.TextChanged += () =>
        {
            MarkSettingsDirty();
            UpdatePreferencesCounter(prefEdit.Text);
        };
        _playerPreferencesEdit = prefEdit;
        controls.Add(prefEdit);
        controls.Add(_playerPreferencesCounter);
        controls.Add(UiFactory.Row(
            UiFactory.Button(Loc.T("保存偏好"), () =>
            {
                if (PersistExperience(s => s.PlayerPreferences = prefEdit.Text))
                {
                    var saved = AgentRuntime.Instance.Settings.PlayerPreferences ?? string.Empty;
                    if (!string.Equals(prefEdit.Text, saved, StringComparison.Ordinal))
                    {
                        prefEdit.Text = saved;
                    }
                    UpdatePreferencesCounter(saved);
                }
            }, UiFactory.ButtonKind.Normal),
            UiFactory.Button(Loc.T("清空偏好"), () =>
            {
                if (PersistExperience(s => s.PlayerPreferences = string.Empty))
                {
                    prefEdit.Text = string.Empty;
                    UpdatePreferencesCounter(string.Empty);
                }
            }, UiFactory.ButtonKind.Ghost)));

        _experienceCard = HelpCard.Build(
            "settings.experience",
            Loc.T("游玩体验"),
            Loc.T("一键预设：省钱速通（极速+关闭截图+4k输出上限）、均衡游玩（推荐默认）、冲层观战（快速+完整画面提示+短暂出牌高亮停顿+8k输出上限）；不会改动你的端点或密钥。\n游戏速度：AI 自动游玩时把游戏自带的动画速度调快，暂停或停止后恢复你原来的设置；联机时最多用「快速」。不会改写你的游戏设置文件。\n小事自动处理：只剩一个选择、确认教学弹窗、无牌可出时结束回合这类不用判断的步骤，直接完成，不花模型请求。选牌、路线、商店、事件、药水永远交给模型。\n整回合规划：战斗中模型一次给出整回合的出牌顺序，Mod 每一步都用最新局面核对，局面一变就停下重新问。\n战斗算术：把来袭伤害、需要多少格挡、每张牌对每个敌人的真实伤害和能否斩杀算好交给模型。\n截图「按需」：只在局面看不懂或上一步出错时才附截图，更省更快。\n游戏画面提示与出牌前高亮停顿：在游戏画面上显示 AI 要出的牌、目标和理由；开启短暂/观战停顿可在高倍速下看清每一张出牌。\n跨局风格偏好：长期生效的打法备注（最多 400 字），放在静态前缀中不影响提示词缓存。"),
            controls.ToArray());
        return _experienceCard;
    }

    private void UpdatePreferencesCounter(string? text)
    {
        if (_playerPreferencesCounter == null)
        {
            return;
        }

        var len = text?.Length ?? 0;
        var over = len > AgentSettings.MaxPlayerPreferencesLength;
        _playerPreferencesCounter.Text = over
            ? Loc.T("已输入 {0} / {1} 字（保存时将截断）", len, AgentSettings.MaxPlayerPreferencesLength)
            : Loc.T("已输入 {0} / {1} 字", len, AgentSettings.MaxPlayerPreferencesLength);
        _playerPreferencesCounter.AddThemeColorOverride(
            "font_color",
            UiFactory.ToGodot(over ? UiFactory.Palette.Warning : UiFactory.Palette.Muted));
    }

    private static string PresetButtonText(string label, bool active) => active ? "● " + label : label;

    private void RefreshPresetButtonLabels(AgentSettings settings)
    {
        var active = PlayStylePresetPolicy.Detect(settings);
        if (_economyPresetButton != null) _economyPresetButton.Text = PresetButtonText(Loc.T("省钱速通"), active == PlayStylePresetPolicy.Economy);
        if (_balancedPresetButton != null) _balancedPresetButton.Text = PresetButtonText(Loc.T("均衡游玩"), active == PlayStylePresetPolicy.Balanced);
        if (_pushPresetButton != null) _pushPresetButton.Text = PresetButtonText(Loc.T("冲层观战"), active == PlayStylePresetPolicy.Push);
    }

    private static int SpectatorDwellIndex(int ms) => ms switch
    {
        <= 0 => 0,
        <= 500 => 1,
        _ => 2
    };

    private void ApplyExperiencePreset(string preset)
    {
        if (!PersistExperience(settings => PlayStylePresetPolicy.Apply(settings, preset)))
        {
            return;
        }

        // Replace only the experience card in place (like ApplyThemeChoice) so unsaved edits
        // elsewhere on the Settings page and any typed playstyle draft are never wiped.
        var previous = _experienceCard;
        var parent = previous?.GetParent();
        if (parent != null && previous != null)
        {
            var draft = _playerPreferencesEdit?.Text;
            var index = previous.GetIndex();
            parent.RemoveChild(previous);
            previous.QueueFree();
            var replacement = BuildExperienceCard(CloneSettings(AgentRuntime.Instance.Settings), draft);
            parent.AddChild(replacement);
            parent.MoveChild(replacement, index);
        }

        if (_maxOutputTokensEdit != null && !_settingsDirty)
        {
            var wasRebuilding = _rebuildingSettings;
            _rebuildingSettings = true;
            try
            {
                _maxOutputTokensEdit.Text = AgentRuntime.Instance.Settings.MaxOutputTokens?.ToString() ?? "";
            }
            finally
            {
                _rebuildingSettings = wasRebuilding;
            }
        }
    }

    private string? _pauseHotkeyName;
    private Key _pauseHotkey = Key.None;
    private bool _pauseHotkeyWasDown;

    /// <summary>
    /// The pause key (default F7): pauses auto-play while it runs, starts it again when paused -- the
    /// same path as the play page's button. F8 only shows or hides the window, which players took for
    /// a pause. F6/F7 are bound by the game only in trailer mode (<c>NInputManager</c> returns early
    /// unless <c>NGame.IsTrailerMode</c>), so they are free in normal play. A key equal to the window
    /// key is ignored rather than doing both.
    /// </summary>
    private void PollPauseHotkey()
    {
        var name = AgentRuntime.Instance.Settings.PauseHotkey ?? string.Empty;
        if (!string.Equals(name, _pauseHotkeyName, StringComparison.Ordinal))
        {
            _pauseHotkeyName = name;
            _pauseHotkey = name.Length > 0 && OverlayHotkey.TryParse(name, out var key) && key != _hotkey ? key : Key.None;
            _pauseHotkeyWasDown = false;
        }

        if (_pauseHotkey == Key.None)
        {
            return;
        }

        var down = Input.IsPhysicalKeyPressed(_pauseHotkey);
        if (down && !_pauseHotkeyWasDown && !IsTypingIntoTextBox(_pauseHotkey))
        {
            if (AgentRuntime.Instance.PlayRunning && AgentRuntime.Instance.InSpectatorDwell)
            {
                AgentRuntime.Instance.InterceptAndPause();
            }
            else
            {
                TogglePlay();
            }
        }

        _pauseHotkeyWasDown = down;
    }

    private CheckBox ExperienceCheck(string label, bool on, Action<AgentSettings, bool> apply)
    {
        var check = UiFactory.Check(label, on);
        check.Toggled += value => PersistExperience(settings => apply(settings, value));
        return check;
    }

    /// <summary>
    /// Writes one experience switch to the settings file, leaving every other field (and any unsaved
    /// form edit) alone -- the same rule as the theme picker.
    /// </summary>
    private bool PersistExperience(Action<AgentSettings> mutate)
    {
        var settings = CloneSettings(AgentRuntime.Instance.Settings);
        mutate(settings);
        try
        {
            AgentRuntime.Instance.SaveSettings(settings);
            RefreshPresetButtonLabels(AgentRuntime.Instance.Settings);
            if (_saveStatus != null) _saveStatus.Text = Loc.T(_settingsDirty ? "未保存" : "已保存");
            // Keep an already-running AI teammate in sync with updated play/preset/preference settings.
            _ = SaveSettingsAndSyncJevAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveStatus != null) _saveStatus.Text = FormatSettingsNotice();
            return false;
        }
    }
}
