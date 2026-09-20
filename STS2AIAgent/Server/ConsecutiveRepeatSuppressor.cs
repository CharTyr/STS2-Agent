namespace STS2AIAgent.Server;

/// <summary>
/// Drops an event that is byte-for-byte the same as the one published immediately before it.
/// </summary>
/// <remarks>
/// <c>ProcessStateLocked</c> publishes from a diff against the previous sample, but several of its
/// branches re-publish the same payload every poll while a state persists -- a stationary map, an
/// open event, a hand that has not changed. A client that already received that frame learns
/// nothing from the repeat, and the 120 ms poll turned it into a frame per tick. Suppressing only
/// the *immediately* consecutive duplicate keeps this honest: a state that genuinely returns after
/// another event in between is published again, because the client has seen something else since.
/// Callers must hold whatever lock guards the publish path; this type holds none.
/// </remarks>
internal sealed class ConsecutiveRepeatSuppressor
{
    private string? _lastSignature;

    /// <summary>
    /// True when <paramref name="signature"/> differs from the previous accepted one. Accepting
    /// updates the remembered signature, so the caller publishes exactly when this returns true.
    /// </summary>
    public bool ShouldPublish(string signature)
    {
        if (string.Equals(_lastSignature, signature, StringComparison.Ordinal))
        {
            return false;
        }

        _lastSignature = signature;
        return true;
    }

    /// <summary>Forgets the last signature, so the next event is published whatever it is.</summary>
    public void Reset()
    {
        _lastSignature = null;
    }
}
