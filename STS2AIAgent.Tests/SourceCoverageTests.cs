using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace STS2AIAgent.Tests;

/// <summary>
/// Syntax safety net for the mod sources this test project cannot compile. Those files reference the
/// game assemblies (Godot / MegaCrit.Sts2), so they never enter the offline compilation, and a
/// syntax error in one of them would otherwise reach CI unnoticed. These tests parse every source
/// file and keep the set of unlinked files explicit.
/// </summary>
internal static class SourceCoverageTests
{
    /// <summary>
    /// The tracked mod sources deliberately kept out of the offline compilation because they need the
    /// game assemblies. Every entry is still parsed by
    /// <see cref="EveryModSourceParsesWithoutSyntaxErrors"/>, so listing one here is the explicit
    /// decision that it stays unlinked. A new file that is neither linked nor listed here turns
    /// <see cref="UncompiledSourcesMatchTheDeclaredWhitelist"/> red.
    /// </summary>
    private static readonly string[] KnownUncompiledSources =
    {
        "STS2AIAgent/Game/GameStateService.cs",
        "STS2AIAgent/Game/GameActionService.cs",
        "STS2AIAgent/Ui/AgentOverlayHost.cs",
        "STS2AIAgent/Agent/AgentRuntime.cs",
        "STS2AIAgent/Game/GameDataExportService.cs",
        "STS2AIAgent/Server/Router.cs",
        "STS2AIAgent/Server/GameEventService.cs",
        "STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs",
        "STS2AIAgent/Agent/GameBridge.cs",
        "STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs",
        "STS2AIAgent/Multiplayer/CoopSaveProbe.cs",
        "STS2AIAgent/Server/HttpServer.cs",
        "STS2AIAgent/Ui/UiFactory.cs",
        "STS2AIAgent/Game/GameThread.cs",
        "STS2AIAgent/Localization/LocSource.cs",
        "STS2AIAgent/ModEntry.cs",
        "STS2AIAgent/Vision/ScreenshotService.cs",
    };

    public static void EveryModSourceParsesWithoutSyntaxErrors()
    {
        var files = AgentSourceFixture.SourceFiles().OrderBy(path => path, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);

        var errors = new List<string>();
        foreach (var path in files)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
            var firstError = tree
                .GetDiagnostics()
                .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (firstError is null)
            {
                continue;
            }

            var position = firstError.Location.GetLineSpan().StartLinePosition;
            errors.Add($"{RepoRelative(path)}:{position.Line + 1} {firstError.Id}: {firstError.GetMessage()}");
            if (errors.Count == 10)
            {
                errors.Add($"... {files.Count} file(s) scanned; stopped after 10 parse errors");
                break;
            }
        }

        Assert.True(
            errors.Count == 0,
            "Every mod source must parse as C# even when it cannot be compiled offline. Parse error(s):\n  "
            + string.Join("\n  ", errors));
    }

    public static void UncompiledSourcesMatchTheDeclaredWhitelist()
    {
        var compiled = CompiledSourcePaths();
        var uncompiled = TrackedModSourcePaths()
            .Where(path => !compiled.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var unexpected = uncompiled
            .Except(KnownUncompiledSources, StringComparer.Ordinal)
            .ToList();
        var missing = KnownUncompiledSources
            .Except(uncompiled, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "Every tracked mod source must either be <Compile Include>d in "
            + "STS2AIAgent.Tests/STS2AIAgent.Tests.csproj or listed in KnownUncompiledSources. "
            + "Unexpected uncompiled source(s):\n  " + string.Join("\n  ", unexpected));
        Assert.True(
            missing.Count == 0,
            "KnownUncompiledSources entries are no longer uncompiled tracked sources; drop or fix them:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>Repo-relative paths of the sources the test project compiles, wildcards expanded.</summary>
    private static HashSet<string> CompiledSourcePaths()
    {
        var projectDirectory = Path.Combine(AgentSourceFixture.Root, "STS2AIAgent.Tests");
        var compiled = new HashSet<string>(StringComparer.Ordinal);
        var includes = XDocument
            .Load(Path.Combine(projectDirectory, "STS2AIAgent.Tests.csproj"))
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        foreach (var include in includes)
        {
            var path = Path.GetFullPath(
                Path.Combine(projectDirectory, include!.Replace('\\', Path.DirectorySeparatorChar)));
            var pattern = Path.GetFileName(path);
            if (!pattern.Contains('*'))
            {
                compiled.Add(RepoRelative(path));
                continue;
            }

            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException($"Compile include has no directory: {include}");
            foreach (var match in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                compiled.Add(RepoRelative(match));
            }
        }

        return compiled;
    }

    /// <summary>
    /// Repo-relative paths of the tracked mod sources. The filesystem scan also sees gitignored local
    /// artifacts (for example the ignored <c>STS2AIAgent/_scratch/</c> folder), so the whitelist
    /// comparison asks git which files are real sources. Without git the tree is treated as a fresh
    /// checkout, where every scanned file is tracked.
    /// </summary>
    private static IReadOnlyCollection<string> TrackedModSourcePaths()
    {
        if (TryListTrackedModSources(out var tracked))
        {
            return tracked;
        }

        return new HashSet<string>(
            AgentSourceFixture.SourceFiles().Select(RepoRelative),
            StringComparer.Ordinal);
    }

    private static bool TryListTrackedModSources(out HashSet<string> tracked)
    {
        tracked = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = AgentSourceFixture.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("-z");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("STS2AIAgent");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000) || process.ExitCode != 0)
            {
                return false;
            }

            foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.EndsWith(".cs", StringComparison.Ordinal))
                {
                    tracked.Add(entry.Replace('\\', '/'));
                }
            }

            return tracked.Count > 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            tracked.Clear();
            return false;
        }
    }

    private static string RepoRelative(string absolutePath) =>
        Path.GetRelativePath(AgentSourceFixture.Root, absolutePath).Replace('\\', '/');
}
