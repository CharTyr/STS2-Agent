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
| MCP 单测 | uv run --with pytest python -m pytest tests/ -q | 49 passed, 61 subtests passed（本周期复验） |

本轮同时验证并提交了工作区里未提交的闸门增强（scripts/check_verification_gates.py、scripts/test-verification-gates.ps1 的版本约束求解：支持 >=、<、~=、^ 等，并对 uv.lock / package-lock.json / pyproject.toml 做真实比对）。

补充发现：AGENTS.md 与 mcp_server/README 记录的测试命令 uv run pytest tests/ -v 在干净检出下不可执行——pytest 并未声明在 mcp_server/pyproject.toml 里，uv run pytest 会直接报 Failed to spawn: pytest / program not found（uv run python -m pytest 亦为 No module named pytest）。本次改用 uv run --with pytest python -m pytest tests/ -q 得到 49 passed。这是目标五「可复现验证」的一处缺口。

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
5. 在 mcp_server/pyproject.toml 声明 pytest（依赖组或可选依赖），或把文档命令改成 uv run --with pytest，让干净检出按文档就能跑测试。

## 后续修复与复验（2026-09-11）

上面结论里排在前面的可修项已经落地，逐条列出改法与证据。

| # | 修复 | 证据 |
| --- | --- | --- |
| 1 | 停止原因分类：新增纯策略类 STS2AIAgent/Agent/StopKindPolicy.cs，run_end 只匹配 CurrentRunBoundary 的两条真实文案 | 3 个新单测（StopKind.RetryIsNotRunEnd / BoundaryIsRunEnd / OtherKindsSurvive），C# 由 210 升至 213 PASS / 0 FAIL；实机复现：桩模型连续三次给出非法动作后 /health 的 stop_kind 由 run_end 变为 failed |
| 2 | 验证闸门：uv.lock / package-lock.json 的冻结版本按 >=、<、~=、^ 等约束真实求解 | 闸门 exit 0、自测 exit 0、发布预检 exit 0 |
| 3 | 文档命令：把 MCP 单测命令从 uv run pytest 改成 CI 实际使用的 uv run --locked python -m unittest discover -s tests -v | CI 同一条命令本地 49 passed；pytest 未声明也不被支持，故不加依赖 |
| 4 | test-full-regression.ps1：造局引导改为「自然推进一个地图节点 + save_and_quit」，新增 Close-MainMenuOverlay 处理首死后自动弹出的时间线浮层 | 抽出 Ensure-ActiveRunMainMenu 对活实例实跑两条分支均通过（弹窗+未落盘的局 7.1 秒；空主菜单新建 10.2 秒），均达到带 continue_run 的主菜单 |
| 5 | 一次性教学弹窗：套件等待动作时可自动消解 FTUE，控制台命令支持有界重试，新增 settle_game_over / settle_main_menu | 全新档案（clientId 2026091007）上 new-run-lifecycle、bootstrap-active-run、deferred-potion-flow、target-index-contract、enemy-intents-payload 五个套件全部 exit 0 |
| 6 | test-multiplayer-lobby-flow.ps1：新增 -ExePath / -HostExtraArguments / -ClientExtraArguments，可指向隔离副本且两个实例各自 clientId | 用隔离副本实跑：两个实例在 8080/8081 起来、创建大厅、进入同一 run（run_id 一致），推进到第 2 层并走完奖励流程 |

实机复验中确认的一个关键机制（值得写进套件设计）：全新档案上**刚进图就下 die，游戏会接受命令但不会打开结算界面**，留下一个停在 MAP 上的死局；等地图可交互并稳定约 3 秒后再 die，结算立刻正常出现。已固化为 RUN_SETTLE_SECONDS 与相应的稳定等待。

第 6 项仍有一处未覆盖：多人大厅套件在「双方同到休息点」阶段没有跑完。MP 里两人共享地图位置，对客户端单独下发 room RestSite 后客户端回到 MAP（host 停在 REST），套件随即等待客户端进入 REST。这与本次新增的启动参数无关，属于套件自身的 MP 推进逻辑，建议后续单独处理。

## select_deck_card 语义定案（2026-09-11）

「实机发现 1」里那条战斗多选返回 completed 的问题已按「代码改回 pending」定案并验证。

改动：STS2AIAgent/Game/GameActionService.cs 的 WaitForCombatHandSelectionStepAsync 在「选中数已变化但界面仍需确认」时返回 false（pending），与 use_potion 及 skill 文档描述的语义对齐；docs/api.md 的 select_deck_card 段落按 selection.kind 拆成牌库单选（自动确认，completed）与战斗手牌多选（逐步 pending，需 confirm_selection）两种说明。

回归定位：该函数自引入（52c3467）到 PR #56 合并（9295f35）一直是 return false，93e5ab5（#68）把它改成 return true；该提交说明里唯一与选择有关的一句是「stop confirming deck picks at min_select」，指的是牌库路径，战斗手牌这次翻转属于连带改动。

实机证据（隔离副本，候选 DLL SHA256 43100E7621E1F962D2A1F7B1719339E2EEF8607735AF39B16B9AA232A6FF1A39）：

