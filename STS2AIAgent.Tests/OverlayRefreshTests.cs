using System.Globalization;
using STS2AIAgent.Ui;

namespace STS2AIAgent.Tests;

/// <summary>
/// The overlay cleanup of 2026-10-26: the Godot-free helpers it introduced, and the source contracts
/// for the defects it fixed.
/// </summary>
/// <remarks>
/// Each source contract names the defect it guards, because the defect is what a later edit would
/// bring back without noticing. The helpers are tested directly; the controls that use them cannot
/// be built offline.
/// </remarks>
internal static class OverlayRefreshTests
{
    private const string Host = "STS2AIAgent/Ui/AgentOverlayHost.cs";
    private const string Settings = "STS2AIAgent/Ui/AgentOverlayHost.Settings.cs";
    private const string Factory = "STS2AIAgent/Ui/UiFactory.cs";

    /// <summary>
    /// Brackets render as written. The old escape swapped them for full-width ones, so a card named
    /// "Strike [+]" was shown as "Strike ［+］".
    /// </summary>
    public static void EscapeKeepsBracketsAsWritten()
    {
        Assert.Equal("Strike [lb]+[rb]", BbText.Escape("Strike [+]"));
        // One pass: the ] inside a freshly inserted [lb] must not be escaped a second time.
        Assert.Equal("[lb]b[rb]x[lb]/b[rb]", BbText.Escape("[b]x[/b]"));
        Assert.Equal("plain", BbText.Escape("plain"));
        Assert.Equal(string.Empty, BbText.Escape(null));
        Assert.False(BbText.Escape("Strike [+]").Contains('［'), "Brackets are being swapped for full-width ones again.");
    }

    /// <summary>A coloured span escapes its text, so a model's reason cannot restyle the log.</summary>
    public static void ColoredEscapesItsText()
    {
        Assert.Equal("[color=#aabbcc]a[lb]b[rb][/color]", BbText.Colored("aabbcc", "a[b]"));
        Assert.Equal("[color=#aabbcc][b]you[/b][/color]", BbText.Colored("aabbcc", "you", bold: true));
    }

    /// <summary>
    /// An unchanged log is left alone, a grown one is appended to, anything else is redrawn. The
    /// first outcome is the fix: the logs used to be cleared and redrawn several times a second.
    /// </summary>
    public static void RichLogTouchesOnlyWhatChanged()
    {
        var shown = new[] { "a", "b" };
        Assert.Equal(RichLogChange.None, RichLogDiff.Plan(shown, new[] { "a", "b" }));
        Assert.Equal(RichLogChange.Append, RichLogDiff.Plan(shown, new[] { "a", "b", "c" }));
        Assert.Equal(RichLogChange.Append, RichLogDiff.Plan(Array.Empty<string>(), new[] { "a" }));
        Assert.Equal(RichLogChange.None, RichLogDiff.Plan(Array.Empty<string>(), Array.Empty<string>()));
        // The streamed reasoning bubble changes in place: that is a redraw, never an append.
        Assert.Equal(RichLogChange.Replace, RichLogDiff.Plan(shown, new[] { "a", "b2" }));
        // A trimmed history or a cleared chat shrinks the list.
        Assert.Equal(RichLogChange.Replace, RichLogDiff.Plan(shown, new[] { "a" }));
        Assert.Equal(RichLogChange.Replace, RichLogDiff.Plan(shown, Array.Empty<string>()));
        // A new turn at the top of the newest-first decision log.
        Assert.Equal(RichLogChange.Replace, RichLogDiff.Plan(shown, new[] { "z", "a", "b" }));
    }

