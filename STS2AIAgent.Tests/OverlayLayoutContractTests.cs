using System.IO;

namespace STS2AIAgent.Tests;

/// <summary>
/// The overlay's page layouts, pinned where a screenshot cannot pin them.
/// </summary>
/// <remarks>
/// Layout is the half of the UI that the offline suite can check at all: these pages are Godot
/// controls, so nothing here can measure a rendered rectangle. What it can do is hold the decisions
/// that were made for a reason and that a later edit would undo without noticing -- Save drifting back
/// inside the scrolling form, the co-op page's actions sinking below its status text, the theme
/// selector sliding back behind the advanced toggle.
///
/// Each assertion below names the bug it prevents. An assertion that only restates the code would be
/// worth deleting; these describe an arrangement a reader would have to reconstruct from three
/// hundred lines of container code.
/// </summary>
internal static class OverlayLayoutContractTests
{
    private const string Pages = "STS2AIAgent/Ui/AgentOverlayHost.Pages.cs";
    private const string Settings = "STS2AIAgent/Ui/AgentOverlayHost.Settings.cs";

    /// <summary>
    /// The conversation card, its turn rendering and its scroll: split out of the pages file once the
    /// chat stream started carrying reasoning and actions of its own.
    /// </summary>
    private const string ChatCard = "STS2AIAgent/Ui/AgentOverlayHost.ChatCard.cs";

    /// <summary>
    /// Save must not live inside the scrolling form.
    /// </summary>
    /// <remarks>
    /// It used to be the last row of the scrolling column. Changing the theme at the top of a form
    /// that grows with every configured endpoint meant scrolling past all of them to reach the button
    /// that commits the change -- and that is the gesture the settings page is opened for. The check
    /// is the order in the builder: the scroll container is added, and the action rows are added after
    /// it, which is what "outside the scroll" means for this page.
    /// </remarks>
    public static void SaveStaysOutsideTheScrollingForm()
    {
        var source = AgentSourceFixture.Read(Settings);
        var body = AgentSourceFixture.MethodBody(source, "BuildSettingsPage");

        var scroll = body.IndexOf("UiFactory.Scroll(_settingsBody", StringComparison.Ordinal);
        var save = body.IndexOf("UiFactory.Button(Loc.T(\"保存设置\"), SaveSettingsFromUi", StringComparison.Ordinal);
        var test = body.IndexOf("UiFactory.Button(Loc.T(\"测试连接\")", StringComparison.Ordinal);

        Assert.True(scroll >= 0, "The settings form no longer builds a scroll body.");
        Assert.True(save > scroll, "Save is back inside the scrolling form; it has to stay in the footer.");
        Assert.True(test > scroll, "Test connection is back inside the scrolling form.");

        // And it must not be added to the body: the builder adds children to `page`, the form builder
        // adds to `_settingsBody`. Reaching for the wrong one is a one-character edit with no symptom
        // until someone scrolls.
        Assert.False(
            body.Contains("_settingsBody.AddChild(save)", StringComparison.Ordinal),
            "Save must be added to the page, not to the scrolling settings body.");
    }

    /// <summary>
    /// Every jump-bar entry has to be a section the form actually registers.
    /// </summary>
    /// <remarks>
    /// The bar and the form are two halves of one list held together by a string id, which is exactly
    /// the shape that goes wrong quietly: a renamed section leaves a jump button that does nothing
    /// when pressed. This asserts the two share their ids, so the button and the heading are one
    /// decision rather than two that happen to agree.
    /// </remarks>
    public static void EveryJumpButtonNamesARegisteredSection()
    {
        var source = AgentSourceFixture.Read(Settings);

        var sections = AgentSourceFixture.MethodBody(source, "SettingsSections");
        var jumpBar = AgentSourceFixture.MethodBody(source, "BuildSettingsJumpBar");
        var form = AgentSourceFixture.MethodBody(source, "RebuildSettingsForm");
        var anchor = AgentSourceFixture.MethodBody(source, "AddSettingsSection");

        // The bar reads the same list the form is described by, rather than its own copy.
        Assert.Contains("SettingsSections()", jumpBar);
        Assert.Contains("_settingsAnchors[id] = heading;", anchor);

        // The form registers a section, and the anchor map is cleared with the rest of the form's
        // state: a stale anchor from the previous build would scroll to a freed control.
        Assert.Contains("AddSettingsSection(", form);
        Assert.Contains("_settingsAnchors.Clear();", form);

        // Every id the bar offers exists in the shared list, and the list is what the form names.
        var ids = IdsIn(sections);
        Assert.True(ids.Count >= 4, $"SettingsSections yielded {ids.Count} sections; the page has more than that.");
        foreach (var id in ids)
        {
            Assert.Contains($"AddSettingsSection(\"{id}\"", form);
        }
    }