| 验证 | 结果 |
| --- | --- |
| combat-hand-confirm-flow | exit 0；post_select_status=pending（修复前为 completed，正是最初的失败断言），confirm_status=completed，final_screen=COMBAT |
| state-invariants | exit 0，0 failure / 0 warning |
| deferred-potion-flow | exit 0（牌库单选自动确认路径未受影响） |
| target-index-contract / enemy-intents-payload | exit 0 |
| C# 核心单测 | 213 PASS / 0 FAIL |
| 自动游玩冒烟（桩模型，零成本） | 桩先打出 PURITY（play_card card_index 5），随后连续三次调用 select_deck_card；日志中 "Timed out waiting for a stable state" 出现次数为 0，会话在每次 pending 之后继续推进 |

冒烟测试用的桩（build/validation-2026-09-10/stub-model-server.py）新增了按紧凑状态选牌的能力：紧凑手牌数组只有 i/line/playable/targets 而没有 card_id，且需要目标的牌必须带 target_index，否则会被 agent 自己的索引校验拒绝。这两点对后续写自动游玩验证脚本同样适用。

## open_timeline 断言定案（2026-09-11）

「实机发现 2」里那条「有存档时主菜单不暴露 open_timeline」已按「套件口径不对」定案并修复。

根因（游戏源码，非 mod 缺陷）：extraction/decompiled 下的 NMainMenu.UpdateTimelineButtonBehavior 要求 !SaveManager.Instance.HasRunSave 才启用时间线按钮；有存档时落入 else 分支被 Disable，第二个分支还会在「已发现纪元 > 0」时显式 Disable。mod 的 CanOpenTimeline 要求按钮 IsEnabled，因此有存档时不暴露 open_timeline 是正确行为。同文件还表明该状态受档案进度影响（未发现任何纪元时会 Enable），所以断言「必须有」和「必须没有」都不成立。

另有一层档案依赖：choose_timeline_epoch 只在「已揭示但未解锁的纪元」存在时才暴露。实测档案 2026091007 打开时间线后只给 close_main_menu_submenu 与 confirm_timeline_overlay。

改动：

| 套件 | 改动 |
| --- | --- |
| assert-active-run-main-menu | 去掉 open_timeline 断言，改为在结果里如实报告 open_timeline_available |
| main-menu-active-run | 去掉「有存档时应有 open_timeline」的前置断言与后续时间线阶段；改为断言撤回放弃对话框后仍保留 continue_run，并继续验证 continue_run 能回到对局 |
| new-run-lifecycle | 在结尾的无存档主菜单上补做时间线覆盖：open_timeline → （档案支持时）choose_timeline_epoch → confirm_timeline_overlay → close_main_menu_submenu；档案不支持纪元选择时如实记录而不是失败。结尾等待改用 settle_main_menu，以吸收游戏在死亡后自动弹出的时间线浮层 |

实机验证（隔离副本，同一实例连续执行）：

| 套件 | 结果 |
| --- | --- |
| assert-active-run-main-menu | exit 0；open_timeline_available=false（有存档时游戏正确地未暴露） |
| main-menu-active-run | exit 0；menu_after_dismiss_actions 保留 continue_run，continue_run_destination=COMBAT |
| new-run-lifecycle | exit 0；timeline={opened: true, screen: TIMELINE, epochs_selectable: false, overlay_confirmed: true} |
| 其余 8 个套件（mod-load、state-summary、state-invariants、bootstrap-active-run、combat-hand-confirm-flow、deferred-potion-flow、target-index-contract、enemy-intents-payload） | 全部 exit 0 |
| 离线 | 闸门 exit 0、自测 exit 0、C# 213 PASS / 0 FAIL |

## 首次推送后的 CI 失败与修复（2026-09-11）

推送 23 个提交到 origin/main 后，CI 的「Verification gate self-test」步骤失败，本地同一命令一直 exit 0。

根因：`scripts/test-verification-gates.ps1` 含非 ASCII 文本且没有 UTF-8 BOM 头部。Windows PowerShell 5.1 会用**系统 ANSI 代码页**读取无 BOM 文件，而 CI 的 Windows runner 是 CP1252、开发机是 GBK，同样的字节因此解出不同文本。em-dash（U+2014，UTF-8 字节 E2 80 94）在 CP1252 下解成 `â€”`，其中 0x94 正是右双引号 U+201D，PowerShell 把它当字符串结束符，于是字符串提前闭合、整份脚本解析失败（CI 报 `Unexpected token 'not' in expression or statement`）。本地因 GBK 把同样的字节解成无害汉字而侥幸通过，属区域设置相关的可移植性缺陷，由本次工作引入（脚本与 CI 步骤同批加入，此前从未在 CI 上执行过）。

复现方式（无需 CI）：把该文件按 CP1252 解码后统计引号类字符，可检出恰好 1 个 U+201D。

修复：

1. 该自测里的 phantom fixture 字符串改用 ASCII 连字符，去掉最高风险字符（CP1252 下解成字符串结束符的那一类）。
2. 给 `scripts/test-verification-gates.ps1` 与同样含非 ASCII 的 `scripts/sts2-coop-full-run-acceptance.ps1` 补上 UTF-8 BOM，使 PowerShell 在任何区域设置下都按 UTF-8 读取。
3. 新增 `script-encoding` 闸门：`scripts/*.ps1` 含非 ASCII 时必须带 UTF-8 BOM，从根本上阻止同类问题复发。
4. 自测新增两个用例：带 BOM 的非 ASCII 脚本必须通过，去掉 BOM 后必须被拒。

复验：闸门 exit 0（列出两个带 BOM 的脚本）、自测 exit 0（9 个漂移用例 + baseline + restored）、preflight exit 0、C# 213 PASS / 0 FAIL、MCP 49 tests OK；解析错误数为 0；CP1252 下的引号类字符由 1 降为 0。
