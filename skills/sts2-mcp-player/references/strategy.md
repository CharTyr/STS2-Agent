# Strategy Rules

`screen-playbooks.md` says **how to drive a screen**. This file says **what to choose** on the screens where the choice is not mechanical: route, rest site, shop, potion timing, combat priority, and co-op division of labour.

Every rule names the payload field it reads, so a rule can be checked against live state instead of trusted. Field names are the compact ones MCP `get_game_state` returns; the rename table in `docs/api.md` maps them back to raw `/state` names.

**This file is not carried whole in the in-game prompt.** The mod embeds it and injects only the section the current screen needs — roughly 260 to 520 tokens, and nothing at all on a screen with no strategic choice — because carrying all ~2,200 tokens on every play step would charge a combat decision for the shop advice. An external agent should read the file directly and keep the whole picture; the in-game loop reads it one screen at a time.

Two consequences of that split are worth knowing:

- A heading that no screen maps to reaches the in-game model on no screen at all. The mapping lives in `STS2AIAgent/Agent/PlaybookSections.cs`, and a test fails if a section is neither mapped nor declared as run-level or deliberately not injected — so a new section cannot be added here and silently never ship.
- The co-op section is one of those deliberate omissions in-game: the in-game loop drives one local player and has no channel to coordinate with the other instance. It is written for an external client that does.


## Route: which node to enter

Read `map.nodes[]` for the whole graph and `map.available_nodes[]` for what may be picked now. `available_nodes[].index` is the `choose_map_node` argument; `nodes[]` is for planning ahead.

1. **Health is the budget.** Compare `run.current_hp` to `run.max_hp` before anything else. Above roughly 65%, `Elite` nodes are the best value on the map: they pay a relic and more gold than a normal fight. Below roughly 45%, take `Rest` over `Elite` and prefer the path with fewer forced fights even if it costs a chest.
2. **Do not enter an `Elite` you cannot read.** If an act boss is known and the deck still has no answer to it, an `Elite` that does not fix that is a coin flip. Take `Shop` or `Event` instead when both are on offer.
3. **`Rest` before the boss is worth more than `Rest` early.** A rest node in the last row before `map.boss_node` can be spent on the boss; one in row 2 usually cannot. When both are reachable, take the later one.
4. **`Shop` is worth a detour when `run.gold` is above roughly 250**, because that is where card removal (`shop.card_removal.price`) becomes affordable alongside a purchase. Below that, a shop you walk past costs nothing.
5. **Prefer the node whose children keep options open.** If two nodes are equally good, pick the one with more `children[]`; a row that funnels into a single node removes every later choice.
6. **In co-op, `choose_map_node` needs everyone.** If `map.local_vote` is not null you have already voted: call `wait_until_actionable` instead of voting again. If another player voted (`map.player_votes[].coord` non-null) and you have not, follow their node — a split vote stalls the run, and the stall costs more than the worse node.

## Rest site: heal or upgrade

Read `rest.options[]`: `option_id` says what each entry does and `is_enabled` (compact `enabled`) says whether it can be taken now — `SMITH` is disabled when nothing can be upgraded.

1. **Heal when `run.current_hp` is below about 50% of `run.max_hp`**, or whenever the next node is an `Elite` or the boss. The exception is a smith that upgrades the one card the deck is built around.
2. **Smith when healthy and the upgrade is real.** Upgrading a starter card is usually worth less than the health; upgrading a card played every combat is worth more.
3. **`MEND`, `LIFT`, `COOK`, `DIG`, `HATCH` and `CLONE` are relic- or run-specific.** Take them when enabled and the heal is not urgent; their `description` states the effect, and an unused rest site is a wasted one.
4. **A rest option that needs a target** (`requires_target` true) picks a player, not a card: use `valid_target_indices` and `valid_target_player_ids` to choose the teammate who needs it — the lowest `current_hp` relative to `max_hp` in `run.players[]` — and never pass an index from another array.
5. **Never leave a rest site unspent.** There is no benefit to `proceed` while an enabled option remains.

## Shop: what to buy

Read `shop.cards[]`, `shop.relics[]`, `shop.potions[]` and `shop.card_removal`. **`enough_gold` is the field that says whether a purchase is possible now**; `is_stocked` alone reports 409 when the gold is short.

