using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline tests for Phase 3 patch-resilience mechanisms: per-section state fault isolation
/// (<see cref="StateSectionFaultTracker"/>) and the ECMA-335 ABI &amp; reflection metadata auditor
/// (<see cref="GameAbiAuditor"/>).
/// </summary>
internal static class GameAbiAndFaultTests
{
    public static void FaultTrackerDeduplicatesLogsAndRecoversCleanly()
    {
        var tracker = new StateSectionFaultTracker();
        var t0 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.False(tracker.HasActiveFaults);
        Assert.Equal(0, tracker.Snapshot().Length);

        var ex = new MissingMethodException("PurchaseLocalEntry");
        Assert.True(tracker.RecordFault("shop", ex, t0), "First fault for a section must log.");
        Assert.False(tracker.RecordFault("shop", ex, t0.AddSeconds(5)), "Identical fault inside cooldown must not spam logs.");
        Assert.True(tracker.RecordFault("shop", ex, t0.AddSeconds(61)), "Same fault after cooldown logs again.");
        Assert.True(tracker.RecordFault("shop", new InvalidOperationException("Different"), t0.AddSeconds(62)), "New error signature logs immediately.");

        Assert.True(tracker.HasActiveFaults);
        var snap = tracker.Snapshot();
        Assert.Equal(1, snap.Length);
        Assert.Equal("shop", snap[0].section);
        Assert.True(snap[0].active);
        Assert.Equal(4, snap[0].count);
        Assert.Equal("InvalidOperationException", snap[0].error_type);

        tracker.RecordSuccess("shop");
        Assert.False(tracker.HasActiveFaults, "Recovery clears active fault flag.");
        var afterRecovery = tracker.Snapshot();
        Assert.Equal(1, afterRecovery.Length, "Historical count remains visible for diagnostics.");
        Assert.False(afterRecovery[0].active);
        Assert.Equal(4, afterRecovery[0].count);
    }

    public static void FaultTrackerBoundsTrackedSections()
    {
        var tracker = new StateSectionFaultTracker();
        var t0 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < StateSectionFaultTracker.MaxTrackedSections + 5; i++)
        {
            tracker.RecordFault($"sec_{i:D2}", new Exception("boom"), t0.AddSeconds(i));
            tracker.RecordSuccess($"sec_{i:D2}");
        }

