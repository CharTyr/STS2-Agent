# Localization acceptance - 2026-09-12

The overlay follows the game language. This records what was actually observed, not what the code
looks like it should do.

## Environment

- Isolated game copy: `build/validation-2026-09-08/game/SlayTheSpire2.exe`, launched with
  `--windowed --force-steam off --clientId 2026091012`.
- Mod built and deployed to that copy only (`build-mod.ps1 -GameRoot ...`); the Steam install was
  not touched. Window screenshots via `PrintWindow` with DPI awareness; the overlay is composed
  separately from the game window, so a plain `CopyFromScreen` capture misses it.
- The game's own language was read from `settings.save` and edited there between runs; the
  language dropdown inside the game was also used, which is what proves the mid-session path.
- Both affected `settings.save` files (the Steam profile and the isolated `clientId` profile) were
  backed up before editing and restored afterwards.

## Cold start, English

- `/health` → `{"health":"ready"}`; log shows `Loading locale path=res://localization/eng`.
- Overlay: `STS2 AI Agent` / `Drag to move` / `Hide`; tabs `AI teammate`, `Chat`, `Play`,
  `Settings`, `Connect`; body `Climb the tower with AI`, `Setup not verified`, `Dual-instance has
  not started.`, `Teammate control is not connected yet.`, `Pause teammate`, `Resume`, `Team chat`,
  `Reset session stats`, `Export diagnostics`, `Invite AI teammate`.
- Settings page: `First-time setup`, `Endpoints`, `Models`, `Role binding`, `Add endpoint`,
  `Add model`, `Save settings`, `Test connection`, `Delete`, `Enabled`.

## Mid-session switch, without restarting

- Started in Chinese, opened the game's own Settings → Language dropdown (listed `English`, `中文`,
  `繁體中文`, ...), picked `English`.
- The game relocalized and the overlay followed in the same session: the same panel switched from
  `和 AI 一起爬塔` / `尚未启动双开。` / `队友控制尚未连接。` / `隐藏` / `对话` to `Climb the tower with
  AI` / `Dual-instance has not started.` / `Teammate control is not connected yet.` / `Hide` /
  `Chat`. No restart.
- Two status lines stayed Chinese on the first attempt. That was a real defect: they were resolved
  while a field was initialized, so they froze in the language active at construction. Idle wording
  is now resolved on read, and a regression test fails any field or computed-once property that
  goes through the lookup.

## Chinese regression

- Restarted with `zhs`: overlay reads `存储1` / `和 AI 一起爬塔` / `配置尚未验证` / `尚未启动双开。` /
  `队伍交流` / `隐藏` / `对话` / `游玩` / `设置` / `接入`, matching the pre-change wording.

## Model-facing state payload, English

Run continued on the isolated profile, read from `/state`:

- Deck lines: `Bash [2 Energy]: Deal 8 damage. Apply 2 Vulnerable.`,
  `Defend*4 [1 Energy]: Gain 5 Block.`, `Whirlwind [X Energy]: Deal 5 damage to ALL enemies X times.`
- Relics: `Burning Blood`, `Phial Holster`, `Eternal Feather`.
- Potions: `0: Vulnerable Potion: CombatOnly`.
- Reward lines: `Gold: 14 Gold`, `Potion: Explosive Ampoule`, `Card: Add a card to your deck.`
- Glossary keys and bodies are both English:
  `{"Strength":"Each point of Strength usually adds 1 damage per attack.","Vulnerable":"Vulnerable creatures take more attack damage.","Block":"Block absorbs incoming damage before HP."}`

## Static

- `dotnet run --project STS2AIAgent.Tests` - all green, including the new `Loc.*` group:
  Chinese identity, English lookup, unknown-key fallback, broken placeholder, language-code shapes,
  change notification, call-site coverage, shared-key agreement, English-is-English, no frozen text,
  startup order, glossary alignment.
- `dotnet build STS2AIAgent` Release: 0 warnings, 0 errors.
- `preflight-release.ps1`: OK; `uv run --locked python -m unittest discover -s tests`: 55 passed.

## Not covered

- The English wording is machine-translated and was not reviewed by a native speaker.
- Only Chinese and English were exercised. Other languages fall back to English by design.
- Combat, shop, event and card-selection screens were only checked through the state payload and the
  overlay, not by playing every screen in English.