1. **Card removal first when the deck carries a curse or a dead starter card.** Removing a card improves every future draw; `shop.card_removal` is once per shop and its `used` flag says whether it is already spent.
2. **Relics over cards when both are affordable and the deck is already functional.** A relic is unconditional; a card has to be drawn.
3. **Buy a potion only into an empty slot** (`run.potions[].occupied` false). With every slot full, `buy_potion` will not stay available, and buying to discard wastes gold.
4. **Keep a reserve.** Spending `run.gold` to zero means the next shop cannot sell the card removal wanted after a bad act. Leave roughly 75 gold behind unless the purchase wins the act outright.
5. **`on_sale` is a discount, not a reason.** A discounted card you would not otherwise take is still a card you did not want.

## Potions: when to drink

Read `run.potions[]` for `usage`, `target_type`, `can_use` and `is_queued`. Targeting is covered in `screen-playbooks.md`; these are the timing rules.

1. **A potion unspent at the end of the run was worth nothing.** Spend them on the boss, on an `Elite` entered at low health, or on a fight being lost — but spend them.
2. **`CombatOnly` potions are for the turn they matter.** Check `combat.enemies[].intents[]` first: a block or damage potion played before the enemy's heavy turn (`total_damage` high) is worth far more than the same potion on a quiet turn.
3. **Do not drink a healing potion above roughly 80% `run.current_hp`** — the slot is worth more than the overheal. `AnyTime` potions can wait for the fight that needs them.
4. **`is_queued` means the potion is already resolving.** Do not use it again; re-read state.
5. **In co-op, a `players`-targeted potion belongs on the teammate at the lowest `current_hp`** when the payload asks for a target.

## Combat: what to prioritise

Stated as a check order rather than a heuristic, because this is the decision the loop makes most often.

1. **Read `combat.action_readiness` first.** It is the same field the mod's own executor gates on, so it decides "act or wait" before any card choice does.
2. **If `combat.end_turn_will_kill_player` is true, do not end the turn.** Play a block or remove the threat instead.
3. **Then read `combat.lethal_risks[]`.** If a lethal line exists, take it — a fight that ends this turn costs no health.
4. **Otherwise prefer the play that minimises `combat.enemies[].intents[].total_damage` reaching you**, weighing block against killing the attacker. Killing the enemy that deals the most damage is usually better than blocking it, because it also stops the turn after this one.
5. **Spend energy down before ending the turn** unless holding it has a purpose (an X-cost card in hand, or a card that scales with energy). Unspent energy is a wasted turn.
6. **Do not target an ally with an unsupported card.** If the payload marks it unplayable rather than guessing a target, that is the answer.

## Co-op: dividing the work

Both instances see the same run, so the failure mode is both of them doing the same job. Read `combat.players[]` (`is_local`, `current_hp`, `max_hp`, `is_alive`) and `map.player_votes[]`.

1. **The host decides the route, the teammate follows.** A split vote stalls the map, so the teammate votes for the host's node unless the host has not voted and the teammate's node is clearly better (it saves a rest node or reaches a shop the host cannot).
2. **Focus fire on one enemy at a time.** Both players attacking different enemies leaves both alive; overkill on one is cheaper than an extra enemy turn. Attack the enemy the other player is already attacking unless it is already dead.
3. **Whoever is lower on health takes the defensive line.** If a teammate is below roughly 40% `current_hp`, the healthier player should spend this turn on damage or on killing the threat rather than on block for themselves.
4. **Potions and healing target the teammate at the lowest `current_hp`**, which is not always the local player — check `combat.players[]` before drinking.
5. **Do not spend the same resource twice.** If the teammate has already used a board clear or a big potion this fight, assume the threat is handled and play for the next turn instead.

## Where these rules come from

They are heuristics, not the game's own logic: the payload exposes the numbers, and these are the thresholds this project's agents use to act on them. Two consequences:

- A rule that names a threshold (65% health, 250 gold, 75 gold) is a starting point to tune, not a constant of the game. Record a better threshold with the run that showed it.
- If a rule disagrees with live state, the state wins. The rule exists to save a decision, never to override the payload.
