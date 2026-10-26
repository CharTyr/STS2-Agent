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
        "STS2AIAgent/Game/GameStateService.Predicates.cs",
        // The raw /state builders, split by screen on 2026-09-20. They stay unlinked for the same
        // reason the base file does -- they read the game assemblies -- and the relocation contract
        // in GameStateServiceRelocationContractTests is what keeps them honest.
        "STS2AIAgent/Game/GameStateService.Combat.cs",
        "STS2AIAgent/Game/GameStateService.Map.cs",
        "STS2AIAgent/Game/GameStateService.Menus.cs",
        "STS2AIAgent/Game/GameStateService.Potions.cs",
        "STS2AIAgent/Game/GameStateService.Rewards.cs",
        "STS2AIAgent/Game/GameStateService.Rooms.cs",
        "STS2AIAgent/Game/GameStateService.Run.cs",
        "STS2AIAgent/Game/GameStateService.Shop.cs",
        "STS2AIAgent/Game/ReflectedGameMembers.cs",
        "STS2AIAgent/Game/GameStateService.AgentView.cs",
        // The lethal-risk half of the combat payload (intents, poison, constrict), split out of
        // GameStateService.Combat.cs on 2026-10-02 for the size budget. It reads the game's power
        // models, so it stays out of the offline compilation like the rest of the partial.
        "STS2AIAgent/Game/GameStateService.CombatRisks.cs",
        "STS2AIAgent/Game/GameStateService.Payloads.cs",
        "STS2AIAgent/Game/GameActionService.cs",
        "STS2AIAgent/Game/GameActionService.Combat.cs",
        "STS2AIAgent/Game/GameActionService.Coop.cs",
        "STS2AIAgent/Game/GameActionService.Embark.cs",
        "STS2AIAgent/Game/GameActionService.Menus.cs",
        "STS2AIAgent/Game/GameActionService.Rewards.cs",
        "STS2AIAgent/Game/GameActionService.Rooms.cs",
        "STS2AIAgent/Game/GameActionService.Run.cs",
        "STS2AIAgent/Game/GameActionService.Shop.cs",
        "STS2AIAgent/Ui/AgentOverlayHost.cs",
        "STS2AIAgent/Ui/AgentOverlayHost.Tabs.cs",
        // The page bodies and the settings form, split out of the host when it hit its size budget on
        // 2026-10-01. They stay unlinked for the same reason the base file does -- they build Godot
        // controls and read the game -- and the Roslyn pass above still parses them, so a syntax error
        // in either one is still caught offline.
        "STS2AIAgent/Ui/AgentOverlayHost.Pages.cs",
        "STS2AIAgent/Ui/AgentOverlayHost.Settings.cs",
        // The palette preview, split out of UiFactory.cs for the same budget. It needs Godot's ColorRect.
        "STS2AIAgent/Ui/OverlaySwatch.cs",
        "STS2AIAgent/Agent/AgentRuntime.cs",
        // Idle chat remains on the game runtime and is split out so the base runtime stays below its
        // source-size budget. The Roslyn pass and runtime source contracts still cover it.
        "STS2AIAgent/Agent/AgentRuntime.Chat.cs",
        // The teammate partial stays unlinked with AgentRuntime.cs: both need the game runtime,
        // while this suite's Roslyn pass still parses them and the explicit list makes the choice
        // visible rather than letting a new source file evade every offline contract.
        "STS2AIAgent/Agent/AgentRuntime.Team.cs",
        // Accounting remains on the game runtime, but is separated from UI status updates.
        "STS2AIAgent/Agent/AgentRuntime.Accounting.cs",
        // The Jev surface of the runtime (the shared strategy store and the decider construction). An
        // AgentRuntime partial, so it stays unlinked with the base file; the Roslyn pass still parses it.
        "STS2AIAgent/Agent/AgentRuntime.Jev.cs",
        // The play-session persistence wiring (dirty tracking, flush, restore around the run boundary).
        // An AgentRuntime partial touching the live history/decisions, so it stays unlinked too.
        "STS2AIAgent/Agent/AgentRuntime.Session.cs",
        // The per-model verification surface (ping + tool-calling probe, recorded per model). An
        // AgentRuntime partial, so it stays unlinked with the base file; the Roslyn pass still parses it.
        "STS2AIAgent/Agent/AgentRuntime.ModelTest.cs",
        // The status line's writers (settled status, in-flight request flag, per-phase elapsed clock).
        // An AgentRuntime partial reading the status fields, so it stays unlinked with the base file.
        "STS2AIAgent/Agent/AgentRuntime.Status.cs",
        // The audit-remediation partials (companion settings push, solo/co-op mode control, the
        // external play-control route, run-scoped play instructions). AgentRuntime partials, so they
        // stay unlinked with the base file; the Roslyn pass still parses them.
        "STS2AIAgent/Agent/AgentRuntime.CompanionSettings.cs",
        "STS2AIAgent/Agent/AgentRuntime.ModeControl.cs",
        "STS2AIAgent/Agent/AgentRuntime.PlayControl.cs",
        "STS2AIAgent/Agent/AgentRuntime.PlayInstructions.cs",
        // The play page's run/pause controls and Jev reading. A Godot control builder, unlinked like
        // the pages file it serves.
        "STS2AIAgent/Ui/AgentOverlayHost.PlayControl.cs",
        // The conversation card (chat log rendering, scroll-to-bottom, the chat-side option row). A
        // Godot control builder, unlinked like the pages file it was split out of.
        "STS2AIAgent/Ui/AgentOverlayHost.ChatCard.cs",
        // The play page's solo/multiplayer mode switch. A Godot control, so it stays unlinked like the
        // overlay host it serves.
        "STS2AIAgent/Ui/SegmentedSwitch.cs",
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
