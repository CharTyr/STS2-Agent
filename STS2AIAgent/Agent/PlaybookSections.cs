using System.Text;

namespace STS2AIAgent.Agent;

/// <summary>
/// Slices the shared skill references into the section that applies to the screen being played.
/// </summary>
/// <remarks>
/// The in-game prompt used to carry every reference in full, so a decision made in combat was paid
/// for with the shop, route, and rest-site guidance it could not use. This type keeps the mapping
/// from a game screen to the guidance that screen actually needs in one place, and both the mapping
/// and the slice are pure, so the executable test project can pin them without a game.
///
/// Each reference gets its own <see cref="Reference"/> because they answer different questions:
/// <c>strategy.md</c> says what to choose and only a few screens have a real choice, while
/// <c>screen-playbooks.md</c> says how to drive the screen and almost every screen has a section
/// (which is why the playbook slice has an index fallback and the strategy slice does not). What
/// the two share are the rules that make it safe to change the references freely:
/// <list type="bullet">
/// <item>A heading that no screen maps to has to be listed as run-level or as deliberately not
/// injected, so a new section cannot be added and then silently never reach the model.</item>
/// <item>Extraction is heading-based against the committed Markdown, so a renamed heading fails the
/// test rather than quietly returning an empty string.</item>
/// </list>
/// </remarks>
internal static class PlaybookSections
{
    /// <summary>
    /// How much of one screen's guidance is injected. A section longer than this is cut at a
    /// paragraph boundary rather than mid-sentence, and the cap is what keeps a longer reference
    /// from turning into a longer prompt.
    /// </summary>
    public const int MaxInjectedCharacters = 2400;

    /// <summary>
    /// The heading the in-run pause pages share. They ride one container, so they share one exit
    /// rule and one section.
    /// </summary>
    private const string InRunMenuPagesHeading = "In-Run Menu Pages (PAUSE_MENU, SETTINGS, COMPENDIUM, ...)";