    /// <summary>
    /// The Jev threshold reads the same on every system locale. With the current culture, "0.35" on
    /// a German Windows parsed as 35, every confidence fell below it, and dual-layer mode silently
    /// handed every turn back to the LLM.
    /// </summary>
    public static void ThresholdIgnoresTheSystemLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(SettingsInput.TryParseThreshold("0.35", out var dot));
            Assert.Equal(0.35, dot);
            Assert.True(SettingsInput.TryParseThreshold(" 0,5 ", out var comma));
            Assert.Equal(0.5, comma);
            Assert.Equal("0.35", SettingsInput.FormatThreshold(0.35));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.True(SettingsInput.TryParseThreshold("1", out var one));
        Assert.Equal(1.0, one);
        Assert.True(SettingsInput.TryParseThreshold("0", out _));
        // Out of range or unreadable: rejected, so the caller keeps the previous value.
        Assert.False(SettingsInput.TryParseThreshold("35", out _));
        Assert.False(SettingsInput.TryParseThreshold("-0.1", out _));
        Assert.False(SettingsInput.TryParseThreshold("abc", out _));
        Assert.False(SettingsInput.TryParseThreshold("", out _));
        Assert.False(SettingsInput.TryParseThreshold(null, out _));
        Assert.False(SettingsInput.TryParseThreshold("NaN", out _));

