using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// Private game members are looked up in one place, and what that place reports is what the mod gets.
/// </summary>
/// <remarks>
/// The first version of <c>ReflectedGameMembers</c> resolved its own copy of each member while the
/// call sites resolved theirs, with their own binding flags. The two could disagree, and did:
/// <c>_longPressDuration</c> is static, the reader asked for an instance field, and the moment the
/// registry was written correctly the probe would have reported <c>ready</c> while the reader went on
/// falling back to a number it invented. Proven, not argued -- reintroducing that reader bug with a
/// correct registry left every offline test and gate green.
///
/// So call sites now ask the registry for the member instead of calling <c>GetField</c> themselves,
/// and these contracts keep it that way:
///
/// - no game-facing source calls <c>GetField</c> / <c>GetMethod</c> / <c>GetProperty</c> with a name
///   of its own, underscore or not (the earlier contract only looked for <c>"_x"</c> literals, and five
///   method names went straight past it);
/// - every <c>ReflectedGameMembers.Field</c> / <c>Method</c> / <c>Property</c> call names a registered member of the
///   right kind -- an unregistered one throws, and it would throw on a live request path;
/// - every registered member is actually asked for, or the probe reports on something nothing reads.
///
/// Duck-typed probing is a different thing and is allowed on purpose: <c>TryGetMemberValue</c> tries
/// a list of candidate names across object shapes and accepts whichever exists, so no single name in
/// it is a member the mod depends on. It is named here as the one sanctioned channel rather than
/// being invisible, which is what it was.
/// </remarks>
internal static class ReflectedMemberRegistryTests
{
    private const string RegistryPath = "STS2AIAgent/Game/ReflectedGameMembers.cs";

    /// <summary>
    /// Directories whose sources reach into the game. The agent, config and LLM layers reflect over
    /// JSON and over the mod's own types, which the compiler checks.
    /// </summary>
    private static readonly string[] GameFacingDirectories =
    {
        "STS2AIAgent/Game",
        "STS2AIAgent/Multiplayer",
        "STS2AIAgent/Ui",
    };

