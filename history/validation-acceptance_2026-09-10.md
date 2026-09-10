# 实机验收记录（五个目标候选版本）

记录日期：2026-09-10。范围：本周期五个目标（依赖收口、主动发言与语气、docs/api.md 动作契约、陈旧文档归档、验证闸门）的 Release 候选，在隔离游戏副本上做离线闸门 + 真实游戏端到端验收，并收口主动发言的最后一项实机验证。

## 候选与环境

| 项 | 值 |
| --- | --- |
| Mod 版本 | 0.10.5 |
| 候选 DLL SHA256 | 4C2092EFC771BF435440B426989B5E07D8CB5689156CEFA7A06F971B8109D420 |
| 候选 PCK SHA256 | 73AA0233254B7B79B494E20606A82DA126A3DACED331C1D9B1323A032F90DD38 |
| 游戏版本 | v0.111.0 |
| 隔离副本 | build/validation-2026-09-08/game/SlayTheSpire2.exe（exe SHA256 8602C26BFFD2937E3841835FD8360EF8E974624A543E05977229FD3D062BE231） |
| 启动参数 | --windowed --force-steam off --clientId 2026091001（离线，不写 Steam 档案） |
| API | http://127.0.0.1:8080，debug actions 开启 |
| 验收期间 settings | build/validation-2026-09-10/settings.json（端点指向死端口 18099，全程未配真实模型） |
| 服务日志 | %APPDATA%/SlayTheSpire2/logs/godot.log（后续启动会覆盖，关键结论已摘录本文） |
| 原始证据目录 | build/validation-2026-09-10/（build 被 gitignore，套件日志在 suites/ 子目录） |

## 离线闸门

| 检查 | 命令 | 结果 |
| --- | --- | --- |
| 验证闸门 | python scripts/check_verification_gates.py | exit 0；api-doc 55 动作一致、doc-marks 7 条、lockfile 5 条 |
| 闸门自测 | powershell -File scripts/test-verification-gates.ps1 | exit 0；全部漂移用例 PASS |
| 发布预检 | powershell -File scripts/preflight-release.ps1 | exit 0 |
| C# 核心单测 | dotnet run --project STS2AIAgent.Tests | 210 PASS / 0 FAIL（本周期复验） |
| MCP 单测 | uv run pytest tests/ | 49 passed（本周期复验） |

本轮同时验证并提交了工作区里未提交的闸门增强（scripts/check_verification_gates.py、scripts/test-verification-gates.ps1 的版本约束求解：支持 >=、<、~=、^ 等，并对 uv.lock / package-lock.json / pyproject.toml 做真实比对）。

## 实机套件结果

端口 8080 的隔离实例，套件原始输出保存在 build/validation-2026-09-10/suites/。共执行 13 项：10 通过、3 失败、1 项未运行。

| 套件 | 结果 | 备注 |
| --- | --- | --- |
| mod-load | PASS | mod_version 0.10.5，status ready |
| state-summary | PASS | MAIN_MENU，动作可读 |
| state-invariants | PASS | 4 actions，0 failure / 0 warning |
| debug-console-gating --enable-debug-actions | PASS | help 返回完整命令表；工具注册门控正确 |
| mcp-tool-profile | PASS | guided 10 工具 |
| new-run-lifecycle | PASS | IRONCLAD → MAP → die → GAME_OVER → MAIN_MENU，11 秒 |
| bootstrap-active-run | PASS | 造局到 MAP |
| deferred-potion-flow | PASS | LIQUID_MEMORIES 选择流；use_potion 返回 pending，选牌后回到 COMBAT |
| target-index-contract | PASS | target_index_space / requires_target 契约正确 |
| enemy-intents-payload | PASS | BYRDONIS SWOOP_MOVE，伤害 17 单次命中 |
| combat-hand-confirm-flow | FAIL | 见发现 1 |
| assert-active-run-main-menu | FAIL | 见发现 2（open_timeline 不可用） |
| main-menu-active-run | FAIL | 见发现 2（同一断点） |
| multiplayer-lobby-flow | 未运行 | 见「未覆盖项」 |

