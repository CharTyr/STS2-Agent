using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// <c>docs/project-map.md</c> is the orientation document an agent reads instead of re-exploring the
/// repository at the start of every task. It is only worth reading if it is current, so this keeps
/// it honest in both directions: every mod source file and every Python MCP module must be named in
/// it (a new file forces a map line in the same change), and every source path the map names must
/// still exist (a rename or deletion cannot leave a dangling pointer).
/// </summary>
internal static class ProjectMapFreshnessTests
{
    private const string MapPath = "docs/project-map.md";

    private static readonly Regex SourcePathPattern = new(
        @"(?<![\w/.-])((?:STS2AIAgent|mcp_server)/[\w./-]+?\.(?:cs|py))(?![\w])",
        RegexOptions.Compiled);

    public static void EverySourceFileIsMapped()
    {
        var root = AgentSourceFixture.Root;
        var map = File.ReadAllText(Path.Combine(root, MapPath));
        var missing = TrackedSources(root)
            .Where(path => !map.Contains(path, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{MapPath} must name every mod source and MCP module (one line: path — responsibility). "
            + "Add a line for:\n  " + string.Join("\n  ", missing));
    }

    public static void EveryMappedPathExists()
    {
        var root = AgentSourceFixture.Root;
        var map = File.ReadAllText(Path.Combine(root, MapPath));
        var dangling = SourcePathPattern.Matches(map)
            .Select(match => match.Groups[1].Value.TrimEnd('.'))
            .Distinct(StringComparer.Ordinal)
            .Where(path => !File.Exists(Path.Combine(root, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            dangling.Count == 0,
            $"{MapPath} names source files that no longer exist; update or drop them:\n  "
            + string.Join("\n  ", dangling));
    }

    private static IEnumerable<string> TrackedSources(string root)
    {
        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            yield return Path.GetRelativePath(root, path).Replace('\\', '/');
        }

        var mcpPackage = Path.Combine(root, "mcp_server", "src", "sts2_mcp");
        foreach (var path in Directory.EnumerateFiles(mcpPackage, "*.py", SearchOption.TopDirectoryOnly))
        {
            yield return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
    }
}