    /// <summary>
    /// Names a direct reflection call may use outside the registry, and why. An entry here is a claim
    /// that the call is not a dependency on one game member, not that it does not matter.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DuckTypedNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Public methods of LocString / LocTable, called on values typed object because the value
            // may be either -- or neither, in which case the caller falls back to ToString().
            ["GetRawText"] = "public LocString/LocTable API called on values of several types, with a ToString() fallback",
            ["GetFormattedText"] = "public LocString API called on values of several types, with a ToString() fallback",
            // The public Id.Entry pair every game model carries, read off card, power and relic models
            // alike inside SafeReadString, which answers null rather than throwing.
            ["Id"] = "public model Id read across model types inside SafeReadString",
            ["Entry"] = "public ModelId.Entry read across model types inside SafeReadString",
            // The metadata name of a C# indexer, used to write a command-line table whose generic type
            // varies. It belongs to .NET, not to the game.
            ["Item"] = "the C# indexer's metadata name, on a table whose generic type varies",
            // A fallback after the compile-checked `value is LocString` branch, for values of other types.
            ["LocEntryKey"] = "fallback after the compile-checked LocString branch, for values of other types",
            // Public properties read off whichever node, creature or power instance is at hand. Checked
            // against the installed sts2.dll on 2026-09-18: every declaration of each is public.
            ["Model"] = "public card-holder Model read across holder node types",
            ["Powers"] = "public Creature.Powers read across creature types",
            ["Text"] = "public Text read across localized value types, inside the GetRawText fallback chain",
            ["Title"] = "public Title read across power models",
        };

    /// <summary>
    /// Lookups that are known not to resolve in the installed game, kept apart from
    /// <see cref="DuckTypedNames"/> so nobody mistakes one for an accepted pattern.
    /// </summary>
    /// <remarks>
    /// An entry here is a defect with a paper trail, not an exemption. It sits here rather than in the
    /// registry only because registering it would put every install into <c>degraded</c> over a
    /// data-export gap while gameplay is unaffected -- an alarm that is always on is one people stop
    /// reading. Remove the entry when the read is fixed.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownDeadLookups =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // MonsterModel.MoveNames is declared nowhere in the installed sts2.dll (2026-09-18); moves
            // now come from GetAllMoves / GenerateBestiaryMoveList / GetBestiaryMoveName. So every
            // monster in GET /data/monsters exports `moves: []`. Fixing it is feature work against the
            // new API and needs live verification; it is on the deferred list in PRODUCT_PLAN_CURRENT.md.
            ["MoveNames"] = "DEAD: MonsterModel.MoveNames no longer exists; /data/monsters exports moves: [] -- see the deferred list",
        };

    /// <summary>
    /// Private-member names that appear outside the registry on purpose, and why.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotProbed =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Six concrete card-grid screens each declare their own _prefs/_selectedCards; the base
            // type declares neither. A probe would have to name every subclass, and would then report a
            // false miss the day the game adds or drops one. The reader guards on the base type and
            // fails safe when the fields are absent, so absence costs a metadata block, not a number.
            ["_prefs"] = "declared per concrete card-grid screen; the reader guards on the base type and fails safe",
            ["_selectedCards"] = "declared per concrete card-grid screen; the reader guards on the base type and fails safe",
        };

    // .NET member names are PascalCase or _camelCase. JsonElement.GetProperty shares the method name
    // but reads the mod's own lower-case JSON keys ("ok", "data"), which are not game members; the
    // first character is what tells the two apart. A lower-case game member would slip past this --
    // the trade is deliberate, and none exists today.
    private static readonly Regex DirectReflectionByName = new(
        @"\.(GetField|GetMethod|GetProperty)\(\s*""([A-Z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);
    private static readonly Regex PrivateMemberLiteral = new(@"""(_[a-zA-Z][a-zA-Z0-9_]*)""", RegexOptions.Compiled);
    private static readonly Regex RegistryEntry = new(
        @"new\(typeof\(([A-Za-z_][A-Za-z0-9_]*)\),\s*""([A-Za-z_][A-Za-z0-9_]*)"",\s*MemberKind\.(Field|Method|Property)", RegexOptions.Compiled);
    private static readonly Regex AccessorCall = new(
        @"\b(Field|Method|Property)\(typeof\(([A-Za-z_][A-Za-z0-9_]*)\),\s*""([A-Za-z_][A-Za-z0-9_]*)""\)", RegexOptions.Compiled);

    // A rewrite that defeats an extraction must fail loudly rather than read as "nothing to check".
    private const int MinimumEntries = 15;
    private const int MinimumAccessorCalls = 15;

    private static IReadOnlyList<(string Type, string Name, string Kind)> ReadRegistry()
    {
        var registry = AgentSourceFixture.Read(RegistryPath);
        var entries = RegistryEntry.Matches(registry)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value))
            .ToArray();
        Assert.True(
            entries.Length >= MinimumEntries,
            $"{RegistryPath} yielded {entries.Length} entries, below the {MinimumEntries} expected. The "
            + "extraction in this test no longer matches the registry; fix it before trusting this contract.");
        return entries;
    }

    private static IEnumerable<(string Path, string Text)> GameFacingSources(bool includeRegistry)
    {
        var root = AgentSourceFixture.Root;
        foreach (var relative in GameFacingDirectories)
        {
            var directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var isRegistry = Path.GetFileName(path) == Path.GetFileName(RegistryPath);
                if (isRegistry && !includeRegistry)
                {
                    continue;
                }

                yield return (path, File.ReadAllText(path));
            }
        }
    }

    public static void NothingLooksUpAGameMemberByNameOutsideTheRegistry()
    {
        var registered = ReadRegistry().Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, text) in GameFacingSources(includeRegistry: false))
        {
            var file = Path.GetFileName(path);

            foreach (Match match in DirectReflectionByName.Matches(text))
            {
                var name = match.Groups[2].Value;
                if (!DuckTypedNames.ContainsKey(name) && !KnownDeadLookups.ContainsKey(name))
                {
                    offenders.Add($"{match.Groups[1].Value}(\"{name}\") in {file}");
                }
            }

            foreach (Match match in PrivateMemberLiteral.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (!registered.Contains(name) && !NotProbed.ContainsKey(name))
                {
                    offenders.Add($"\"{name}\" in {file}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These look up a game member by name without going through ReflectedGameMembers:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nRegister the member and ask ReflectedGameMembers.Field/Method for it. A lookup of its own "
            + "is a second opinion on the binding flags -- which is how _longPressDuration was read with the "
            + "wrong ones for as long as the line existed.");
    }

    public static void EveryRegistryCallNamesARegisteredMember()
    {
        var registered = ReadRegistry()
            .Select(entry => $"{entry.Kind}:{entry.Type}.{entry.Name}")
            .ToHashSet(StringComparer.Ordinal);

        var calls = new List<string>();
        var unregistered = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in GameFacingSources(includeRegistry: true).Concat(ModEntrySource()))
        {
            foreach (Match match in AccessorCall.Matches(text))
            {
                var key = $"{match.Groups[1].Value}:{match.Groups[2].Value}.{match.Groups[3].Value}";
                calls.Add(key);
                if (!registered.Contains(key))
                {
                    unregistered.Add($"{key} in {Path.GetFileName(path)}");
                }
            }
        }

        Assert.True(
            calls.Count >= MinimumAccessorCalls,
            $"Only {calls.Count} ReflectedGameMembers.Field/Method calls were found, below the "
            + $"{MinimumAccessorCalls} expected. The extraction no longer matches the call sites.");
        Assert.True(
            unregistered.Count == 0,
            "These ReflectedGameMembers calls name a member the registry does not hold, or hold as the "
            + "other kind:\n  " + string.Join("\n  ", unregistered)
            + "\n\nAn unregistered lookup throws, and it throws on a live request path. The type, the name "
            + "and Field versus Method all have to match an entry.");
    }

    public static void EveryRegisteredMemberIsAskedFor()
    {
        var asked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in GameFacingSources(includeRegistry: true))
        {
            foreach (Match match in AccessorCall.Matches(text))
            {
                asked.Add($"{match.Groups[1].Value}:{match.Groups[2].Value}.{match.Groups[3].Value}");
            }
        }

        var orphans = ReadRegistry()
            .Select(entry => $"{entry.Kind}:{entry.Type}.{entry.Name}")
            .Where(key => !asked.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            "ReflectedGameMembers holds members nothing asks for: " + string.Join(", ", orphans)
            + ". Drop them, or /health will keep reporting on something that stopped mattering.");
    }

    public static void HealthStatusComesFromTheProbe()
    {
        var router = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs"));

        // Assert.True rather than Assert.Contains: the haystack is a whole flattened source file,
        // and a failure that prints it is a failure nobody reads.
        Assert.True(
            router.Contains("status=ReflectedGameMembers.ResolveStatus()", StringComparison.Ordinal),
            "GET /health must derive status from ReflectedGameMembers.ResolveStatus().");
        Assert.True(
            router.Contains("compatibility=ReflectedGameMembers.BuildHealthSection()", StringComparison.Ordinal),
            "GET /health must report the compatibility probe, or a missing game member stays invisible.");
        Assert.False(
            router.Contains("status=\"ready\"", StringComparison.Ordinal),
            "GET /health must not hard-code its own status. A field that can never be wrong can "
            + "never tell a player the mod has stopped being able to read the game.");
    }

    public static void TheProbeRunsWhenTheModLoads()
    {
        var entry = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/ModEntry.cs"));
        Assert.True(
            entry.Contains("ReflectedGameMembers.ProbeAtStartup();", StringComparison.Ordinal),
            "ModEntry.Initialize must run the compatibility probe. A player reporting a problem attaches "
            + "the log, not a curl of /health; a renamed member has to be in the file they actually send.");
    }

    private static IEnumerable<(string Path, string Text)> ModEntrySource()
    {
        var path = Path.Combine(AgentSourceFixture.Root, "STS2AIAgent", "ModEntry.cs");
        yield return (path, File.ReadAllText(path));
    }
}