    /// <summary>
    /// The co-op page's actions have to precede its status card.
    /// </summary>
    /// <remarks>
    /// The order was status, invite, control, chat. That put the page's reason for existing below a
    /// card of prose, and mid-fight the pause button was a scroll away from the top. Inverting it is
    /// the whole change; without this assertion the next card added "for context" can undo it from
    /// above, which is how it got that way in the first place.
    /// </remarks>
    public static void CoopActionsComeBeforeCoopStatus()
    {
        var source = AgentSourceFixture.Read(Pages);
        var page = AgentSourceFixture.MethodBody(source, "BuildCoopSection");

        var invite = page.IndexOf("Loc.T(\"邀请 AI 队友\")", StringComparison.Ordinal);
        var statusCard = page.IndexOf("Loc.T(\"组队状态\")", StringComparison.Ordinal);
        var chat = page.IndexOf("Loc.T(\"队伍交流\")", StringComparison.Ordinal);

        Assert.True(invite >= 0, "The co-op section no longer builds an invite button.");
        Assert.True(statusCard >= 0, "The co-op section no longer builds its status card.");
        Assert.True(invite < statusCard, "The invite action sank below the status card again.");
        Assert.True(statusCard < chat, "The chat card has to stay last: it is the only part that has to be scrolled to.");

        // Pause and resume share the action card rather than living in one of their own, which is what
        // removes the scroll between "start the teammate" and "stop the teammate".
        Assert.True(
            page.IndexOf("Loc.T(\"暂停队友\")", StringComparison.Ordinal) < statusCard,
            "Teammate pause belongs with the invite, above the status card.");
        Assert.False(
            page.Contains("Loc.T(\"队友控制\")", StringComparison.Ordinal),
            "The standalone teammate-control card is back; its buttons belong in the action card.");
    }

    /// <summary>
    /// The co-op section no longer scrolls itself: the play page's single scroll carries it.
    /// </summary>
    /// <remarks>
    /// When the multiplayer mode had its own tab it owned a scroll container sized from the page. As a
    /// section of the play page it must not build a second, nested scroll -- two scroll containers on
    /// one axis fight over the same drag, and the inner one wins where the player expects the page to
    /// move. The section returns its plain column and lets the page scroll it.
    /// </remarks>
    public static void CoopChatBoxAdaptsToThePage()
    {
        var source = AgentSourceFixture.Read(Pages);
        var page = AgentSourceFixture.MethodBody(source, "BuildCoopSection");

        Assert.Contains("return page;", page);
        Assert.False(
            page.Contains("UiFactory.Scroll(", StringComparison.Ordinal),
            "The co-op section builds its own scroll again; the play page's scroll carries it.");
    }

    /// <summary>
    /// The conversation card's switches stay compact so the message log keeps the height.
    /// </summary>
    /// <remarks>
    /// The compose controls are a fixed cost against a panel that is 72% of the viewport. Three
    /// checkboxes sprawled one per row plus a tall editor was most of a third of the page spent on
    /// flags a player sets once, at the message log's expense. They share rows instead.
    ///
    /// The card moved to its own file when the conversation became the decision flow; the contract
    /// follows it rather than reading a file that no longer declares it.
    /// </remarks>
    public static void ChatFooterKeepsItsHeightForMessages()
    {
        var source = AgentSourceFixture.Read(ChatCard);
        var card = AgentSourceFixture.MethodBody(source, "BuildChatCard");

        Assert.Contains("UiFactory.Row(_attachState, _attachShot)", card);
        Assert.Contains("column.AddChild(_showThinking);", card);
        Assert.False(
            card.Contains("UiFactory.Row(_showThinking", StringComparison.Ordinal),
            "The reasoning switch is back in a row of its own; the flags share rows.");
        Assert.Contains("UiFactory.Multiline(\"\", 52)", card);
        // The "let the AI act" switch is gone: acting is the auto-play and single-step controls' job,
        // and a message that asks for a move is what releases one turn.
        Assert.False(
            card.Contains("_allowAct", StringComparison.Ordinal),
            "The play-for-me switch is back in the conversation card.");
    }

