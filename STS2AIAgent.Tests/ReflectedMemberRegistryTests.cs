using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The registry that <c>GET /health</c> probes must name every private game member the mod reads.
/// </summary>
/// <remarks>
/// <c>ReflectedGameMembers</c> only helps while it is complete. A new reflection site added without
/// a registry entry is a member nobody probes, which is exactly the blind spot the registry exists
/// to close -- and it would close silently, because a missing entry breaks nothing and shows up
/// nowhere.
///
/// So this counts. Every private member name the mod reflects out of the game either appears in the
/// registry or on <see cref="NotProbed"/> with a reason. The registry's *types* need no contract:
/// they are <c>typeof</c> expressions, so the compiler already checks them against the game
/// assembly the project references.
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
    /// Member names the registry deliberately does not probe, and why. An entry here is a claim
    /// that a startup probe would be wrong, not that the member does not matter.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotProbed =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Six concrete card-grid screens each declare their own _prefs/_selectedCards; the base
            // type declares neither. A probe would have to name every subclass, and would then
            // report a false miss the day the game adds or drops one. The reader already guards on
            // the base type and fails safe when the fields are absent, so absence costs a metadata
            // block rather than a wrong number.
            ["_prefs"] = "declared per concrete card-grid screen; the reader guards on the base type and fails safe",
            ["_selectedCards"] = "declared per concrete card-grid screen; the reader guards on the base type and fails safe",
        };

    private static readonly Regex PrivateMemberLiteral = new(@"""(_[a-zA-Z][a-zA-Z0-9_]*)""", RegexOptions.Compiled);
    private static readonly Regex RegistryEntry = new(@"""([A-Za-z_][A-Za-z0-9_]*)"",\s*MemberKind\.", RegexOptions.Compiled);

    // A rewrite that defeats the extraction must fail loudly rather than read as "nothing to check".
    private const int MinimumEntries = 15;

    public static void EveryReflectedMemberIsProbedOrExcused()
    {
        var registry = AgentSourceFixture.Read(RegistryPath);
        var probed = RegistryEntry.Matches(registry)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            probed.Count >= MinimumEntries,
            $"{RegistryPath} yielded {probed.Count} entries, below the {MinimumEntries} expected. The "
            + "extraction in this test no longer matches the registry; fix it before trusting this "
            + "contract.");

        var root = AgentSourceFixture.Root;
        var unprobed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relative in GameFacingDirectories)
        {
            var directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                // The registry names these on purpose; reading it back would make the check circular.
                if (Path.GetFileName(path) == Path.GetFileName(RegistryPath))
                {
                    continue;
                }

                foreach (Match match in PrivateMemberLiteral.Matches(File.ReadAllText(path)))
                {
                    var member = match.Groups[1].Value;
                    if (!probed.Contains(member) && !NotProbed.ContainsKey(member))
                    {
                        unprobed.Add($"{member} (in {Path.GetFileName(path)})");
                    }
                }
            }
        }

        Assert.True(
            unprobed.Count == 0,
            "These private game members are read by name but neither probed by "
            + "ReflectedGameMembers nor listed in NotProbed with a reason:\n  "
            + string.Join("\n  ", unprobed)
            + "\n\nA member nobody probes is one the game can rename without anything noticing: the "
            + "read returns null, the call site falls back to a default, and an agent is handed a "
            + "number the mod invented.");
    }

    /// <summary>
    /// A registry entry for a member no source reads is a probe reporting on something that no
    /// longer matters -- and, once the game drops the member, a permanent false alarm.
    /// </summary>
    public static void TheRegistryHasNoEntriesNobodyReads()
    {
        var registry = AgentSourceFixture.Read(RegistryPath);
        var probed = RegistryEntry.Matches(registry)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var root = AgentSourceFixture.Root;
        var read = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            if (Path.GetFileName(path) == Path.GetFileName(RegistryPath))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match match in PrivateMemberLiteral.Matches(text))
            {
                read.Add(match.Groups[1].Value);
            }

            // The one probed member that is not a private field: a method the co-op path invokes.
            foreach (Match match in Regex.Matches(text, @"GetMethod\(""([A-Za-z_][A-Za-z0-9_]*)"""))
            {
                read.Add(match.Groups[1].Value);
            }
        }

        var orphans = probed.Where(member => !read.Contains(member)).OrderBy(m => m, StringComparer.Ordinal).ToArray();
        Assert.True(
            orphans.Length == 0,
            "ReflectedGameMembers probes members no source reads any more: "
            + string.Join(", ", orphans)
            + ". Drop them, or /health will keep reporting on something that stopped mattering.");
    }

    /// <summary>
    /// <c>GET /health</c> has to derive its status from the probe rather than assert it.
    /// </summary>
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
}
