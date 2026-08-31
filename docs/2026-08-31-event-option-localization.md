# Event option localization variable injection

## Symptom

Live game logs showed `GameStateService.BuildEventPayload` failing while reading event option descriptions. `LocManager.SmartFormat` reported missing event-specific variables, including:

- `SUNKEN_STATUE`: `Relic`, `Gold`, `HpLoss`
- `SUNKEN_TREASURY`: `SmallChestGold`, `LargeChestGold`
- `ABYSSAL_BATHS`: `MaxHp`, `Damage`, `Heal`
- `BRAIN_LEECH`: `FromCardChoiceCount`, `CardChoiceCount`, `RipHpLoss`

`SafeReadString` prevented the state endpoint from failing, but it replaced each affected description with an empty string and deprived the agent of the option details.

## Root cause and fix

The game creates each `EventOption` with character details and the multiplayer flag. Event-specific values live separately in `EventModel.DynamicVars`.

The native option UI, `NEventOptionButton._Ready()`, calls `Event.DynamicVars.AddTo(Option.Title)` and `Event.DynamicVars.AddTo(Option.Description)` before `GetFormattedText()`. The agent previously called `GetFormattedText()` directly and skipped that native injection step.

`BuildEventPayload` now follows the same generic ordering for every event option title and description. No event IDs or localization variable names are hard-coded in production code.

## Regression coverage

`EventOptionLocalizationTests` exercises the four variable shapes observed in the live logs and verifies that event variables are installed before formatting. It also verifies the null localization fallback. The production agent build remains the compile-time check that `EventModel.DynamicVars.AddTo(LocString)` is wired into both option fields.

This change intentionally does not deploy or launch the game; live skin verification was running independently.
