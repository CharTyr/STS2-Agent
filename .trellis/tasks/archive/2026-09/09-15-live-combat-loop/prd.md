# Complete isolated co-op combat through rewards

## Goal
On a clean-mods isolated host, drive both instances through a full first fight until rewards are resolved and both return to MAP. Previous session stopped during the enemy/transition turn.

## Constraints
- Isolated launch only: windowed, --force-steam off, host API 18080, companion 18081.
- Do not touch the Steam profile save under AppData\\Roaming\\SlayTheSpire2\\steam\\.
- Temporarily remove third-party mods (DamageMeter, SpeedX, RemoveMultiplayerPlayerLimit, 储君娘化) before combat; restore them afterwards even on failure. DamageMeter throws MissingMethodException on RunRngSet.get_Seed() and aborts co-op embark.
- Companion on the external-takeover route will not play or vote by itself. Drive 18081 over HTTP.
- Do not spend paid model quota. Keep auto_play false.
- Do not commit, push, or release.
- Do not edit product C# / Python except evidence files under this task directory.

## Acceptance Criteria
- [ ] Host and companion both enter COMBAT after voting the same map node.
- [ ] At least one successful play_card with before/after HP or energy evidence.
- [ ] Combat completes; both instances leave COMBAT.
- [ ] Reward screen is resolved (choose_reward_card / skip_reward_cards / collect_rewards_and_proceed as advertised).
- [ ] Both instances return to MAP with choose_map_node available, or document the exact blocking screen with payloads.
- [ ] state-invariants failure_count=0 on host and companion at MAP after the fight, or a filed invariant defect with payloads.
- [ ] Evidence written to this task''s evidence.md with timestamps, action names, status, and compact before/after fields.
