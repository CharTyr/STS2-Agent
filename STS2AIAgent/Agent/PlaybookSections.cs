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
/// Two rules make it safe to change the references freely:
/// <list type="bullet">
/// <item>A heading that no screen maps to has to be listed in <see cref="RunLevelHeadings"/> or
/// <see cref="NotInjectedHeadings"/>, so a new section cannot be added and then silently never
/// reach the model.</item>
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
    /// Game screens (as <c>GameStateService.ResolveNonModalScreen</c> names them) to the headings
    /// they need, in the order they are injected. <c>FAKE_MERCHANT</c> is the merchant event: it
    /// opens the same shop payload, so it needs the same advice as <c>SHOP</c>.
    /// </summary>
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

    /// <summary>
    /// The headings this screen needs. Empty for a screen with no strategic choice, which is the
    /// honest answer for a reward screen rather than a fallback to something unrelated.
    /// </summary>
    public static IReadOnlyList<string> HeadingsForScreen(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return Array.Empty<string>();
        }

        return ScreenHeadings.TryGetValue(screen.Trim(), out var headings)
            ? headings
            : Array.Empty<string>();
    }

    /// <summary>
    /// The guidance for one screen, or an empty string when that screen has none. Every requested
    /// heading is included when the budget allows; a single section that alone exceeds the budget is
    /// cut at a paragraph boundary so the injection stays bounded.
    /// </summary>
    public static string ForScreen(string markdown, string? screen)
    {
        var wanted = HeadingsForScreen(screen);
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
    /// The exact headings this type promises to inject, for the contract test that compares them
    /// against the reference file: a mapped heading that no longer exists is guidance the model
    /// silently stopped receiving.
    /// </summary>
    public static IReadOnlyList<string> MappedHeadings()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var headings in ScreenHeadings.Values)
        {
            foreach (var heading in headings)
            {
                names.Add(heading);
            }
        }

        return names.ToArray();
    }

    public static IReadOnlyList<string> RunLevel() => RunLevelHeadings;

    public static IReadOnlyDictionary<string, string> NotInjected() => NotInjectedHeadings;

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
}
