# Combat live evidence
Clean mods (STS2AIAgent only): embarked to MAP, both instances voted node 0, entered COMBAT.
Companion play_card BASH target 0: enemy 83->75, energy 3->1, status completed.
end_turn completed; both screens stayed COMBAT during enemy/transition with empty available_actions.
state-invariants after the playable-cards-during-empty-action-set fix: host and companion failure_count=0.
DamageMeter previously crashed StartNewMultiplayerRun via RunRngSet.get_Seed(); removed from mods for this combat path.
