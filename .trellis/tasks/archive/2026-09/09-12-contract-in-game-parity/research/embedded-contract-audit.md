# 证据：内嵌契约的可满足性

## 链路（已证实）

`STS2AIAgent.csproj:32-33`（只嵌 `SKILL.md` + `references/screen-playbooks.md`）
→ `PlayPrompt.cs:43-47`（`ReadEmbedded` + `ExtractSharedContract`）
→ `PlayPrompt.BuildPlaySystem()`（`:57-71`；`:62` 列工具、`:65/67` 拼契约与 playbook）
→ `AgentLoop.cs:115-119`（每步重建系统提示）。原生 MCP 的 `instructions` 见 `NativeMcpServer.cs:374`。

## 唯一的硬性不可满足项

| 契约要求 | 行 | 游戏内 | 证据 |
|---|---|---|---|
| `Call health_check once at session start` | SKILL.md:52、53 | ❌ | `AgentTools.Play`（`AgentTools.cs:70-78`）无该工具；`health_check` 只在 `AgentTools.Mcp`（`:80-83`，`:82`）。调用后 `AgentLoop.cs:547` 返回 `Unknown tool` |

补充：`PlayPrompt.cs:62` 在同一段里列出了游戏内工具清单（不含 health_check），与要求自相矛盾。

## playbooks 的同类项

`screen-playbooks.md:93`：debug `fight THE_ARCHITECT_EVENT_ENCOUNTER`。游戏内无 console 工具、
`act` 无 `command` 位（`AgentTools.cs:7-26`）、`run_console_command` 不在 `available_actions`
（`GameStateService.cs:2528-2810`）⇒ `AgentLoop.cs:593-603` 判非法。
外部 MCP 侧可行（`ActionRequest.command` `GameActionService.cs:6623`；
`STS2_ENABLE_DEBUG_ACTIONS=1` 时注册）。

## 其余契约要求：逐条可满足（抽样已核）

guided 环、`wait_until_actionable`、三个数据工具、9 个 compact id 字段
（hand.card_id `GameStateService.cs:3601`；enemies[].enemy_id `3183`；run.deck[].card_ids `3734`；
relic_ids `3245`；selection/reward/shop/bundles/chest 各字段见 `3298/3324/3696/3343/3542`）、
`session.mode/phase` 四值（`165-204`）、`error.code/retryable/status_code`（`AgentErrorEnvelope.cs:21,39-46`）、
`game_over.phase=summary_animating`（`4966-4970`）、`unlock_type/items`（`3036-3043`）、
`character_select.embark`（`4470`）、`shop.open`（`3384`）、敌方 `intents[].damage/hits/total_damage`
与 `powers`（`3190-3199`、`3137/3189`）、`map.options[].i/local_vote/votes`（`3462-3481`）、
`capstone.options[].i`（`1168-1185`）——全部存在。

## routing 动作名核对

契约 99-118 行点名的动作名**全部**存在于 `BuildAvailableActionNames`（`2528-2810`）。
两处非硬性缺口：`MAIN_MENU` 标签同时来自 `NSubmenu`/`NLogoAnimation`/`NMainMenu`
（`6861-6863`）而 `continue_run`/`switch_profile` 只认 `NMainMenu`；routing 段缺
`TIMELINE`（`6855`）与 `CARDS_VIEW`（`6815-6818`）两行（playbook 5-17 兜住）。

## 文档层不一致（旁证）

`README.md:183` 与 `README.zh-CN.md:183` 都写"Built-in tools match in-game autoplay: health_check, ..."，
与 `AgentTools.cs:70-78` 不符。
