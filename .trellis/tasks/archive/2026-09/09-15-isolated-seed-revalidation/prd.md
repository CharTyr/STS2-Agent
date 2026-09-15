# Revalidate isolated clientId mod seed

## Goal
Prove scripts/start-game-session.ps1 seeds a brand-new --clientId so STS2AIAgent actually loads.

## Constraints
- Use a clientId that does not already have settings.save (not 2026091001).
- Isolated: --windowed --force-steam off, API 18080.
- Do not use the Steam profile save.
- Restore any moved mods after the check.
- Do not commit, push, or release.

## Acceptance Criteria
- [ ] Fresh clientId start prints the seed log line.
- [ ] GET /health on 18080 succeeds with mod_version populated.
- [ ] A second start against the same clientId does not wipe an already-enabled mods_enabled=true file.
- [ ] Evidence in this task directory includes the clientId, health JSON, and confirmation that AppData\\Roaming\\SlayTheSpire2\\steam was not used.