        Assert.Equal(StateSectionFaultTracker.MaxTrackedSections, tracker.Snapshot().Length);
    }

    public static void AbiAuditorDetectsValidAndMissingReflectedEntriesOffline()
    {
        var testDll = typeof(GameAbiAndFaultTests).Assembly.Location;
        var dir = Path.GetDirectoryName(testDll)!;
        var selfAsm = Path.GetFileNameWithoutExtension(testDll);

        var okRegistry = """
            new(typeof(StateSectionFaultTracker), "_gate", MemberKind.Field, false, "test field"),
            new(typeof(StateSectionFaultTracker), "RecordFault", MemberKind.Method, false, "test method"),
            new(typeof(StateSectionFaultTracker), "HasActiveFaults", MemberKind.Property, false, "test prop"),
            """;
        var okReport = GameAbiAuditor.Audit(testDll, dir, okRegistry, new[] { selfAsm });
        Assert.True(okReport.Ok, "Valid reflected members on self assembly must pass.");
        Assert.Equal(3, okReport.CheckedReflected);
        Assert.Equal(0, okReport.MissingReflected.Count);

        var brokenRegistry = """
            new(typeof(StateSectionFaultTracker), "_renamedField", MemberKind.Field, false, "broken"),
            new(typeof(StateSectionFaultTracker), "_gate", MemberKind.Field, true, "wrong staticness"),
            new(typeof(NonExistentGameScreen), "_items", MemberKind.Field, false, "missing type"),
            """;
        var badReport = GameAbiAuditor.Audit(testDll, dir, brokenRegistry, new[] { selfAsm });
        Assert.False(badReport.Ok, "Missing field, wrong staticness, and missing type must fail the audit.");
        Assert.Equal(3, badReport.MissingReflected.Count);
        Assert.True(badReport.MissingReflected.Any(item => item.Contains("_renamedField", StringComparison.Ordinal)));
        Assert.True(badReport.MissingReflected.Any(item => item.Contains("_gate", StringComparison.Ordinal)));
        Assert.True(badReport.MissingReflected.Any(item => item.Contains("NonExistentGameScreen", StringComparison.Ordinal)));
    }

    public static void AbiAuditorCatchesRealCrossAssemblyPropertySignatureVisibilityAndStaticBreaks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sts2-abi-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetPath = Path.Combine(tempDir, "FakeGame.dll");
            var consumerPath = Path.Combine(tempDir, "FakeMod.dll");

            var baselineSource = """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public string Title => "Strike";
                    public int Compute(int value) => value + 1;
                }
                """;
            CompileToDll("FakeGame", baselineSource, targetPath);

            var consumerSource = """
                using FakeNs;
                namespace FakeMod;
                public static class Runner
                {
                    public static int Run()
                    {
                        var card = new FakeCard();
                        return card.Title.Length + card.PublicScore + card.Compute(5);
                    }
                }
                """;
            CompileToDll("FakeMod", consumerSource, consumerPath, targetPath);

            var registry = """
                new(typeof(FakeCard), "Compute", MemberKind.Method, false, "compute"),
                """;
            var pass = GameAbiAuditor.Audit(consumerPath, tempDir, registry, new[] { "FakeGame" });
            Assert.True(pass.Ok, "Baseline consumer against matching FakeGame must pass.");
            Assert.True(pass.CheckedTypes >= 1, "Must check cross-assembly TypeReferences.");
            Assert.True(pass.CheckedMembers >= 4, "Must check .ctor, get_Title, Compute, and PublicScore.");

            // 1. Delete Title property getter — previously masked by external-base wildcard.
            CompileToDll("FakeGame", """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public int Compute(int value) => value + 1;
                }
                """, targetPath);
            var missingGetter = GameAbiAuditor.Audit(consumerPath, tempDir, null, new[] { "FakeGame" });
            Assert.False(missingGetter.Ok, "Deleting a property getter on a game class must fail ABI audit.");
            Assert.True(missingGetter.MissingMembers.Any(m => m.Contains("get_Title", StringComparison.Ordinal)));

            // 2. Change parameter type Compute(int) -> Compute(string).
            CompileToDll("FakeGame", """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public string Title => "Strike";
                    public int Compute(string value) => value.Length;
                }
                """, targetPath);
            var changedParam = GameAbiAuditor.Audit(consumerPath, tempDir, null, new[] { "FakeGame" });
            Assert.False(changedParam.Ok, "Changing a method parameter type must fail ABI audit.");
            Assert.True(changedParam.MissingMembers.Any(m => m.Contains("Compute", StringComparison.Ordinal)));

            // 3. Narrow visibility Public -> internal.
            CompileToDll("FakeGame", """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public string Title => "Strike";
                    internal int Compute(int value) => value + 1;
                }
                """, targetPath);
            var madeInternal = GameAbiAuditor.Audit(consumerPath, tempDir, null, new[] { "FakeGame" });
            Assert.False(madeInternal.Ok, "Narrowing a referenced method to internal must fail ABI audit.");

            // 4. Flip instance -> static.
            CompileToDll("FakeGame", """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public string Title => "Strike";
                    public static int Compute(int value) => value + 1;
                }
                """, targetPath);
            var flippedStatic = GameAbiAuditor.Audit(consumerPath, tempDir, null, new[] { "FakeGame" });
            Assert.False(flippedStatic.Ok, "Flipping an instance method to static must fail ABI audit.");

            // 5. Add a second overload Compute(int, int) -> breaks reflected unique-overload lookup.
            CompileToDll("FakeGame", """
                namespace FakeNs;
                public class FakeCard
                {
                    public int PublicScore;
                    public string Title => "Strike";
                    public int Compute(int value) => value + 1;
                    public int Compute(int a, int b) => a + b;
                }
                """, targetPath);
            var ambiguousReflected = GameAbiAuditor.Audit(consumerPath, tempDir, registry, new[] { "FakeGame" });
            Assert.False(ambiguousReflected.Ok, "Adding a second overload must fail reflected unique-method audit.");
            Assert.True(ambiguousReflected.MissingReflected.Any(m => m.Contains("Compute", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static void CompileToDll(string assemblyName, string source, string outputPath, params string[] extraReferences)
    {
        var refs = new List<MetadataReference>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var entry in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Path.GetFileNameWithoutExtension(entry);
                if (name is "System.Private.CoreLib" or "System.Runtime" && File.Exists(entry))
                {
                    refs.Add(MetadataReference.CreateFromFile(entry));
                }
            }
        }

        foreach (var extra in extraReferences)
        {
            refs.Add(MetadataReference.CreateFromFile(extra));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(outputPath);
        Assert.True(emit.Success, "Fixture compilation failed: " + string.Join("; ", emit.Diagnostics));
    }
}
