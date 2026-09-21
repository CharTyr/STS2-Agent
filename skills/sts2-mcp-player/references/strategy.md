# Strategy Rules

`screen-playbooks.md` says **how to drive a screen**. This file says **what to choose** on the screens where the choice is not mechanical: route, rest site, shop, potion timing, combat priority, and co-op division of labour.

Every rule names the payload field it reads, so a rule can be checked against live state instead of trusted. The names below are the compact `agent_view` keys `get_game_state` returns and the in-game loop is handed, not the raw `/state` names. The rename table in `docs/api.md` lists every one; a compact read of a raw name comes back missing. A few facts exist only on the raw payload (`usage`, `is_queued`, `occupied` on a potion). Those rules say so instead of pointing at a compact key that is not there.

**This file is not carried whole in the in-game prompt.** The mod embeds it and injects only the section the current screen needs — roughly 260 to 520 tokens, and nothing at all on a screen with no strategic choice — because carrying all ~2,200 tokens on every play step would charge a combat decision for the shop advice. An external agent should read the file directly and keep the whole picture; the in-game loop reads it one screen at a time. The same sections are reachable over MCP through `get_scene_guidance`, which answers "what does this screen need" in one call.

Two consequences of that split are worth knowing:

- A heading that no screen maps to reaches the in-game model on no screen at all. The mapping lives in `STS2AIAgent/Agent/PlaybookSections.cs`, and a test fails if a section is neither mapped nor declared as run-level or deliberately not injected — so a new section cannot be added here and silently never ship.
- The co-op section is one of those deliberate omissions in-game: the in-game loop drives one local player and has no channel to coordinate with the other instance. It is written for an external client that does.


## Route: which node to enter

Read `map.nodes[]` for the whole graph and `map.options[]` for what may be picked now. `options[].i` is the `choose_map_node` argument; `nodes[]` is for planning ahead. Raw `/state` calls that list `available_nodes` and that index `index`.

1. **Health is the budget.** Compare the two numbers in `run.hp` (`"current/max"`) before anything else. Above roughly 65%, `Elite` nodes are the best value on the map: they pay a relic and more gold than a normal fight. Below roughly 45%, take `Rest` over `Elite` and prefer the path with fewer forced fights even if it costs a chest.
2. **Do not enter an `Elite` you cannot read.** If an act boss is known and the deck still has no answer to it, an `Elite` that does not fix that is a coin flip. Take `Shop` or `Event` instead when both are on offer.
3. **`Rest` before the boss is worth more than `Rest` early.** A rest node in the last row before `map.boss_node` can be spent on the boss; one in row 2 usually cannot. When both are reachable, take the later one.
4. **`Shop` is worth a detour when `run.gold` is above roughly 250**, because that is where card removal (`shop.remove.price`) becomes affordable alongside a purchase. Below that, a shop you walk past costs nothing.
5. **Prefer the node whose children keep options open.** If two nodes are equally good, pick the one with more `children[]`; a row that funnels into a single node removes every later choice.
6. **In co-op, `choose_map_node` needs everyone.** If `map.local_vote` is not null you have already voted: call `wait_until_actionable` instead of voting again. If another player voted (`map.votes[].coord` non-null) and you have not, follow their node — a split vote stalls the run, and the stall costs more than the worse node.

## Rest site: heal or upgrade

Read `rest.options[]`: `option_id` says what each entry does and `enabled` says whether it can be taken now — `SMITH` is disabled when nothing can be upgraded. Raw `/state` spells that flag with an `is_` prefix.

1. **Heal when the current number in `run.hp` is below about 50% of the max**, or whenever the next node is an `Elite` or the boss. The exception is a smith that upgrades the one card the deck is built around.
2. **Smith when healthy and the upgrade is real.** Upgrading a starter card is usually worth less than the health; upgrading a card played every combat is worth more.
3. **`MEND`, `LIFT`, `COOK`, `DIG`, `HATCH` and `CLONE` are relic- or run-specific.** Take them when enabled and the heal is not urgent; their `description` states the effect, and an unused rest site is a wasted one.
4. **A rest option that needs a target** (`requires_target` true) picks a player, not a card: use that option's `valid_target_indices` (also written into its `line`) to choose the teammate who needs it — the lowest `current_hp` relative to `max_hp` in `run.players[]` — and never pass an index from another array. `valid_target_player_ids` is not on the compact option.
5. **Never leave a rest site unspent.** There is no benefit to `proceed` while an enabled option remains.

## Shop: what to buy

Read `shop.cards[]`, `shop.relics[]`, `shop.potions[]` and `shop.remove`. **`affordable` is the field that says whether a purchase is possible now**; `stocked` alone reports 409 when the gold is short. Raw `/state` calls the removal entry `card_removal` and spells the two flags with `enough_` / `is_` prefixes.