套件之外，本轮还手动走通了这些动作链路（均为隔离实例）：abandon_run + confirm_modal、save_and_quit（战斗中与地图上都成功）、continue_run、choose_map_node → COMBAT、confirm_selection（选牌确认后回到 COMBAT）、以及时间线三连 open_timeline → confirm_timeline_overlay → close_main_menu_submenu。控制台命令 help、room、card、potion、die、win 均可用。

## 实机发现

### 1. combat-hand-confirm-flow 失败（基线内既有回归，非本周期引入）

现象：注入 PURITY 打出后进入 CARD_SELECTION，选中一张牌时接口返回 status=completed、stable=true，而界面仍在等待确认（selection.requires_confirmation=true、can_confirm=true、selected_count=1）。套件期望选牌后保持 pending，故断言失败。

定位：STS2AIAgent/Game/GameActionService.cs 的 WaitForCombatHandSelectionStepAsync 中，选中数变化时直接 return true。该函数引入提交 52c3467（与套件同一提交）此处是 return false；基础线 cdd820a（上次已认可构建）与当前 HEAD 都是 return true，变化发生在本周期起始点之前。对比：同类多选 use_potion 仍返回 pending（deferred-potion-flow 通过），两者语义不一致。

影响：调用方无法用 stable 判断「选择仍在等待确认」，只能靠再读一次 selection 字段。修复需重跑联机整局（dd1cfe6 起的自动游玩流程依赖该函数），因此本轮只记录、未改动代码。

### 2. 有进行中存档时主菜单不暴露 open_timeline（套件与游戏行为差异）

现象：主菜单存在 continue_run/abandon_run 时，动作集为 switch_profile、continue_run、abandon_run、invite_ai_teammate；无存档时为 switch_profile、open_character_select、open_timeline、invite_ai_teammate。assert-active-run-main-menu 与 main-menu-active-run 都断言有存档时 open_timeline 必须可用，故无法通过。

定位：CanOpenTimeline 要求时间线按钮 visible 且 enabled。同一个查找逻辑在没有存档时能正常返回（open_timeline 可用），说明不是节点查找失效，而是游戏 v0.111.0 在有进行中存档时就没有暴露时间线入口。

替代覆盖：无存档主菜单下已手动验证 open_timeline、confirm_timeline_overlay、close_main_menu_submenu 三个动作全部 completed。choose_timeline_epoch 在全新档案上本就不可用（时间线直接给确认浮层），属档案状态限制。

### 3. 全新档案的一次性教学弹窗会让套件首次运行失败（环境前置）

两种实测：首次进图时 NMapSelectFtue 让 run_console_command die 被拒（invalid_action：A run does not appear to be in progress）；首次获得药水时 NObtainPotionFtue 常驻，使 use_potion 在整段等待中不可用。两者都在该档案消费一次后不再出现，重跑即 PASS（new-run-lifecycle、deferred-potion-flow 均如此）。建议在验收流程前先做一次预热，或在等待动作可用前统一 resolve 一次 FTUE。

### 4. 进行中的局只在自然进入节点时落盘（流程知识，影响既有脚本）

实测：bootstrap 造局停在 MAP 时，无论强杀进程还是 save_and_quit 都不会写出 current_run.save，主菜单也没有 continue_run；先用 choose_map_node 自然进入一次节点后，立刻出现 current_run.save（41801 字节）与 progress 更新，此后 save_and_quit 即可得到带 continue_run 的主菜单。调试命令 room Monster 跳房间不会触发该存档钩子。

因此 scripts/test-full-regression.ps1 里「造局 → 强杀 → 重启期待 continue_run」的 Ensure-ActiveRunMainMenu 在本环境不成立，应改为「造局 → 自然选一次地图节点 → save_and_quit」。