        var harvest = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Settings), "HarvestSettings");
        Assert.Contains("SettingsInput.TryParseThreshold(_jevThresholdEdit.Text, out var threshold)", harvest);
        Assert.False(harvest.Contains("double.TryParse(", StringComparison.Ordinal),
            "The Jev threshold is parsed with the system culture again.");
    }

    /// <summary>
    /// The team chat and the decision log go through the same wrapper as the conversation, and the
    /// MCP config box is only written when its text changed: assigning a TextEdit resets its
    /// selection, so a player selecting the config to copy it lost the selection on every refresh.
    /// </summary>
    public static void LiveViewsAreNotRedrawnWhenNothingChanged()
    {
        var overlay = AgentSourceFixture.ReadOverlayHost();
        Assert.Contains("_teamChatView.Show(team);", AgentSourceFixture.MethodBody(overlay, "RefreshCoopSection"));
        Assert.Contains("_decisionLogView.Show(lines);", AgentSourceFixture.MethodBody(overlay, "RefreshDecisionPage"));
        Assert.False(overlay.Contains("_teamChat.Clear()", StringComparison.Ordinal),
            "The team chat clears its label on every refresh again.");
        Assert.False(overlay.Contains("_decisionLog.Clear()", StringComparison.Ordinal),
            "The decision log clears its label on every refresh again.");

        var connect = AgentSourceFixture.MethodBody(overlay, "RefreshConnectSection");
        Assert.Contains("_mcpConfigEdit.Text != mcpConfig", connect);
    }

    /// <summary>
    /// A theme change repaints a tab as a tab. The repaint walk reaches tabs through its Button arm,
    /// and repainting them as ordinary buttons erased the selected-tab fill, so after picking a theme
    /// neither the header nor the solo/multiplayer switch showed where the player was.
    /// </summary>
    public static void ThemeRepaintKeepsTheSelectedTab()
    {
        var factory = AgentSourceFixture.Read(Factory);
        var tab = AgentSourceFixture.MethodBody(factory, "TabButton");
        Assert.Contains("button.SetMeta(MetaTabActive, active);", tab);
        var repaint = AgentSourceFixture.MethodBody(factory, "ReapplyButtonTheme");
        Assert.Contains("if (button.HasMeta(MetaTabActive))", repaint);
        Assert.True(
            repaint.IndexOf("MetaTabActive", StringComparison.Ordinal) < repaint.IndexOf("ApplyButtonTheme(button, kind)", StringComparison.Ordinal),
            "Tabs have to be recognised before the ordinary button repaint.");

        // The title is accent-coloured through the factory, so the repaint keeps it accent-coloured.
        var header = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Host), "BuildHeader");
        Assert.Contains("UiFactory.MarkAccent(UiFactory.Label(\"STS2 AI Agent\"", header);
    }

    /// <summary>
    /// The settings jump bar only offers sections that are on the form. Endpoint, model and binding
    /// sections exist only with the advanced options shown, and their buttons used to stay on the bar
    /// and do nothing when pressed.
    /// </summary>
    public static void JumpBarOffersOnlyPresentSections()
    {
        var settings = AgentSourceFixture.Read(Settings);
        var refresh = AgentSourceFixture.MethodBody(settings, "RefreshSettingsJumpBar");
        Assert.Contains("button.Visible = _settingsAnchors.ContainsKey(id);", refresh);
        var form = AgentSourceFixture.MethodBody(settings, "RebuildSettingsForm");
        var finallyBlock = form[form.LastIndexOf("finally", StringComparison.Ordinal)..];
        Assert.Contains("RefreshSettingsJumpBar();", finallyBlock);
    }

    /// <summary>
    /// A letter hotkey does not fire while the player is typing, and is parsed once rather than on
    /// every frame.
    /// </summary>
    public static void HotkeyIgnoresTypingIntoTextBoxes()
    {
        var host = AgentSourceFixture.Read(Host);
        var tick = AgentSourceFixture.MethodBody(host, "OnProcessFrame");
        Assert.Contains("!IsTypingIntoTextBox(_hotkey)", tick);
        Assert.False(tick.Contains("TryParse(hotkeyName, out var", StringComparison.Ordinal),
            "The hotkey is parsed on every frame again.");
        var typing = AgentSourceFixture.MethodBody(host, "IsTypingIntoTextBox");
        Assert.Contains("GuiGetFocusOwner() is LineEdit or TextEdit", typing);

        // A bare number used to parse through Enum.TryParse into an arbitrary key code.
        var parse = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Ui/OverlayHotkey.cs"), "TryParse");
        Assert.Contains("!char.IsDigit(value[0])", parse);
        Assert.Contains("key != Key.None", parse);
    }

    /// <summary>
    /// Settings editors are matched to their endpoint or model by id, and a model test does not wipe
    /// edits typed while it ran.
    /// </summary>
    public static void SettingsEditsAreNotLostOrMisfiled()
    {
        var harvest = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Settings), "HarvestSettings");
        Assert.Contains("MatchById(current.Endpoints, i, editor.Id, item => item.Id)", harvest);
        Assert.Contains("MatchById(current.Models, i, editor.Id, item => item.Id)", harvest);

        var test = AgentSourceFixture.DeclarationBody(
            AgentSourceFixture.ReadOverlayHost(), "private async Task RunModelTestFromUiAsync(string? modelId)");
        var dirty = test.IndexOf("if (_settingsDirty)", StringComparison.Ordinal);
        Assert.True(dirty >= 0, "A finished model test rebuilds the form without checking for unsaved edits.");
        Assert.True(test.IndexOf("RefreshModelTestBadges();", StringComparison.Ordinal) > dirty,
            "A dirty form should only have its test badges updated in place.");
    }

    /// <summary>A language or theme rebuild keeps half-typed chat and team messages.</summary>
    public static void RebuildKeepsMessageDrafts()
    {
        var rebuild = AgentSourceFixture.MethodBody(AgentSourceFixture.Read(Host), "RebuildInPlace");
        var draft = rebuild.IndexOf("var chatDraft = _chatInput?.Text", StringComparison.Ordinal);
        var teardown = rebuild.IndexOf("TearDown();", StringComparison.Ordinal);
        Assert.True(draft >= 0 && draft < teardown, "The chat draft has to be read before the old controls are freed.");
        Assert.Contains("_chatInput.Text = chatDraft;", rebuild);
        Assert.Contains("_teamInput.Text = teamDraft;", rebuild);
    }

    /// <summary>
    /// The window fits a short viewport. The 520-pixel floor pushed the footer off the bottom of any
    /// window shorter than about 540 pixels.
    /// </summary>
    public static void PanelFitsShortWindows()
    {
        var size = AgentSourceFixture.MethodBody(AgentSourceFixture.Read("STS2AIAgent/Ui/OverlayPlacement.cs"), "PanelSizeFor");
        Assert.Contains("height = Math.Min(height, Math.Max(240f, viewport.Y - 16f));", size);
    }
}
