namespace STS2AIAgent.Game;

/// <summary>
/// Remembers that the player skipped a card reward, keyed to the reward set that recorded
/// it. The intent must outlive the request that set it (<c>skip_reward_cards</c> is one call
/// and the <c>collect_rewards_and_proceed</c> that follows is another), so it is stored here
/// rather than threaded through the request; the reward-set id keeps it from ever applying to
/// a different reward set.
/// </summary>
internal sealed class RewardSkipScope
{
    private bool _skipped;
    private ulong _rewardSetId;

    /// <summary>
    /// True only for the reward set the skip was recorded in. An unresolved scope (id 0)
    /// never applies, so a missing identity re-shows the reward instead of silently
    /// dropping it.
    /// </summary>
    public bool AppliesTo(ulong rewardSetId)
    {
        return _skipped && _rewardSetId != 0 && _rewardSetId == rewardSetId;
    }

    /// <summary>
    /// Records a skip against the reward set that observed it. A later mark replaces the
    /// previous scope, so only the most recent intent survives.
    /// </summary>
    public void MarkSkipped(ulong rewardSetId)
    {
        _skipped = true;
        _rewardSetId = rewardSetId;
    }

    /// <summary>
    /// Forgets the recorded skip, e.g. because a card was taken or the reward screen was left.
    /// </summary>
    public void Clear()
    {
        _skipped = false;
        _rewardSetId = 0;
    }

    /// <summary>Recorded scope id for diagnostics; 0 when nothing is recorded.</summary>
    public ulong RecordedRewardSetId => _rewardSetId;
}