    /// <summary>
    /// Game screens (as <c>GameStateService.ResolveNonModalScreen</c> names them) to the strategy
    /// headings they need, in the order they are injected. <c>FAKE_MERCHANT</c> is the merchant
    /// event: it opens the same shop payload, so it needs the same advice as <c>SHOP</c>.
    /// </summary>
    /// <remarks>
    /// The table names below are read as source text by
    /// <c>mcp_server/tests/test_scene_guidance_alignment.py</c>, which keeps this mapping and the
    /// Python sidecar's copy of it identical; renaming a field here breaks that cross-language
    /// contract even though nothing in C# would notice.
    /// </remarks>
    private static readonly Dictionary<string, string[]> ScreenHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COMBAT"] = new[] { "Combat: what to prioritise", "Potions: when to drink" },
        ["MAP"] = new[] { "Route: which node to enter" },
        ["REST"] = new[] { "Rest site: heal or upgrade" },
        ["SHOP"] = new[] { "Shop: what to buy" },
        ["FAKE_MERCHANT"] = new[] { "Shop: what to buy" },
        ["EVENT"] = new[] { "Event options: how to choose" }
    };

    /// <summary>
    /// Headings that apply wherever the agent is, and are therefore not injected per screen: they
    /// are either always true or belong with the shared contract the prompt already carries.
    /// </summary>
    private static readonly string[] RunLevelHeadings = Array.Empty<string>();

    /// <summary>
    /// Headings that are deliberately never injected in-game, with the reason. The co-op section is
    /// written for a client that can see the teammate's instance and coordinate over the shared
    /// session; the in-game loop drives one local player and has no channel to act on it.
    /// </summary>
    private static readonly Dictionary<string, string> NotInjectedHeadings = new(StringComparer.Ordinal)
    {
        ["Co-op: dividing the work"] =
            "written for a client coordinating two instances; the in-game loop drives one local player",
        ["Where these rules come from"] =
            "provenance note for a reader, not instruction for a decision"
    };

    /// <summary>
    /// The choices the per-screen action sequences do not make: route, rest site, shop, potion
    /// timing, combat priority. Screens with no strategic choice map to nothing on purpose, which
    /// is the honest answer for a reward screen rather than a fallback to something unrelated.
    /// </summary>
    public static Reference Strategy { get; } = new(ScreenHeadings, RunLevelHeadings, NotInjectedHeadings);

    /// <summary>
    /// The exact action sequence for the screen being played. Screens named together in one heading
    /// ("MODAL, GAME_OVER, and UNLOCK") are mapped one by one, because the screen that is on top is
    /// the one the loop resolves. <c>FAKE_MERCHANT</c> opens the merchant event's inventory, so it
    /// gets its own section and the shop sequence it drives.
    /// </summary>
    /// <remarks>
    /// The screen names are the ones <c>GameStateService.ResolveNonModalScreen</c> and
    /// <c>ResolveScreen</c> produce. A name that drifts away from the one the resolver returns stops
    /// matching here and silently drops to the index fallback, which is why
    /// <c>PlaybookSectionsTests.EveryPlaybookScreenIsOneTheGameCanReport</c> reads the resolver's own
    /// source and pins both directions: every mapped screen is a name the game can report, and every
    /// name the game can report is either mapped or declared in
    /// <see cref="PlaybookIndexFallbackScreens"/>.
    /// </remarks>
    public static Reference Playbooks { get; } = new(
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MAIN_MENU"] = new[] { "MAIN_MENU and Timeline" },
            ["TIMELINE"] = new[] { "MAIN_MENU and Timeline" },
            ["CHARACTER_SELECT"] = new[] { "CHARACTER_SELECT" },
            ["BUNDLE_SELECTION"] = new[] { "BUNDLE_SELECTION" },
            ["CAPSTONE_SELECTION"] = new[] { "CAPSTONE_SELECTION" },
            ["MAP"] = new[] { "MAP" },
            // Targeting a potion happens inside combat, and the targeting rules are the part of the
            // playbook a combat turn can be wrong about, so they travel with it.
            ["COMBAT"] = new[] { "COMBAT", "Potion Targeting" },
            ["CARD_SELECTION"] = new[] { "CARD_SELECTION" },
            ["REWARD"] = new[] { "REWARD" },
            ["SHOP"] = new[] { "SHOP" },
            ["FAKE_MERCHANT"] = new[] { "FAKE_MERCHANT", "SHOP" },
            ["REST"] = new[] { "REST" },
            ["CHEST"] = new[] { "CHEST" },
            ["EVENT"] = new[] { "EVENT" },
            ["CRYSTAL_SPHERE"] = new[] { "CRYSTAL_SPHERE" },
            ["MODAL"] = new[] { "MODAL, GAME_OVER, and UNLOCK" },
            ["GAME_OVER"] = new[] { "MODAL, GAME_OVER, and UNLOCK" },
            ["UNLOCK"] = new[] { "MODAL, GAME_OVER, and UNLOCK" },
            ["PATCH_NOTES"] = new[] { "PATCH_NOTES" },
            // One section covers the plain card list and both inspect overlays, because one action
            // (`close_cards_view`) leaves all three; a combat card pile exits the same way.
            ["CARDS_VIEW"] = new[] { "CARD_INSPECT and RELIC_INSPECT" },
            ["CARD_PILE"] = new[] { "CARD_INSPECT and RELIC_INSPECT" },
            ["CARD_INSPECT"] = new[] { "CARD_INSPECT and RELIC_INSPECT" },
            ["RELIC_INSPECT"] = new[] { "CARD_INSPECT and RELIC_INSPECT" },
            ["FEEDBACK"] = new[] { "FEEDBACK" },
            ["PAUSE_MENU"] = new[] { InRunMenuPagesHeading },
            ["SETTINGS"] = new[] { InRunMenuPagesHeading },
            ["COMPENDIUM"] = new[] { InRunMenuPagesHeading },
            ["CARD_LIBRARY"] = new[] { InRunMenuPagesHeading },
            ["RELIC_COLLECTION"] = new[] { InRunMenuPagesHeading },
            ["POTION_LAB"] = new[] { InRunMenuPagesHeading },
            ["BESTIARY"] = new[] { InRunMenuPagesHeading },
            ["STATS"] = new[] { InRunMenuPagesHeading },
            ["RUN_HISTORY"] = new[] { InRunMenuPagesHeading }
        },
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Screens the game can report that this playbook deliberately has no section for, and which
    /// therefore receive the index fallback. Declaring them is what turns "we forgot to map this
    /// screen" into a test failure: the reverse contract keeps every name the resolver can return
    /// either mapped or listed here.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PlaybookIndexFallbackScreens { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UNKNOWN"] =
                "the resolver's own name for a screen it could not identify; there is nothing to look up",
            ["MULTIPLAYER_LOBBY"] =
                "the lobby is driven by the shared contract's routing rules, not by an action sequence",
            ["MULTIPLAYER_LOAD"] =
                "a loading transition, not a screen the agent can act on"
        };

    /// <summary>Every heading in the reference, so a mapped name can be proven to exist.</summary>
    public static IReadOnlyList<string> Headings(string markdown) => ExtractHeadings(markdown);

    /// <summary>
    /// The <c>screen</c> of a compact state payload, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Deliberately forgiving: the caller's job is to decide what guidance to attach, and a payload
    /// that will not parse must leave it attaching none rather than throwing on the play path.
    /// </remarks>
    public static string? ScreenOfCompactState(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(stateJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("screen", out var screen) ||
                screen.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            var value = screen.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal static string Cap(string text)
    {
        if (text.Length <= MaxInjectedCharacters)
        {
            return text;
        }

        var window = text[..MaxInjectedCharacters];
        var lastBreak = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        return (lastBreak > 0 ? window[..lastBreak] : window).TrimEnd();
    }

    private static List<string> ExtractHeadings(string markdown)
    {
        var headings = new List<string>();
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("## ", StringComparison.Ordinal) &&
                !trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                headings.Add(trimmed[3..].Trim());
            }
        }

        return headings;
    }

    /// <summary>Heading to body, for every <c>##</c> section, bodies trimmed.</summary>
    private static Dictionary<string, string> ExtractSections(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = (string?)null;
        var body = new StringBuilder();

        void Flush()
        {
            if (current != null)
            {
                sections[current] = body.ToString().Trim();
            }

            body.Clear();
        }

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("## ", StringComparison.Ordinal) &&
                !trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                current = trimmed[3..].Trim();
                continue;
            }

            if (current != null)
            {
                body.Append(trimmed).Append('\n');
            }
        }

        Flush();
        return sections;
    }

    /// <summary>
    /// The screen-to-heading mapping of one reference, with the slicing that mapping produces.
    /// </summary>
    internal sealed class Reference
    {
        private readonly IReadOnlyDictionary<string, string[]> _screenHeadings;
        private readonly IReadOnlyList<string> _runLevelHeadings;
        private readonly IReadOnlyDictionary<string, string> _notInjectedHeadings;

        internal Reference(
            IReadOnlyDictionary<string, string[]> screenHeadings,
            IReadOnlyList<string> runLevelHeadings,
            IReadOnlyDictionary<string, string> notInjectedHeadings)
        {
            _screenHeadings = screenHeadings;
            _runLevelHeadings = runLevelHeadings;
            _notInjectedHeadings = notInjectedHeadings;
        }

        /// <summary>Every screen this reference has guidance for.</summary>
        public IReadOnlyList<string> Screens =>
            _screenHeadings.Keys.OrderBy(screen => screen, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// The exact headings this reference promises to inject, for the contract test that compares
        /// them against the reference file: a mapped heading that no longer exists is guidance the
        /// model silently stopped receiving.
        /// </summary>
        public IReadOnlyList<string> MappedHeadings()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var headings in _screenHeadings.Values)
            {
                foreach (var heading in headings)
                {
                    names.Add(heading);
                }
            }

            return names.ToArray();
        }

        /// <summary>
        /// Headings that apply wherever the agent is, and are therefore not injected per screen:
        /// they are either always true or belong with the shared contract the prompt already
        /// carries.
        /// </summary>
        public IReadOnlyList<string> RunLevel() => _runLevelHeadings;

        /// <summary>Headings that are deliberately never injected in-game, with the reason.</summary>
        public IReadOnlyDictionary<string, string> NotInjected() => _notInjectedHeadings;

        /// <summary>
        /// The headings this screen needs. Empty for a screen with no strategic choice (or none of
        /// this reference's subject in it), which is the honest answer rather than a fallback to
        /// something unrelated.
        /// </summary>
        public IReadOnlyList<string> HeadingsFor(string? screen)
        {
            if (string.IsNullOrWhiteSpace(screen))
            {
                return Array.Empty<string>();
            }

            return _screenHeadings.TryGetValue(screen.Trim(), out var headings)
                ? headings
                : Array.Empty<string>();
        }

        /// <summary>
        /// The guidance for one screen, or an empty string when that screen has none. Every requested
        /// heading is included when the budget allows; a single section that alone exceeds the budget
        /// is cut at a paragraph boundary so the injection stays bounded.
        /// </summary>
        public string Slice(string markdown, string? screen)
        {
            var wanted = HeadingsFor(screen);
            if (wanted.Count == 0)
            {
                return string.Empty;
            }

            var sections = ExtractSections(markdown);
            var builder = new StringBuilder();
            foreach (var heading in wanted)
            {
                if (!sections.TryGetValue(heading, out var body))
                {
                    // A renamed heading in the reference must not silently remove the guidance.
                    continue;
                }

                var piece = "## " + heading + "\n\n" + body.Trim();
                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append(piece);
            }

            if (builder.Length == 0)
            {
                return string.Empty;
            }

            return Cap(builder.ToString());
        }

        /// <summary>
        /// The same slice, but never empty: a screen this reference has no section for gets the
        /// index of its sections instead.
        /// </summary>
        /// <remarks>
        /// Used for the screen playbooks, which carry the play contract for the screen being played:
        /// injecting nothing there would leave a screen with no action sequence at all, and the
        /// model cannot tell "there is nothing to know" from "the mapping missed this screen". The
        /// index lists what exists rather than falling back to the whole document, because an
        /// unmatched screen is a gap or a cosmetic screen, and paying the full document (~3,070
        /// tokens) on every such step would undo the saving on the screens that do match.
        /// </remarks>
        public string SliceOrIndex(string markdown, string? screen)
        {
            var slice = Slice(markdown, screen);
            return slice.Length > 0 ? slice : Index(markdown, screen);
        }

        private static string Index(string markdown, string? screen)
        {
            var builder = new StringBuilder();
            builder.Append("No section of this playbook matches the current screen (");
            builder.Append(string.IsNullOrWhiteSpace(screen) ? "unknown" : screen.Trim());
            builder.AppendLine("). The sections it carries are:");
            foreach (var heading in ExtractHeadings(markdown))
            {
                builder.Append("- ").AppendLine(heading);
            }

            builder.Append(
                "Drive this screen from the shared play contract above and the live state, and re-read "
                + "state after every action instead of assuming an action sequence.");
            return Cap(builder.ToString());
        }
    }
}
