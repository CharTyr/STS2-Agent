using System.Security.Cryptography;
using System.Text;

namespace STS2AIAgent.Agent;

/// <summary>
/// Pure no-progress guard for the auto-play retry policy. It covers two defects that pull in
/// opposite directions: an action the game accepts but that changes nothing can repeat forever
/// (no error is ever raised, so the retry budget never grows), and an action that did execute but
/// never reported a stable state used to be reported as a hard failure even though the game did
/// what the model asked. Kept free of Godot and game types so the offline core tests can pin it.
/// </summary>
internal static class NoProgressPolicy
{
    /// <summary>
    /// How many consecutive turns may report the same (action, state) pair before the session is
    /// treated as spinning instead of playing.
    /// </summary>
    public const int RepeatThreshold = 3;

    /// <summary>
    /// How many consecutive executed-but-unconfirmed turns may pass before the session stops.
    /// A slow animation must not fail a turn, but it must not wait forever either.
    /// </summary>
    public const int UnsettledLimit = 5;

    /// <summary>
    /// True when the latest successful action repeats the previous one <em>and</em> the state
    /// fingerprint did not move. A changed action or a changed state is progress. Missing data
    /// never counts as a repeat: a turn without a fingerprint cannot prove the game stood still.
    /// </summary>
    public static bool IsRepeat(
        string? previousAction,
        string? previousFingerprint,
        string? action,
        string? fingerprint)
    {
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(fingerprint))
        {
            return false;
        }

        if (string.IsNullOrEmpty(previousAction) || string.IsNullOrEmpty(previousFingerprint))
        {
            return false;
        }

        return string.Equals(previousAction, action, StringComparison.Ordinal)
            && string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cheap, stable fingerprint of a compact state string ("is this the same situation?").
    /// SHA-256 truncated to 16 hex characters: enough to compare consecutive states without
    /// keeping the JSON around. Null in, null out, so a turn that read no state is never
    /// mistaken for an unchanged one.
    /// </summary>
    public static string? Fingerprint(string? compactStateJson)
    {
        if (string.IsNullOrEmpty(compactStateJson))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(compactStateJson));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
