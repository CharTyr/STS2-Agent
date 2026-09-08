# Phase 4C Shop Status

> 历史记录，2026-09-09 归档。以下实现说明仅属于 2026-03-10；当前接口见 [API 文档](../docs/api.md)，当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

Updated: `2026-03-10`

## Implemented

- `shop` payload is now populated in `GET /state`
- `open_shop_inventory`
- `close_shop_inventory`
- `buy_card`
- `buy_relic`
- `buy_potion`
- `remove_card_at_shop`
- MCP client/server wrappers for all shop actions

## Shop State Shape

`screen = "SHOP"` can represent two layers:

- outer room: `NMerchantRoom`
- inner inventory: `NMerchantInventory`

The payload exposes:

- `shop.is_open`
- `shop.can_open`
- `shop.can_close`
- `shop.cards[]`
- `shop.relics[]`
- `shop.potions[]`
- `shop.card_removal`

### `shop.cards[]`

- `index`
- `category` (`character` or `colorless`)
- `card_id`
- `name`
- `upgraded`
- `card_type`
- `rarity`
- `energy_cost`
- `star_cost`
- `price`
- `on_sale`
- `is_stocked`
- `enough_gold`

### `shop.relics[]`

- `index`
- `relic_id`
- `name`
- `rarity`
- `price`
- `is_stocked`
- `enough_gold`

### `shop.potions[]`

- `index`
- `potion_id`
- `name`
- `rarity`
- `usage`
- `price`
- `is_stocked`
- `enough_gold`

### `shop.card_removal`

- `price`
- `available`
- `used`
- `enough_gold`

## Action Rules

### `open_shop_inventory`

- no args
- valid only when `shop.is_open = false`

### `close_shop_inventory`

- no args
- valid only when `shop.is_open = true`

### `buy_card`

- requires `option_index`
- index maps to `shop.cards[]`
- valid only when inventory is open and at least one stocked card is affordable

### `buy_relic`

- requires `option_index`
- index maps to `shop.relics[]`

### `buy_potion`

- requires `option_index`
- index maps to `shop.potions[]`

### `remove_card_at_shop`

- no args
- valid only when inventory is open and card removal is affordable
- may transition into `CARD_SELECTION`
- follow-up continues with existing `select_deck_card`

## Notes

- Shop actions now use the game's merchant entry objects directly instead of simulating generic UI clicks.
- `available_actions` is intentionally strict: buy/remove actions only appear when the inventory is open and the player can currently afford the entry.
- `remove_card_at_shop` is fire-and-forget so the HTTP call does not block on the card selection flow.
