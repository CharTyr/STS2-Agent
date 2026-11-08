using Godot;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>Non-interactive game-screen intent layer. Updated only on the game thread.</summary>
internal sealed partial class AgentHud : Control
{
    private readonly Label _banner = UiFactory.Wrapped("", UiFactory.FontBody);
    private readonly Label _reason = UiFactory.Wrapped("", UiFactory.FontBody);
    private Rect2? _card;
    private Vector2? _target;
    private Color _accent;
    private ulong _lastTick;
    private LiveIntent? _lastIntent;

    public AgentHud()
    {
        Name = "STS2AgentHud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _banner.MouseFilter = MouseFilterEnum.Ignore;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _banner.AddThemeConstantOverride("shadow_offset_x", 2);
        _banner.AddThemeConstantOverride("shadow_offset_y", 2);
        _reason.MouseFilter = MouseFilterEnum.Ignore;
        _reason.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _reason.AddThemeConstantOverride("shadow_offset_x", 2);
        _reason.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(_banner);
        AddChild(_reason);
    }

    public void Refresh(AgentRuntime runtime, bool captureHidden)
    {
        var tick = Time.GetTicksMsec();
        var intentChanged = !ReferenceEquals(_lastIntent, runtime.CurrentIntent);
        if (tick - _lastTick < 100 && !intentChanged) return;
        _lastIntent = runtime.CurrentIntent;
        _lastTick = tick;
        var settings = runtime.Settings;
        Visible = !captureHidden && settings.HudMode != AgentSettings.HudOff;
        if (!Visible) return;
        _accent = UiFactory.ToGodot(UiFactory.Palette.Accent);
        var width = GetViewportRect().Size.X;
        // Below the game's own top bar and relic row: at y=14 the banner sat on the floor counter and
        // potion slots (observed live 2026-10-29).
        _banner.Position = new Vector2(Math.Max(12, (width - 680) / 2), Math.Max(110, GetViewportRect().Size.Y * 0.16f));
        _banner.Size = new Vector2(Math.Min(680, width - 24), 86);
        var speed = settings.TurboMode switch
        {
            AgentSettings.TurboInstant => Loc.T("极速"), AgentSettings.TurboFast => Loc.T("快速"), _ => Loc.T("关闭")
        };
        _banner.Text = runtime.PlayRunning
            ? Loc.T("AI 正在游玩 · {0} · {1} 暂停 · {2} 打开面板", speed, settings.PauseHotkey, settings.Hotkey)
                + "\n" + runtime.StatusWithElapsed
            : Loc.T("AI 已暂停 · {0} 继续 · {1} 打开面板", settings.PauseHotkey, settings.Hotkey);
        _card = null;
        _target = null;
        _reason.Visible = false;
        var intent = runtime.CurrentIntent;
        string screen;
        try { screen = GameStateService.CurrentScreenName(); }
        catch (Exception) { screen = "UNKNOWN"; }
        if (settings.HudMode == AgentSettings.HudFull && HudModel.ShowIntent(intent, screen, runtime.PlayRunning, DateTimeOffset.UtcNow))
        {
            var anchors = HudAnchors.Read(intent!);
            _card = anchors.Card;
            _target = anchors.Target;
            _banner.Text += "\n" + HudModel.IntentText(intent!);
            _reason.Text = intent!.Reason ?? "";
            _reason.Visible = _reason.Text.Length > 0;
            var point = anchors.Player ?? new Vector2(width / 2, 160);
            _reason.Position = new Vector2(Math.Clamp(point.X - 180, 12, Math.Max(12, width - 372)), Math.Max(110, point.Y - 90));
            _reason.Size = new Vector2(Math.Min(360, width - 24), 80);
            _reason.Modulate = new Color(1, 1, 1, (float)Math.Clamp(1 - (DateTimeOffset.UtcNow - intent.UpdatedAt).TotalSeconds / 4, 0, 1));
        }
        if (runtime.IntentPlanPreview.Length > 0 && runtime.PlayRunning) _banner.Text += "\n" + runtime.IntentPlanPreview;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_card is not Rect2 card) return;
        DrawRect(card.Grow(5), _accent, filled: false, width: 3, antialiased: true);
        if (_target is not Vector2 target) return;
        var from = card.GetCenter();
        DrawLine(from, target, _accent, 3, antialiased: true);
        DrawCircle(target, 9, _accent, filled: false, width: 3, antialiased: true);
    }
}