    /// <summary>
    /// A label whose text can run long has to reflow, or it becomes the width of the whole panel.
    /// </summary>
    /// <remarks>
    /// This is the defect the live pass found on 2026-09-21: an unwrapped label reports the width of
    /// its entire line as its minimum, and a Godot container grows to fit its children's minimums. One
    /// hint sentence measured 659 pixels against a 440-pixel panel, which pushed the content column to
    /// 691 and clipped every card against the panel edge -- reported by the player as "the UI is cut
    /// off horizontally".
    ///
    /// The contract is on the *default*: <c>Label</c> does not wrap, so anything holding a sentence
    /// must ask for <c>Wrapped</c>. Checking that every call site is correct is not possible from
    /// source text, but checking that the long ones go through the reflowing helper is -- and those are
    /// the ones that can break the layout.
    /// </remarks>
    public static void LongLabelsGoThroughTheReflowingHelper()
    {
        foreach (var file in new[] { Pages, Settings })
        {
            var source = AgentSourceFixture.Read(file);
            foreach (var line in source.Split('\n'))
            {
                var call = line.IndexOf("UiFactory.Label(", StringComparison.Ordinal);
                if (call < 0 || line.Contains("UiFactory.Wrapped(", StringComparison.Ordinal))
                {
                    continue;
                }

                // A CJK sentence in a Label call is a layout hazard: count the characters in the first
                // string literal on the line and fail past the width of a panel column.
                var open = line.IndexOf('"', call);
                if (open < 0)
                {
                    continue; // dynamic text; the field-level cases below cover those
                }

                var close = line.IndexOf('"', open + 1);
                if (close < 0)
                {
                    continue;
                }

                var text = line[(open + 1)..close];
                Assert.True(
                    text.Length <= 24,
                    $"{Path.GetFileName(file)}: UiFactory.Label is holding a {text.Length}-character "
                    + $"string, which will set the panel's width instead of reflowing: \"{text[..Math.Min(24, text.Length)]}...\"\n"
                    + "Use UiFactory.Wrapped for sentences.");
            }
        }
    }

    /// <summary>
    /// The active-play control uses a vertical stack so its long pause label cannot push the disabled
    /// single-step button out of a narrow overlay.
    /// </summary>
    public static void PlayControlsDoNotRequireOneWideRow()
    {
        var source = AgentSourceFixture.Read(Pages);
        var page = AgentSourceFixture.MethodBody(source, "BuildSoloSection");
        var refresh = AgentSourceFixture.MethodBody(source, "RefreshDynamic");

        Assert.Contains("playControls.AddChild(_playToggle);", page);
        Assert.Contains("playControls.AddChild(_stepButton);", page);
        Assert.False(
            page.Contains("UiFactory.Row(_playToggle, _stepButton)", StringComparison.Ordinal),
            "The long pause button and single-step control share a horizontal row again.");
        Assert.Contains("_stepButton.Visible = !playing;", refresh);
    }

    /// <summary>
    /// Session and run usage are sentences, not numerals, so they cannot share one horizontal row.
    /// </summary>
    public static void UsageSentencesDoNotShareARow()
    {
        var source = AgentSourceFixture.Read(Pages);
        var decisions = AgentSourceFixture.MethodBody(source, "BuildDecisionCard");
        var sessionTile = decisions.IndexOf("Loc.T(\"本次会话\")", StringComparison.Ordinal);
        var runTile = decisions.IndexOf("Loc.T(\"本局\")", StringComparison.Ordinal);
        Assert.True(sessionTile >= 0 && runTile > sessionTile, "The decision card no longer builds the usage tiles.");
        Assert.False(
            decisions.Contains("UiFactory.Row(\n", StringComparison.Ordinal)
            && decisions.Contains("UiFactory.MetricTile(Loc.T(\"本次会话\"), _decisionUsage),\n", StringComparison.Ordinal),
            "Session and run usage are sentences; a side-by-side row clips them inside a 440px panel.");
    }

    /// <summary>
    /// Metric values receive data at runtime and must reflow inside their tile instead of setting the
    /// horizontal minimum for the whole dashboard row.
    /// </summary>
    public static void MetricValuesReflowInsideTheirTiles()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Ui/UiFactory.cs");
        var metric = AgentSourceFixture.MethodBody(source, "MetricTile");

