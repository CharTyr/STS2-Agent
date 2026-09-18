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
/// - every registered member is actually asked for, or the probe reports on something nothing reads;
/// - no helper takes a member name as a parameter, except the few <c>NameTakingChannels</c> names --
///   that is how four private lobby and pause-menu methods stayed invisible to the scan above;
/// - Godot is never called by a string (<c>Call("X")</c>, <c>EmitSignal("x")</c>, <c>Set("x")</c>), and
///   the game's buttons are clicked rather than sent a <c>BaseButton</c> signal they do not have.
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
            ["Text"] = "public Text read across localized value types, inside the GetRawText fallback chain",
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
            // Empty, and that is the goal. MonsterModel.MoveNames sat here from the day the registry
            // made the lookup visible until the export was rewritten against the public
            // LocTable.GetLocStringsWithPrefix it had always wrapped.
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

    /// <summary>
    /// Methods allowed to call <c>GetField</c> / <c>GetMethod</c> / <c>GetProperty</c> with a name held
    /// in a variable, and why.
    /// </summary>
    /// <remarks>
    /// A helper that takes the member name as a parameter hides every name its callers pass from the
    /// literal scan above. That is not hypothetical: <c>InvokePrivateTask</c> and
    /// <c>InvokePrivateVoid</c> carried StartHost, ReadyButtonPressed, Disconnect and CloseToMenu past
    /// the first version of this contract, unprobed. Each entry here is a helper that is allowed to
    /// exist; a new one has to be argued for by name.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NameTakingChannels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FindMember"] = "ReflectionMemberAccessor: walks base types for a field or property whose owner varies; its private-name callers are held to the registry by the \"_x\" scan",
            ["FindInstanceMethod"] = "tries CloseFtue on whichever FTUE modal is stuck, then falls back to NModalContainer.Clear()",
        };

    // A name in a variable: a lower-case identifier followed by an argument separator. nameof(...) is
    // compile-checked and does not match, because "nameof" is followed by a parenthesis.
    private static readonly Regex ReflectionByVariable = new(
        @"\.(GetField|GetMethod|GetProperty)\(\s*([a-z][A-Za-z0-9_]*)\s*[,)]", RegexOptions.Compiled);
    private static readonly Regex StaticMethodDeclaration = new(
        @"\b(?:private|public|internal)\s+static\s+[^;{}()=]+?\s([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.Compiled);

    // Godot's own by-name entry points. Each has a compile-checked form -- a MethodName, SignalName or
    // PropertyName constant, or a direct call -- so a string here is always a choice not to be checked.
    private static readonly Regex GodotCallByString = new(
        @"\.(Call|CallDeferred|Set|Get|EmitSignal|HasMethod|HasSignal|Connect)\(\s*""", RegexOptions.Compiled);
    private static readonly Regex GodotButtonSignal = new(
        @"EmitSignal\(\s*(?:global::)?(?:Godot\.)?(?:Base)?Button\.SignalName\.", RegexOptions.Compiled);

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
                // The resolver is the registry's lookup, split out so it compiles offline; it is the
                // one place outside the registry file allowed to call GetField with a name it was given.
                var isRegistry = Path.GetFileName(path) == Path.GetFileName(RegistryPath) ||
                    Path.GetFileName(path) == "ReflectedMemberResolver.cs";
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
        var duckNamesSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, text) in GameFacingSources(includeRegistry: false))
        {
            var file = Path.GetFileName(path);

            foreach (Match match in DirectReflectionByName.Matches(text))
            {
                var name = match.Groups[2].Value;
                duckNamesSeen.Add(name);
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

        // An exemption nothing uses any more is an exemption waiting for the next lookup to hide
        // behind. Powers and Title sat here after the reads they covered became typed.
        var staleDuckNames = DuckTypedNames.Keys.Where(name => !duckNamesSeen.Contains(name)).OrderBy(name => name).ToArray();
        Assert.True(
            staleDuckNames.Length == 0,
            "DuckTypedNames lists names no reflection call uses any more: " + string.Join(", ", staleDuckNames)
            + ". Drop them.");
    }

    public static void NoHelperTakesAGameMemberNameAsAParameter()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var channelsSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, text) in GameFacingSources(includeRegistry: false))
        {
            var declarations = StaticMethodDeclaration.Matches(text);
            foreach (Match match in ReflectionByVariable.Matches(text))
            {
                var enclosing = declarations
                    .Where(declaration => declaration.Index < match.Index)
                    .Select(declaration => declaration.Groups[1].Value)
                    .LastOrDefault() ?? "(no enclosing static method)";
                if (NameTakingChannels.ContainsKey(enclosing))
                {
                    channelsSeen.Add(enclosing);
                    continue;
                }

                offenders.Add($"{match.Groups[1].Value}({match.Groups[2].Value}) in {enclosing}, {Path.GetFileName(path)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These look a member up by a name held in a variable, outside the declared channels:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nA helper that takes the name as a parameter hides every caller's name from the literal "
            + "scan -- which is how four private lobby and pause-menu methods went unprobed. Register the "
            + "members and ask ReflectedGameMembers for them, or argue for the helper in NameTakingChannels.");

        var unused = NameTakingChannels.Keys.Where(name => !channelsSeen.Contains(name)).OrderBy(name => name).ToArray();
        Assert.True(
            unused.Length == 0,
            "NameTakingChannels lists helpers that no longer look anything up by a variable name: "
            + string.Join(", ", unused)
            + ". Drop them -- or, if they still do, the extraction in this test stopped seeing them.");
    }

    public static void GodotIsNeverCalledByAString()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in GameFacingSources(includeRegistry: false))
        {
            foreach (Match match in GodotCallByString.Matches(text))
            {
                var line = text[..match.Index].Count(ch => ch == '\n') + 1;
                offenders.Add($"{match.Groups[1].Value}(\"...\") at {Path.GetFileName(path)}:{line}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These reach into a Godot node by a string name:\n  " + string.Join("\n  ", offenders)
            + "\n\nUse the generated MethodName / SignalName / PropertyName constant of the node's own type, "
            + "or call the method directly when it is public. A string is a lookup the compiler cannot "
            + "check: confirm_bundle called OnConfirmPressed, which the installed game does not declare, "
            + "on every use.");
    }

    public static void GameButtonsAreClickedRatherThanSignalled()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in GameFacingSources(includeRegistry: false))
        {
            foreach (Match match in GodotButtonSignal.Matches(text))
            {
                var line = text[..match.Index].Count(ch => ch == '\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These emit Godot's Button pressed signal: " + string.Join(", ", offenders)
            + "\n\nThe game's NButton derives from NClickableControl -> Control, not from BaseButton, so it "
            + "has no pressed signal and the emit does nothing. The constant compiles because it names "
            + "BaseButton, not the button's own type. Use ForceClick(). choose_capstone_option and "
            + "continue_game_over both did this, and only the second had a ForceClick behind it.");
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
