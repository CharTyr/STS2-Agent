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
        // Audit our own test assembly against itself: zero game DLLs needed in CI.
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
            new(typeof(NonExistentGameScreen), "_items", MemberKind.Field, false, "missing type"),
            """;
        var badReport = GameAbiAuditor.Audit(testDll, dir, brokenRegistry, new[] { selfAsm });
        Assert.False(badReport.Ok, "Missing reflected field and type must fail the audit.");
        Assert.Equal(2, badReport.MissingReflected.Count);
        Assert.True(badReport.MissingReflected.Any(item => item.Contains("_renamedField", StringComparison.Ordinal)));
        Assert.True(badReport.MissingReflected.Any(item => item.Contains("NonExistentGameScreen", StringComparison.Ordinal)));
    }
}
