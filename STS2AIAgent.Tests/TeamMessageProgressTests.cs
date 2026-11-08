using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// The pending-message line the host shows while it waits for the teammate.
/// </summary>
/// <remarks>
/// Pure input/output on purpose: no game, no companion process, no real clock. The arithmetic and the
/// wording are the parts that used to be missing (the line froze on the text that was true at send
/// time), so they are the parts pinned here.
/// </remarks>
internal static class TeamMessageProgressTests
{
    public static void ElapsedSecondsAreWholeClampedAndClockSafe()
    {
        Assert.Equal(0, TeamMessageProgress.ElapsedSeconds(0, 5_000));       // nothing pending
        Assert.Equal(0, TeamMessageProgress.ElapsedSeconds(1_000, 1_999));   // under one second
        Assert.Equal(1, TeamMessageProgress.ElapsedSeconds(1_000, 2_000));
        Assert.Equal(12, TeamMessageProgress.ElapsedSeconds(1_000, 13_900));
        Assert.Equal(0, TeamMessageProgress.ElapsedSeconds(9_000, 1_000));   // clock ran backwards
        Assert.Equal(
            TeamMessageProgress.MaxSeconds,
            TeamMessageProgress.ElapsedSeconds(1, 100_000_000));             // wall-clock jump
    }

    public static void WaitingLineNamesTheWaitAndTheCombatReason()
    {
        var waiting = TeamMessageProgress.DescribeWaiting(teammateInCombat: false, elapsedSeconds: 7);
        Assert.Contains("7", waiting);
        Assert.Contains("等待", waiting);
        Assert.False(
            waiting.Contains("战斗", StringComparison.Ordinal),
            "A teammate that is not in combat must not be described as fighting.");

        var inCombat = TeamMessageProgress.DescribeWaiting(teammateInCombat: true, elapsedSeconds: 3);
        Assert.Contains("3", inCombat);
        Assert.Contains("战斗", inCombat);
        Assert.False(
            string.Equals(waiting, inCombat, StringComparison.Ordinal),
            "The combat line has to be distinguishable from the ordinary wait.");
    }

    public static void WaitingLineStaysSaneForImpossibleElapsedValues()
    {
        // The getter can catch the clearing timestamp between its two reads; the line still has to
        // read as a wait rather than as a negative or four-billion-second one.
        foreach (var seconds in new[] { -1, -999, int.MinValue })
        {
            var text = TeamMessageProgress.DescribeWaiting(teammateInCombat: false, elapsedSeconds: seconds);
            Assert.Contains("已等待 0 秒", text);
        }

        var clamped = TeamMessageProgress.DescribeWaiting(teammateInCombat: true, elapsedSeconds: int.MaxValue);
        Assert.Contains("已等待 " + TeamMessageProgress.MaxSeconds + " 秒", clamped);
    }
}
