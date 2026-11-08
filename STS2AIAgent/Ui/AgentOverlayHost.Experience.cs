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

    private Control BuildExperienceCard(AgentSettings settings)
    {
        var controls = new List<Control>();

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

        controls.Add(UiFactory.Label(Loc.T("暂停热键"), UiFactory.FontBody));
        controls.Add(UiFactory.SegmentedSwitch(
            new[] { "F7", "F6", Loc.T("不用热键") },
            Math.Max(0, Array.IndexOf(PauseKeys, settings.PauseHotkey ?? string.Empty)),
            index => PersistExperience(s => s.PauseHotkey = PauseKeys[index])));

        controls.Add(ExperienceCheck(Loc.T("记录战绩并提供给模型"), settings.RunJournalEnabled, (s, on) => s.RunJournalEnabled = on));

        return HelpCard.Build(
            "settings.experience",
            Loc.T("游玩体验"),
            Loc.T("游戏速度：AI 自动游玩时把游戏自带的动画速度调快，暂停或停止后恢复你原来的设置；联机时最多用「快速」。不会改写你的游戏设置文件。\n小事自动处理：只剩一个选择、确认教学弹窗、无牌可出时结束回合这类不用判断的步骤，直接完成，不花模型请求。选牌、路线、商店、事件、药水永远交给模型。\n整回合规划：战斗中模型一次给出整回合的出牌顺序，Mod 每一步都用最新局面核对，局面一变就停下重新问。\n战斗算术：把来袭伤害、需要多少格挡、每张牌对每个敌人的真实伤害和能否斩杀算好交给模型。\n截图「按需」：只在局面看不懂或上一步出错时才附截图，更省更快。\n游戏画面提示：在游戏画面上显示 AI 要出的牌、目标和理由；截图时会自动隐藏。"),
            controls.ToArray());
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
            TogglePlay();
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
    private void PersistExperience(Action<AgentSettings> mutate)
    {
        var settings = CloneSettings(AgentRuntime.Instance.Settings);
        mutate(settings);
        try
        {
            AgentRuntime.Instance.SaveSettings(settings);
            if (_saveStatus != null) _saveStatus.Text = Loc.T(_settingsDirty ? "未保存" : "已保存");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveStatus != null) _saveStatus.Text = FormatSettingsNotice();
        }
    }
}