        Assert.Contains("valueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;", metric);
        Assert.Contains("valueLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;", metric);
    }

    /// <summary>
    /// A decision reason is rich text, not a <see cref="Label"/>, so the normal label helper cannot
    /// protect the decision log from a long English reason.
    /// </summary>
    public static void RichLogsReflowInsteadOfClippingHorizontally()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Ui/UiFactory.cs");
        var rich = AgentSourceFixture.MethodBody(source, "Rich");

        Assert.Contains("AutowrapMode = TextServer.AutowrapMode.WordSmart", rich);
        Assert.Contains("CustomMinimumSize = new Vector2(MinReflowWidth, 0)", rich);
    }

    public static void SentenceBearingFieldsReflow()
    {
        var pages = AgentSourceFixture.Read(Pages);
        var settings = AgentSourceFixture.Read(Settings);

        foreach (var (source, field, file) in new[]
                 {
                     (pages, "_dualHint = UiFactory.Wrapped(", Pages),
                     (pages, "_teamStatus = UiFactory.Wrapped(", Pages),
                     // The connect section moved into settings with the two-tab reorg, so its status
                     // line is built there now.
                     (settings, "_mcpStatus = UiFactory.Wrapped(", Settings),
                     (settings, "_settingsLoadNotice = UiFactory.Wrapped(", Settings),
                     (settings, "_conversationTest = UiFactory.Wrapped(", Settings),
                     (settings, "_playTest = UiFactory.Wrapped(", Settings),
                     (settings, "_visionTest = UiFactory.Wrapped(", Settings),
                     (settings, "_budgetHint = UiFactory.Wrapped(", Settings),
                     // The labels that are short at build time and long once the runtime fills them in.
                     // This is the case that escaped review twice: `_sessionNext` is "-" when built and
                     // measured 577 pixels wide live, which is what clipped the co-op page.
                     (pages, "_sessionDetail = UiFactory.Wrapped(", Pages),
                     (pages, "_sessionNext = UiFactory.Wrapped(", Pages),
                     (pages, "_sessionConfigNotice = UiFactory.Wrapped(", Pages),
                     // Play and decision pages fill these after build. A heading-sized "-" becomes
                     // a usage sentence or a model reason and sets the row width if it cannot wrap.
                     (pages, "_playStatus = UiFactory.Wrapped(", Pages),
                     (pages, "_playSummary = UiFactory.Wrapped(", Pages),
                     (pages, "_decisionUsage = UiFactory.Wrapped(", Pages),
                     (pages, "_decisionRunSpend = UiFactory.Wrapped(", Pages)
                 })
        {
            Assert.Contains(field, source);
        }
    }

    /// <summary>
    /// A live theme switch has to repaint the panels, not only the text on them.
    /// </summary>
    /// <remarks>
    /// The first version of the switch handled labels, buttons and inputs and knew nothing about
    /// <c>PanelContainer</c>, so choosing the light theme produced light cards sitting on a still-dark
    /// chrome -- reported on 2026-09-21 as "the ivory theme still has parts that are not light". Every
    /// surface this factory styles is tagged with its role at the moment it is styled, so the paint and
    /// the repaint cannot disagree.
    /// </remarks>
    public static void ThemeSwitchRepaintsSurfacesNotJustText()
    {
        var factory = AgentSourceFixture.Read("STS2AIAgent/Ui/UiFactory.cs");
        var settings = AgentSourceFixture.Read(Settings);

        // The factory records a role wherever it styles a surface, and can rebuild it.
        Assert.Contains("public static void TagSurface(PanelContainer panel, SurfaceRole role", factory);
        Assert.Contains("public static void ReapplySurfaceTheme(PanelContainer panel)", factory);
        foreach (var role in new[] { "SurfaceChrome", "SurfaceChromeRaised", "SurfaceCard", "SurfaceMetric", "SurfaceTinted" })
        {
            Assert.Contains(role, factory);
        }

        // Cards and metric tiles tag themselves.
        Assert.Contains("card.SetMeta(MetaSurface, SurfaceCard);", factory);
        Assert.Contains("panel.SetMeta(MetaSurface, SurfaceMetric);", factory);
        Assert.Contains("pill.SetMeta(MetaSurface, SurfaceTinted);", factory);
        Assert.Contains("pill.SetMeta(MetaTint, (int)tone);", factory);

        // And the repaint walk reaches panel containers, rich text and the tagged accent bars.
        Assert.Contains("UiFactory.ReapplySurfaceTheme(container);", settings);
        Assert.Contains("UiFactory.ReapplyRichTheme(rich);", settings);
        Assert.Contains("UiFactory.ReapplyPanelTheme(panel);", settings);

        // It walks from the host, not the panel: the chat footer is the panel's sibling, and a walk
        // rooted at the panel left its buttons in the previous palette.
        Assert.Contains("RepaintForPalette(_host);", settings);
    }

    /// <summary>
    /// The overlay's own chrome is tagged, so it is repainted with everything else.
    /// </summary>
    /// <remarks>
    /// The chrome and the header are built in the host rather than through the factory's helpers, which
    /// is exactly why they were the last dark surfaces left in the light theme.
    /// </remarks>
    public static void OverlayChromeIsTaggedForRepaint()
    {
        var host = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");

        Assert.Contains("UiFactory.TagSurface(chrome, UiFactory.SurfaceRole.Chrome", host);
        Assert.Contains("UiFactory.TagSurface(_dragHandle, UiFactory.SurfaceRole.ChromeRaised", host);
        Assert.False(
            host.Contains("chrome.AddThemeStyleboxOverride(\"panel\", UiFactory.PanelStyle());", StringComparison.Ordinal),
            "The chrome styles itself directly again, which leaves it out of the repaint path.");
    }

    /// <summary>
    /// The swatch preview is built from child nodes rather than a custom draw.
    /// </summary>
    /// <remarks>
    /// The first version overrode <c>_Draw</c> and was never called in the live build -- the diagnostic
    /// printed nothing at all and every tile rendered as an empty rectangle. Child nodes have no such
    /// question. This pins the approach, not the drawing code.
    /// </remarks>
    public static void SwatchPreviewUsesChildNodes()
    {
        const string swatch = "STS2AIAgent/Ui/OverlaySwatch.cs";
        var preview = AgentSourceFixture.Read(swatch);
        var factory = AgentSourceFixture.Read("STS2AIAgent/Ui/UiFactory.cs");

        Assert.Contains("internal static class OverlaySwatch", preview);
        Assert.Contains("public static Control Build(OverlayPalette palette)", preview);
        Assert.Contains("new ColorRect", preview);
        // The picker draws the tile with it, and neither file falls back to a custom draw.
        Assert.Contains("OverlaySwatch.Build(preset)", factory);
        Assert.False(
            preview.Contains("public override void _Draw()", StringComparison.Ordinal),
            "The swatch went back to a custom _Draw, which did not run in the live build.");
        Assert.False(
            factory.Contains("public override void _Draw()", StringComparison.Ordinal),
            "The factory went back to a custom _Draw, which did not run in the live build.");
    }

    /// <summary>
    /// A jump lands the section's heading at the top of the view, not at its bottom edge.
    /// </summary>
    /// <remarks>
    /// <c>EnsureControlVisible</c> scrolls the least amount that makes a control visible, so jumping
    /// downwards parked the heading against the bottom of the viewport with all of its content below
    /// the fold -- reported live on 2026-09-21 as "it only pulls the heading into view". The offset has
    /// to be computed, clamped, and assigned.
    /// </remarks>
    public static void JumpingToASectionPutsItAtTheTop()
    {
        var settings = AgentSourceFixture.Read(Settings);
        var jump = AgentSourceFixture.MethodBody(settings, "JumpToSettingsSection");

        Assert.Contains("scroll.ScrollVertical = ", jump);
        Assert.Contains("Math.Clamp(target, 0f, maximum)", jump);
        Assert.False(
            jump.Contains("EnsureControlVisible", StringComparison.Ordinal),
            "EnsureControlVisible is back, which stops a downward jump at the bottom edge of the view.");
    }

    /// <summary>The section ids a <c>SettingsSections</c> body declares, in order.</summary>
    private static List<string> IdsIn(string sections)
    {
        var ids = new List<string>();
        foreach (var line in sections.Split('\n'))
        {
            var start = line.IndexOf("(\"", StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            var end = line.IndexOf('"', start + 2);
            if (end > start)
            {
                ids.Add(line[(start + 2)..end]);
            }
        }

        return ids;
    }
}
