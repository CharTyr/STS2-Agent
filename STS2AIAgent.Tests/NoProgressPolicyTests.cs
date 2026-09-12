using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Truth table for the no-progress guard. Every branch is asserted, so a rewrite that drops one of
/// them fails here instead of only showing up as a spinning auto-play session.
/// </summary>
internal static class NoProgressPolicyTests
{
    public static void IsRepeatNeedsBothTheSameActionAndTheSameState()
    {
        // No previous pair, no current action, or no current fingerprint: nothing proves a stall.
        Assert.False(NoProgressPolicy.IsRepeat(null, null, "play_card", "fp"));
        Assert.False(NoProgressPolicy.IsRepeat("play_card", "fp", null, "fp"));
        Assert.False(NoProgressPolicy.IsRepeat("play_card", "fp", "play_card", null));
        Assert.False(NoProgressPolicy.IsRepeat(null, "fp", "play_card", "fp"));
        Assert.False(NoProgressPolicy.IsRepeat("play_card", null, "play_card", "fp"));
        Assert.False(NoProgressPolicy.IsRepeat(string.Empty, string.Empty, "play_card", "fp"));
        Assert.False(NoProgressPolicy.IsRepeat("play_card", "fp", "play_card", string.Empty));

        // A changed action is progress even when the state looks identical...
        Assert.False(NoProgressPolicy.IsRepeat("end_turn", "fp", "play_card", "fp"));
        // ...and so is a changed state under the same action.
        Assert.False(NoProgressPolicy.IsRepeat("play_card", "fp", "play_card", "other"));

        // Only the exact same (action, fingerprint) pair is a repeat.
        Assert.True(NoProgressPolicy.IsRepeat("play_card", "fp", "play_card", "fp"));
        Assert.True(NoProgressPolicy.IsRepeat("end_turn", "abc123", "end_turn", "abc123"));

        // Action names are API identifiers, so the comparison stays case-sensitive.
        Assert.False(NoProgressPolicy.IsRepeat("play_card", "fp", "Play_Card", "fp"));
    }

    public static void ThresholdsAreNamedConstants()
    {
        // The retry loop stops at these, so pinning them keeps a magic number from creeping in.
        Assert.Equal(3, NoProgressPolicy.RepeatThreshold);
        Assert.Equal(5, NoProgressPolicy.UnsettledLimit);
    }

    public static void FingerprintOnlyTracksTheCompactStateText()
    {
        Assert.Null(NoProgressPolicy.Fingerprint(null));
        Assert.Null(NoProgressPolicy.Fingerprint(string.Empty));

        var state = """{"screen":"COMBAT","floor":3}""";
        var first = NoProgressPolicy.Fingerprint(state);
        Assert.NotNull(first);
        Assert.Equal(16, first!.Length);

        // Same state -> same fingerprint, so consecutive turns can be compared cheaply.
        Assert.Equal(first, NoProgressPolicy.Fingerprint(state));

        // A different state has to read as progress, and the raw JSON must not be kept around.
        var other = NoProgressPolicy.Fingerprint("""{"screen":"MAP","floor":3}""");
        Assert.False(string.Equals(first, other, StringComparison.Ordinal));
        Assert.False(first.Contains("screen", StringComparison.Ordinal));
    }
}
