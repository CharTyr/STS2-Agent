# PR #56 native profile switch live test

Tested on 2026-09-05 against Slay the Spire 2 v0.111.0 with the PR head
`a14d41fc2d637d424ff32d9bc8d7f85f9684ebb6` deployed as STS2AIAgent v0.9.2.
The deployed DLL and the local Release build both had SHA-256
`674B7451B0BC37BAA740CD27103405A6B490566B4130A0C015A12216339BE124`.

The autonomous brain was paused before it sent any action. All requests below
were sent directly to the local game API on the game thread.

| Test | Result |
| --- | --- |
| Initial state | `MAIN_MENU`, `native_profile_id=1`, `switch_profile` advertised |
| Profile 1 -> 2 | 643 ms; `completed`, `stable=true`; six 250 ms post-response samples all remained on Profile 2 at `MAIN_MENU` |
| Profile 2 -> 1 | 283 ms; `completed`, `stable=true`; six 250 ms post-response samples all remained on Profile 1 at `MAIN_MENU` |
| Existing run after restore | `continue_run` remained available on Profile 1 |
| Idempotent Profile 1 -> 1 | 96 ms; `completed`, `stable=true`, no replacement menu required |

Screenshots are the original full-screen captures:

- `2026-09-05-profile-1-before.png`: initial Profile 1 main menu.
- `2026-09-05-profile-2-after-switch.png`: Profile 2 after the completed response.
- `2026-09-05-profile-1-restored.png`: Profile 1 restored with Continue available.
