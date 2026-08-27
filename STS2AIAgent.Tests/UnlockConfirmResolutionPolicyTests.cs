using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class UnlockConfirmResolutionPolicyTests
{
    public static void PrefersUsableReflectedCandidate()
    {
        var reflected = new Candidate("reflected", Usable: true);
        var fallback = new Candidate("fallback", Usable: true);

        var selected = UnlockConfirmResolutionPolicy.SelectUsable(
            reflected, new[] { fallback }, candidate => candidate.Usable);

        Assert.Equal(reflected, selected);
    }

    public static void SkipsUnusableCandidatesBeforeUsableFallback()
    {
        var reflected = new Candidate("reflected", Usable: false);
        var hiddenFallback = new Candidate("hidden", Usable: false);
        var usableFallback = new Candidate("usable", Usable: true);

        var selected = UnlockConfirmResolutionPolicy.SelectUsable(
            reflected,
            new[] { hiddenFallback, usableFallback },
            candidate => candidate.Usable);

        Assert.Equal(usableFallback, selected);
    }

    public static void ProbeSignatureIncludesScreenInstance()
    {
        var first = UnlockConfirmResolutionPolicy.BuildProbeSignature(
            "NUnlockRelicsScreen", 100, "member", "NUnlockConfirmButton", "/Confirm", true, true);
        var second = UnlockConfirmResolutionPolicy.BuildProbeSignature(
            "NUnlockRelicsScreen", 101, "member", "NUnlockConfirmButton", "/Confirm", true, true);

        Assert.False(string.Equals(first, second, StringComparison.Ordinal));
    }

    private sealed record Candidate(string Name, bool Usable);
}
