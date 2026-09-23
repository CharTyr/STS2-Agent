namespace STS2AIAgent.Agent;

/// <summary>
/// The reasoning a model is still producing, so the conversation can show a thinking model's progress
/// instead of nothing until the turn completes.
/// </summary>
/// <remarks>
/// Deliberately not a chat turn. This is transient display state: it is never persisted to a session
/// file, never replayed to a provider, and never written to the decision log. The completed turn appends
/// the recorded "thought" turn (<c>AgentRuntime.AppendTurnTraces</c>), which calls
/// <see cref="Reset"/> first -- that one call is what keeps a turn from drawing its reasoning twice, so
/// the streamed bubble disappears exactly when the recorded one takes over.
///
/// It stores text only while the player's own reasoning switch is on, which is what makes the default
/// configuration unable to show a model's scratchpad: with the switch off nothing is ever kept, so
/// there is nothing for the overlay to draw.
///
/// Godot-free on purpose. The overlay's control builders cannot be exercised offline, and the two rules
/// worth keeping -- the switch gates storage, and a completed turn leaves nothing behind -- live here
/// where the offline suite compiles them.
/// </remarks>
internal sealed class LiveThoughtBuffer
{
    /// <summary>
    /// How much reasoning the live bubble carries. The recorded thought turn uses this same budget, so
    /// the streamed text and the bubble that replaces it are the same characters rather than two
    /// different clips of one thought.
    /// </summary>
    internal const int MaxChars = 600;

    private readonly object _gate = new();
    private string _text = string.Empty;

    /// <summary>The reasoning so far, or empty when no request is streaming one.</summary>
    public string Text
    {
        get { lock (_gate) return _text; }
    }

    /// <summary>
    /// Records the reasoning the provider has accumulated. Nothing is kept unless the player enabled
    /// reasoning display and the provider actually sent some, so this callback firing on every model
    /// request cannot leak a scratchpad into the default configuration.
    /// </summary>
    public void Report(string? accumulated, bool enabled)
    {
        if (!enabled)
        {
            // The switch can be turned off in the middle of a request. Dropping what was already shown
            // is what makes "nothing is stored while the switch is off" true of the buffer and not only
            // of the drawing: no scratchpad is left waiting in memory for the switch to come back.
            Reset();
            return;
        }

        if (string.IsNullOrWhiteSpace(accumulated))
        {
            return;
        }

        var flat = accumulated.Replace("\r", " ").Replace("\n", " ").Trim();
        var clipped = flat.Length <= MaxChars ? flat : flat[..MaxChars] + "…";
        lock (_gate)
        {
            _text = clipped;
        }
    }

    /// <summary>
    /// Empties the buffer: the recorded turn's own bubble has taken over, a request ended without one,
    /// or a new request is starting. Whichever comes first, no turn may leave a second reasoning bubble
    /// behind it.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _text = string.Empty;
        }
    }
}