### 5. 观察：主动发言的情境键不随游玩会话重置

AgentRuntime 的 _proactiveSituationKey 只在观察时写入，没有随自动游玩会话结束清空。因此在战斗中暂停再恢复自动游玩时不会再次触发 CombatStart；需要先离开战斗再进入。这是行为取舍而非故障，记录供后续判断。

### 6. 观察：停止原因分类不准

自动游玩因「模型未给出可执行动作」停止时，/health 的 stop_kind 报 run_end，与实际原因（非 run 边界）不符。在桩模型返回非法动作时稳定复现。

## 主动发言实机验证（目标二收口）

方法：本地零成本 OpenAI 兼容桩（build/validation-2026-09-10/stub-model-server.py，支持 SSE，逐请求记账），配置 proactiveChatEnabled=true、proactiveChatTone=friendly、supportsTools=false（让 JSON 兜底路径生效），不触碰真实模型端点与预算。

证据（build/validation-2026-09-10/proactive-chat-live-ledger.jsonl，同时段 /health 读数）：

- 自动游玩由桩驱动自行推进：collect_rewards_and_proceed → choose_map_node option_index=0 → 进入 COMBAT → 连续 end_turn，play_running=true 且未中断。
- 同一会话内出现一条主动发言请求：stream=true，message_count=8，tool_count=0（只读对话路径），user 消息正是 A fight just started. Say one short line to your teammate about how you want to handle it.
- 该系统提示尾部含 Voice: warm co-op partner. Encouraging, first person, and still concrete. 加共享规则，即 settings 中的 friendly 语气确实注入到请求。
- 该请求之后紧接着仍有正常 end_turn 请求，说明会话未因主动发言报错而中断。

结论：COMBAT 切换触发、提示词构造、语气注入、只读 chat 路径、回复消费，五项在真实游戏里全部成立，且零模型成本。

## 未覆盖项

- multiplayer-lobby-flow：Python 入口依赖 scripts/start-game-session.sh（Windows 上不可用），PowerShell 版 scripts/test-multiplayer-lobby-flow.ps1 没有隔离副本参数、默认指向正式 Steam 安装。为保护玩家真实存档，本轮未运行；建议给该脚本加 --exe-path/--game-root 参数后再补。
- choose_timeline_epoch：全新档案无可选纪元，属档案状态限制。
- 真实模型连通与成本路径：本轮用桩验证，未调用真实上游，也未消耗预算。

## 真实存档与部署状态

- 保护快照（steam/76561198420578597、default/1、default/1001、%APPDATA%/STS2AIAgent，共 197 个文件）复验：0 changed、0 missing。真实存档、模型配置均未被本次验收修改。
- 隔离实例只写 default/2026091001 与游戏日志。
- 正式安装目录 mods/STS2AIAgent.dll 当前 SHA256 为 4C2092EF...，与本次候选一致（上一轮 build-mod.ps1 部署所致）；被覆盖的旧 DLL 备份在 build/validation-2026-09-10/prior-candidate-STS2AIAgent.dll，需要回退可直接替换。

## 结论与建议

本轮五个目标的成果没有引入回归：变化的文件（ProactiveChatPolicy、SettingsClone、docs/api.md、脚本与闸门）都不在上述三处失败路径上；三处失败都可以在基线构建或游戏行为上复现。

建议后续动作，按优先级：

1. 修正 WaitForCombatHandSelectionStepAsync 的 return true（改回 pending 语义），随后重跑联机整局验收确认 dd1cfe6 的流程不受影响。
2. 对齐 assert-active-run-main-menu 与 main-menu-active-run 对 open_timeline 的断言，或在无存档主菜单下单独覆盖时间线流程。
3. 把 test-full-regression.ps1 的 active-run 引导改成「自然节点 + save_and_quit」，并加入 FTUE 预热步骤。
4. 给多人大厅 PowerShell 脚本补隔离副本参数，恢复该套件的可运行性。
