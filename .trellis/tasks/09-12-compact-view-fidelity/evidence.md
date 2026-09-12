# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Game/GameStateService.cs` | 仅 compact 构造方法内增量加字段 + `FormatPowerLine` helper + `AgentCardDescriptor` 新字段 |
| `STS2AIAgent.Tests/CompactViewFidelityTests.cs`（新） | 4 条源码契约 + raw 同名对照断言 |
| `docs/api.md` | 新增 `### compact agent_view 的增补字段` 段（动作契约块未动） |
| `skills/sts2-mcp-player/SKILL.md` | Quick Start 第 3 条、overlay 规则、`COMBAT` 条、`UNLOCK` 条（两个 marker 仍在第 48 / 141 行） |

### 新增字段（compact 键 → raw 同源）

`unlock`（顶层）、`combat.player.powers`、`combat.players[]`、`combat.enemies[].powers`、
`combat.enemies[].intents[]`（`i/intent_type/label/damage/hits/total_damage/status_card_count`）、
`run.relic_ids`、`run.players[]`、`chest.relics[].relic_id`、`modal.underlying_screen`、
手牌 / 选择 / 奖励 / 卡包 / 商店的 `card_id`、牌组与牌堆合并组的 `card_ids`、helper `FormatPowerLine`。

两处刻意的向后兼容取舍：`run.relics` 仍是名字数组，新增同序等长的 `relic_ids`；
合并组暴露去重升序的 `card_ids` 而非单个 `card_id`（`line` 里已有 `*N` 计数）。

## 验收

| 标准 | 证据 |
|---|---|
| 字段进入 compact | `PASS CompactViewFidelity.Powers / IntentNumbers / CardAndRelicIds / OverlayAndParty` |
| 每个字段在 raw 有同源 | 测试对每个新增键断言 raw 侧同名出现（防止发明字段） |
| 体积受控 | 只加短行与数值，未搬运 raw 的整段结构（`valid_target_indices` 等 raw 专用字段未进入 compact） |
| 文档同步 | `docs/api.md` 新增段；SKILL.md 四处更新且 marker 完好 |
| 可否证 | 逐条删 4 个代表性字段（enemy `powers`、`intent.total_damage`、choice `card_id`、`modal.underlying_screen`）→ 对应测试全红 → 还原全绿 |

## 门禁

C# 335 PASS / 0 FAIL；`dotnet build` 0 警告 0 错误；MCP 167 OK；
`check_verification_gates.py`（含 api-facts）`check_release_package.py` preflight 全绿。

## 只能实机验证

真实战斗里 powers / intents 的数值是否正确与是否非空、多段攻击与增益类意图的退化形态、
多人局 `players[]` 与 `target_index` 的索引空间是否对齐、`run.relic_ids` 与 `run.relics` 是否严格同序等长、
合并组 `card_ids` 是否覆盖同名牌不同 id 的边界、`modal.underlying_screen` 在各覆盖层下的取值。