1. **Card removal first when the deck carries a curse or a dead starter card.** Removing a card improves every future draw; `shop.remove` is once per shop and its `used` flag says whether it is already spent.
2. **Relics over cards when both are affordable and the deck is already functional.** A relic is unconditional; a card has to be drawn.
3. **Buy a potion only into an empty slot.** Compact `run.potions[].line` reads `N: 空` when the slot is empty. With every slot full, `buy_potion` will not stay available, and buying to discard wastes gold.
4. **Keep a reserve.** Spending `run.gold` to zero means the next shop cannot sell the card removal wanted after a bad act. Leave roughly 75 gold behind unless the purchase wins the act outright.
5. **`on_sale` is a discount, not a reason.** A discounted card you would not otherwise take is still a card you did not want.

## Event options: how to choose

Read `event.options[]` and skip every entry with `locked` true. `event.options[].i` is the `choose_event_option` argument; `proceed` marks the leave/continue entry and `kill` marks an option the game itself considers fatal. The event itself is `event.id`. Raw `/state` spells the same facts with `is_` / `will_` prefixes and calls the id `event_id`.

1. **Never take a `kill` option** unless the run is being ended on purpose. It outranks every other consideration on this screen.
2. **Read the cost before the reward.** The generated event index grades each option — `lethal-possible`, `harmful`, `costly`, `none-detected`, `locked`, `unknown` — and names what the handler spends (HP, Max HP, gold, a card, a relic). An option that costs Max HP is a permanent price; one that costs gold is usually not.
3. **A card or relic the deck does not want is not a reward.** When an option "adds a card to your deck", read what it adds before taking it: a curse is a cost dressed as a reward.
4. **`none-detected` is not a promise.** It means the handler body showed no harmful command, not that the option is safe. When a risk grade is `?` or `unknown`, prefer an option whose cost is written down.
5. **Leave when the remaining options all cost more than they give.** `proceed` / the finished-event entry is a legitimate answer; an unspent event is not a loss.
6. **Re-read state after every branch.** Events mutate in place, and the options on the next page are not the options on this one.

### Where the risk grades come from

The grades are derived offline from the decompiled event sources into `docs/game-knowledge/events.md`, per option the event builds. An external agent reading the repository can look up the current `event.id` there and see the specific options this general advice is about; the in-game loop gets this section only, because the index is not shipped inside the mod.

## Potions: when to drink

Read `run.potions[]` for `usable`, `target` and `targets`. Usage is folded into `line`; a queued potion is confirmed by re-reading, not by a key on this object. Targeting is in `screen-playbooks.md`.

1. **A potion unspent at the end of the run was worth nothing.** Spend them on the boss, on an `Elite` entered at low health, or on a fight being lost — but spend them.
2. **A `CombatOnly` line is for the turn it matters.** Check `combat.enemies[].intents[]` first: drink a block or damage potion before the heavy hit, not on a quiet turn.
3. **Do not drink a healing potion above roughly 80% of `run.hp`** — the slot is worth more than the overheal. An `AnyTime` line can wait for the fight that needs it.
4. **A potion already resolving must not be used again.** Compact has no queue flag; if the last drink is still settling, re-read instead of using the same slot.
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

Both instances see the same run, so the failure mode is both of them doing the same job. Read `combat.players[]` (`is_local`, `current_hp`, `max_hp`, `is_alive`) and `map.votes[]`.

1. **The host decides the route, the teammate follows.** A split vote stalls the map, so the teammate votes for the host's node unless the host has not voted and the teammate's node is clearly better (it saves a rest node or reaches a shop the host cannot).
2. **Focus fire on one enemy at a time.** Both players attacking different enemies leaves both alive; overkill on one is cheaper than an extra enemy turn. Attack the enemy the other player is already attacking unless it is already dead.
3. **Whoever is lower on health takes the defensive line.** If a teammate is below roughly 40% `current_hp`, the healthier player should spend this turn on damage or on killing the threat rather than on block for themselves.
4. **Potions and healing target the teammate at the lowest `current_hp`**, which is not always the local player — check `combat.players[]` before drinking.
5. **Do not spend the same resource twice.** If the teammate has already used a board clear or a big potion this fight, assume the threat is handled and play for the next turn instead.

## Where these rules come from

They are heuristics, not the game's own logic: the payload exposes the numbers, and these are the thresholds this project's agents use to act on them. Two consequences:

- A rule that names a threshold (65% health, 250 gold, 75 gold) is a starting point to tune, not a constant of the game. Record a better threshold with the run that showed it.
- If a rule disagrees with live state, the state wins. The rule exists to save a decision, never to override the payload.
