# STS2 AI Agent：当前状态页

> 本页是仓库唯一的当前状态入口。更新日期：2026-09-13（v0.12.1 发布与工坊上传；随后 #85 合入主线未发布）。
> 发布代码基准：tag `v0.12.1` @ `640c343`；标签后主线变更（#85）单列在下方，不把文档更新视为新版本发布。
> 发布基准：[GitHub Release v0.12.1](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.1)，2026-09-13 发布；上一版 [v0.12.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.0)，2026-09-13。2026-09-13 通过 Steam Web API 核对工坊物品 3796486050：visibility=0（公开）、file_size 1213444 与本地内容字节和相等、time_updated 2026-09-13 12:41:42。**工坊简体中文列表仍是旧版**（缺 v0.11.0 与 v0.12.0 就写进仓库的两条列表项，2026-09-13 重新抓取工坊页面确认），待手工粘贴 `steam-workshop/description.zh-CN.txt`——`ModUploader` 没有语言参数，这一步只能在工坊网页端做。

旧路线图见 [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md)（历史），旧交付原文见 [history/PRODUCT_PLAN_CURRENT_2026-09-07.md](history/PRODUCT_PLAN_CURRENT_2026-09-07.md) 和 [history/COOP_DELIVERY_2026-09-07.md](history/COOP_DELIVERY_2026-09-07.md)。[COOP_DELIVERY.md](COOP_DELIVERY.md) 现在只是历史证据索引。本页不继承历史文档中的审批、工作树或测试前执行约束。

## 1. 当前基线

- **v0.12.1（当前发布基准）**：2026-09-13 发布，发布提交 `f0f3b2a`（`Release v0.12.1`）经 PR #95 合并为 `640c343`（tag 指向合并提交，两者树内容相同），GitHub Release 资产 `sts2-ai-agent-v0.12.1-windows.zip`（539550 字节，SHA256 `26C6FE8445BA730B193F7697A26EB09BCD52A7532A09F4D422E14F8769BC16C2`），CI 在 `f0f3b2a` 上的 push 与 pull_request 两个 Validate run 均 success（合并提交 `640c343` 上的 Validate run 也 success）。工坊物品 3796486050 已更新：公开、`file_size` 1213444 与本地内容字节和相等、`time_updated` 2026-09-13 12:41:42、内容 id `3644578850671780412`。见 [v0.12.1 发布记录](history/release-v0.12.1_2026-09-13.md)。本版内容是暂停边界（#88 / #89 / #92 / #93，提交 `31296bd`、`3cf347a`、`04748f6`），唯一实机证据是 #93 的两轮隔离验收（`verify-capstone-pages.log`，FAILURES: 0）。**本版未再做完整实机验收**，待办项见 `docs/live-validation-checklist.md`。
- **v0.12.0（上一版）**：2026-09-13 发布，发布提交 `69602c9`（`Release v0.12.0`）经 PR #86 合并为 `69887a3`（tag 指向合并提交，两者树内容相同），GitHub Release 资产 `sts2-ai-agent-v0.12.0-windows.zip`（533493 字节，SHA256 `C2B1F3229D6CF8E7D757D571AAF717004DDBCAFD4F8AAE1276A86A54A3AFA685`），CI 在 `69602c9` 上的 push 与 pull_request 两个 Validate run 均 success。工坊物品 3796486050 已更新：公开、`file_size` 1202181 与本地内容字节和相等、`time_updated` 2026-09-13 02:25:02、manifest `3382317012139913714`。见 [v0.12.0 发布记录](history/release-v0.12.0_2026-09-13.md)。新增 `continue_ai_teammate` / `CompanionAutoSelectCharacter`（#83 / #84）与状态可信度收口（`72c96fd`、`12c35b3`、`492722a`、`ca12a4f` 等）随本版发布。
- **v0.11.0**：2026-09-12 发布，发布提交 `84631b9`（`feat(i18n): follow the game language in the overlay and the state payload`），GitHub Release 资产 `sts2-ai-agent-v0.11.0-windows.zip`（677015 字节），CI Validate `34629717120` success。工坊物品 3796486050 当时更新为 `file_size` 1135844、`time_updated` 2026-09-12 02:33:39。见 [v0.11.0 发布记录](history/release-v0.11.0_2026-09-12.md) 与 [本地化验收](history/localization-2026-09-12.md)。只更新工坊的 v0.10.7 发布提交 `f9330ba` 已包含在 `v0.11.0` 里。
- **已随 v0.11.0 发布**（`git log 2f75e4a..84631b9`，共 13 个提交）：
  - `d77982a` 卡牌网格选择元数据改读基类 `NCardGridSelectionScreen`，修复 #82 的升级/变形/附魔选牌屏（隔离副本实测：附魔 1/1/0 → 0/3/0、首次点击 10s 超时 → 151ms；变形 36ms/187ms；升级 173ms 无回归；事件多选 2/2/0 无回归）。
  - `7b02168` 战斗状态暴露自家宠物（`pets[]` / `pet_missing`），紧凑视图同步；Necrobinder 实测奥斯提 1/1 与 `DIE_FOR_YOU_POWER`。同批补测 Defect 球槽（`orbs[]`/`orb_capacity`/`empty_orb_slots`、DUALCAST 后清空）。
  - `deafa23` 主动发言配额与对局边界按会话收敛（每会话 6 条上限随自动游玩会话重新发放，75 秒间隔仍是跨会话全局约束；自动游玩开始时重置对局边界）；`2526832` 恢复「离开对局即停止」并加固 `POST /action` / `/session/control` 的请求契约；`d6ede1f` 单步回合也计入会话预算；`ccf093e` full profile 每个动作都有 legacy 工具；`03507dc` / `e4c5797` / `c3e654c` 修正状态页与 `docs/api.md` 的过期口径。
- **已随 v0.10.5 发布**（2026-09-08，tag `04d2466`）：v0.10.4 之后合入的产品提交 19710ad（#78 空奖励 overlay 不再卡 pending）、4b4da6e（#79 continue_game_over 等待原生结算写入）、22907b0（#80 会话预算 / 损坏配置恢复 / MCP Origin / 停流超时 / play_card 取消）；v0.10.5 GitHub ZIP 已发布并安装到 Steam mods/，Workshop 订阅加载验收见 [验收记录](history/workshop-load-acceptance_2026-09-09.md)。
- **已随 v0.10.6 发布**（2026-09-11，tag `2f75e4a`）：`bd93662` 停止原因分类；`a9d4478` 主动发言与可选语气（默认关闭）；依赖安全 #50/#51（`cd55fe1`：`fastmcp` → 3.4.7、`fast-uri` → 3.1.7，`npm audit` 0 项）；离线验证闸门 `scripts/check_verification_gates.py` 与自测（`c212594`、`6aabb4f`）；`34c6e91` 归档 `docs/sts2-coverage-gaps.md` 至 `history/sts2-coverage-gaps_2026-03-10.md` 并给带日期的验证记录补历史快照标记。曾未进入 v0.10.5 标签的提交（96bd410 工坊更新默认 public、15e483c/2838250 发布与 P3 记录、4f1ec66 收尾日志）也已随 v0.10.6 发布。
- **仓库卫生（已入库，不构成发布）**：90s continue 超时与不重复 Continue；Trellis spec/skills/platform 纳入版本控制，00-bootstrap-guidelines 已归档；stash@{0}（ai-companion 旧脏树）已 drop；`.trellis/.template-hashes.json` 保持本地不入库；`c69d3b3` 只忽略本地 mod-uploader.log，`374d7fd` 只记录 Trellis journal。
- **已随 v0.12.0 发布：2026-09-12 的五项目标**（任务树 `.trellis/tasks/09-12-agent-trust-hardening`，各子任务归档在 `.trellis/tasks/archive/2026-09/`）：
  - `72c96fd`（action-trust）动作可信度收口：`resolve_rewards` 显式越界索引改报 409 `invalid_target` 且不再点错牌；`continue_run` / `embark` / `open_character_select` 遇到阻塞弹窗返回 `pending`，不再拿「有弹窗」当成功；`remove_card_at_shop` 的购买失败不再被吞成 `pending`；bundle 动作不再配空 state 报 `completed`；`play_card` 未离手时回滚回合计数。
  - `12c35b3`（bounded-game-waits）九处游戏侧 `await` 全部加上期限：超时返回 `pending` 并点名动作与超时；任务以 false 或异常收尾时返回 409 `invalid_action`；`GameActionService.cs` 的裸 `await` 由源码契约测试拦截。
  - `33b137e`（agent-contract-compact）skill 改读紧凑状态字段名（`selection.min|max|selected|confirm`、`shop.open`、`chest.claimed`、`character_select.embark`、`timeline.slots[].i|line|actionable`）；`get_game_state` 增加 `compact_agent_view`；`wait_until_actionable` 增加 `actionable`；full profile 的 `resolve_rewards` 允许不带索引。
  - `5457e0d`（screen-index-contract）`choose_timeline_epoch` 按 `timeline.slots[].index` 取槽、不可操作槽返回 409 `invalid_target`；`crystal_clear_cell` / `crystal_set_tool` 的 descriptor 分别暴露 `requires_coordinates` / `requires_tool`；新增 `FAKE_MERCHANT`（`open_shop_inventory` 可用）、`PATCH_NOTES`（`close_main_menu_submenu` 可关）、`CARD_INSPECT` / `RELIC_INSPECT`（`close_cards_view` 可关）、`FEEDBACK` 五个屏幕名。
  - 本次文档收口（docs-release-baseline）：本文件改为 v0.11.0 基准；`AGENTS.md` 版本号列全五个文件并改成与 `.github/CONTRIBUTING.md` 一致的 PR 发布流程；`docs/api.md` 补 `failed` 状态、`requires_coordinates` / `requires_tool`、五个屏幕名、时间线索引契约与两个关闭动作的扩大范围；两个 README 补路由与对称段落；`steam-workshop/workshop.json` 的 changeNote 更新到 0.11.0；`scripts/preflight-release.ps1` 补跑 CI-only 的 `test-verification-gates.ps1` 与 `test-native-exit-propagation.ps1`。
  - 以上五项只有离线证据：2026-09-12 本次收口后 `dotnet run --project STS2AIAgent.Tests -c Release` 272 PASS / 0 FAIL、`mcp_server` 单测 76 项 OK，`scripts/check_verification_gates.py` 四闸门全绿，`preflight-release.ps1` exit 0；没有实机复验。逐项记录见各子任务归档的 `prd.md` / `evidence.md`。

- **v0.12.1 标签后主线未发布变更**：
  - `d80a19d`（PR #97，[issue #85](https://github.com/CharTyr/STS2-Agent/issues/85)）把「邀请 AI 队友」的前置要求按路线拆开——游玩模型已验证时队友照旧自走；未配置 / 未验证 / 验证失败时队友照样拉起、照样进图，但子进程拿到 `STS2_AGENT_AUTOPLAY=0`，停在原地等待外部接管，且这条路线根本不调用模型。两条路线共用的结构条件（不是队友实例、该角色没有正在跑的自动游玩、必须在主菜单）抽成 `CoopLaunchPolicy.GetStructuralError`，拆分只作用在模型这一档。同一 PR 新增主窗口的 `POST /teammate/control`（外部 agent 的开始 / 暂停入口，不需要队友会话令牌）与 `GET /health` 的 `companion` 区块（`api_host` / `api_port` / `process_id` / `auto_play`，不含令牌）。
  - 实机证据：2026-09-13 隔离主机（`--clientId 2026091001`、API `18080`）全程 HTTP 驱动，模型端点指向**死端口** `127.0.0.1:18199` 且 `roleTests` 为空。一次完整跑通：`invite_ai_teammate` 首次调用即 200 `completed`；队友以 `role=companion` 起在 `18081`；进图 20 秒后 `play_running=false`、`play_phase=paused`、`session_requests=0`、`stop_kind=null`；队友 API 暴露 `choose_map_node` 可被外部驱动；`/teammate/control` 暂停 200、未验证时开始 409、非布尔 400。玩家 Steam 真实存档 183 个文件哈希前后一致。脚本与证据：`build/validation-2026-09-13/verify-takeover.ps1`、`takeover-evidence.jsonl`（gitignore）。记录见 `docs/live-validation-checklist.md` 的「External-takeover route (issue #85)」。
  - 该 PR 还修掉三处实机发现：路线标记原先在启动校验**之前**写入，导致一次被拒的重试会把正在自走的队友误标成 `auto_play:false`；`/health` 在队友进程退出后仍继续报其端口；`teammate_control_failed` 文档写可重试、实现对 `retryable:false`。
  - `7a371c7`（PR #99）补齐开局门禁并记录真实外部 agent 验收：队友实例自己的 `POST /session/control {"running":true}` 此前会绕过模型验证直接启动进程内循环（宿主「继续游玩」/`/companion/control`/`/teammate/control` 都拒绝的同一请求它接受）——`SetCompanionRunningAsync` 现在同样过 `FirstRunSetup.ReadyToInvite`，暂停刻意不过门禁；实机确认队友侧 `running:true` 返回 409 `session_not_ready`、`running:false` 返回 200。
  - **真实外部 agent 端到端接管验收通过**：外部 agent（Grok 4.6，自带模型与上下文，只给仓库自带的 MCP 工具面与 play skill，不是进程内循环）在**未配置任何模型**的队友窗口上自己打完一场完整战斗——队友座位 16 次 `play_card` + 6 次 `end_turn` + 4 次 `confirm_modal` 加地图投票，`FUZZY_WURM_CRAWLER` 从 `121/121` 打到死（7 回合），领奖 `REWARD` → `MAP`，金币 `99` → `110`，第二场（`SHRINKER_BEETLE`）已开打时收工；全程队友 `play_running=false`、`play_phase=paused`、`session_requests=0`，日志 84 行机械核查 `/teammate/control`、`/session/control`、`run_console_command` 命中 0 次。证据 `build/validation-2026-09-13/external-agent-log.jsonl` + `external-agent-final-state.json`，驱动 `mcp-call.py`；玩家真档 183 文件哈希不变。
  - 记录未改动的发现：`NCombatRulesFtue` 在队友侧要 3 次 `confirm_modal`（前两次 `pending`），而宿主此时已在 `COMBAT` 可出牌；`get_relevant_game_data` 实际要求 `collection` + `item_ids`，skill 却描述为可无参按场景取；怪物元数据不含联机缩放（`min_hp=55/max_hp=57` vs 实况 `121/121`）。
  - 尚未随任何 tag 发布；队友窗口本身没有 overlay（`ModEntry` 对 companion 跳过），这一点已写进文档。

## 2. 已有验收证据与边界

以下结果来自 2026-09-08 对隔离候选构建的实际执行。证据目录：build/validation-2026-09-08/（已 gitignore，不作发布产物）。

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| C# 核心测试 | 180 PASS，0 FAIL | 离线测试结果，不等于完整 Mod 或自然结束整局通过 |
| 停流超时回归 | loopback 先回 header、正文停流的按契约分类（超时 vs 用户取消） | 离线回归 LlmClientTests；生产默认请求超时 10 分钟、HTTP 客户端 11 分钟（OpenAiCompatibleClient.DefaultRequestTimeout，22907b0/#80 起）。真实上游超长停流的端到端仍后置 |
| MCP Origin | 7 项离线测试通过；隔离 Host 实机探测 | 不是完整浏览器页面利用 |
| Mod Release 构建 | 0 warning 0 error；SkipInstall 生成 DLL/PCK | 此行记录隔离候选构建；后续正式打包与安装见下方发布验收 |
| 隔离双开 13 层 | 2026-09-08 08:35 打到 13 层原生 GAME_OVER | full-run-result.json。Host progress 聚合当时未更新；不能顶替下面短路径 |
| P1.3 超限 UI | 2026-09-08 请求上限=1，stop_kind=budget，overlay 出现上限文案与下一步 | p13-overlimit-result.json；重置按钮截图 ui-budget-scrolled3.jpg。不是真实 MiniMax 超长停流。 |
| 隔离双开 GAME_OVER 存档 | 2026-09-08 20:24-20:28，当前 main 隔离 DLL 72C72F02。第一场地图战斗空过团灭，双方 continue_game_over 各一次（4.22s / 2.42s），save_verified=true，双方 progress.save mtime 在 continue 后更新，返回主菜单。forced_return_suspected=false | gameover-save-result.json outcome=gameover_save_ok。控制台 fight/die 会触发多人数据不同步断线，短路径改为自然进战斗 + end_turn 团灭。total_losses 本局未增加（host 仍为 2），score 有更新。已随 v0.10.5 发布。 |

历史候选绑定：隔离 DLL SHA256 72C72F022F7EC32563BCDFA5F3269449DFFF0956A764441E8D7565A8D8AED920；隔离 exe SHA256 8602C26BFFD2937E3841835FD8360EF8E974624A543E05977229FD3D062BE231。该隔离验收未修改真档快照；Steam 手动安装随后已更新到 v0.10.5，见 P2.4。

后续发布验收：2026-09-08 preflight-release、发布目录/ZIP artifact check 已通过，记录见提交 15e483c。2026-09-09 复核 GitHub 资产 `sts2-ai-agent-v0.10.5-windows.zip`（SHA256 `27da01401714347143244badfb9674e0e31932d03f1e5bebe1e4c67705fb12b8`）、Workshop 公开状态和本地订阅清单。后续用户启用并重启游戏，已核对本次 Workshop 加载日志、悬浮窗和运行接口；见 [订阅加载验收](history/workshop-load-acceptance_2026-09-09.md)。未重跑历史游戏测试。

## 3. 能力状态矩阵

| 能力 | 实现提交 / 版本 | 发布归属 | 验收证据 | 剩余缺口 |
| --- | --- | --- | --- | --- |
| 游戏内 UI、聊天、自动游玩 | v0.9.0 | 已发布 v0.9.0 | 发布记录 | 完整自然结束仍待实机；短路径结算已做 |
| AI 队友启动隔离与地图投票 | v0.10.0-v0.10.1 | 已发布 | 隔离双开 | 控制台 fight/die 在双开会不同步断线 |
| 原生 MCP | v0.10.2 | 已发布 | Origin 契约；2026-09-08 外部客户端 initialize/list_tools/ping/关闭成功 | 已验证 streamable-http 客户端，不代表所有桌面客户端均实测 |
| 共享 skill 与投票 | v0.10.3 | 已发布 | 离线测试；隔离实机 | 完整 13 层结算不是当前门槛 |
| FTUE 与时间线解锁 | v0.10.4 | 已发布 | 离线测试；隔离 FTUE | 无 |
| 首次配置与诊断 | v0.10.4 | 已发布 | 隔离邀请成功 | 无 |
| 恢复、预算与暂停 | v0.10.4 + #80 | 已发布 v0.10.5 | 暂停探测 ok；P1.3 超限 UI 已看 | 真实上游超长停流仍后置 |
| 空奖励 overlay | 19710ad | 已发布 v0.10.5 | 180 PASS；发布预检与 ZIP 检查通过 | Workshop 订阅加载冒烟已通过；不代表完整对局验收 |
| GAME_OVER 等待原生结算 | 4b4da6e（#79） | 已发布 v0.10.5 | 2026-09-08 20:28 隔离双开 continue 4.22s/2.42s，save_verified，progress mtime 更新 | total_losses 未观察到 +1 |
| 简单选牌屏状态与确认（#81 / #82） | 1b7236a、27fa223；#82 修复 d77982a | v0.10.6 已含 #81；#82 修复已随 v0.11.0 发布 | 合并构建隔离实机：事件多选报 2/2/0、两次点击 30ms/131ms 完成、原生 chose cards 记录；Sea Glass（0/15 手动确认）暴露 confirm_selection 并 168ms 完成。#82：附魔屏 1/1/0 → 0/3/0、首点 10s 超时 → 151ms、单次确认 166ms；变形屏 36ms/187ms；升级屏 173ms 无回归 | 只有同一台机器上的隔离副本证据；未在正式安装或真实存档上复跑 |
| 战斗自家宠物与球槽状态 | 7b02168 | 已随 v0.11.0 发布 | 隔离实机：Necrobinder `pets[]` 报奥斯提 1/1 与 `DIE_FOR_YOU_POWER`、`pet_missing=false`，紧凑视图同步；Defect `orbs[]`/`orb_capacity=3`/`empty_orb_slots`，DUALCAST 后清空 | 只有 Necrobinder / Defect 两个样本；其它召唤物与充能球类型未逐个采样 |
| 自动游玩停止原因分类 | bd93662（已随 v0.10.6 发布） | 已发布 v0.10.6 | 离线回归：连续 3 次空决策不再报 `run_end`；两条真实边界文案仍归 `run_end` | 分类基于停止文案匹配，不是结构化错误码 |
| 主动发言与交流风格 | a9d4478（v0.10.6）；会话配额与对局边界修复 deafa23（v0.11.0） | v0.10.6 已含基础实现；会话收敛随 v0.11.0 发布 | 2026-09-10 隔离实机 + 本地桩验证触发、提示词、语气注入、只读 chat 路径、回复消费五项；2026-09-11 再验闸门：开关关闭时 50 次请求 0 条主动发言、战斗开始与结束均触发、9 次发送间隔 88–137 秒、发送后 12 秒的强制转移保持沉默、6 条后第 7 个时刻被拒、暂停再继续后额度恢复。见 [09-10 验收](history/validation-acceptance_2026-09-10.md) 与 [09-11 验收](history/validation-acceptance_2026-09-11.md) | 默认关闭的可选功能；仅战斗开始/结束触发，最多 6 句（每自动游玩会话）、间隔 ≥75 秒（跨会话）。真实模型在真实对局中的发言质量仍未验收 |
| 依赖安全（#50 / #51） | cd55fe1 | 已发布 v0.10.6 | fastmcp 3.4.7、fast-uri 3.1.7；npm audit total 0；MCP 49 项单测通过 | 只覆盖这两条报告与 npm 树，不是完整的第三方审计 |
| 文档契约与验证闸门 | c212594、6aabb4f | 已发布 v0.10.6 | `check_verification_gates.py` 四闸门全绿；自测漂移场景全部被拒；preflight 端到端 exit 0 | 静态检查，不能替代实机行为验证 |
| 暂停与局内菜单页的屏幕名（#88 / #93） | `31296bd`（#88/#89）、`3cf347a`（#92）、`04748f6`（#93） | 已随 v0.12.1 发布 | 2026-09-13 隔离实机两轮逐屏核对：`PAUSE_MENU` / `SETTINGS` / `COMPENDIUM` / `CARD_LIBRARY` / `RELIC_COLLECTION` / `POTION_LAB` / `STATS` / `RUN_HISTORY` 各自报名、动作面只剩 `close_main_menu_submenu`、`choose_capstone_option` 全 409，FAILURES: 0 | `BESTIARY` 未实机开屏（该存档 hub 不画磁贴）；`save_and_quit` 的 409 是实机发现后补的 |
| 外部 agent 接管队友窗口（#85） | `d80a19d`（PR #97） | **主线未发布**（晚于 tag `v0.12.1`） | 2026-09-13 隔离实机：死模型端点 + 空 `roleTests` 下 `invite_ai_teammate` 仍 200 `completed`，队友 `auto_play:false`、进图 20 秒 `session_requests=0`，`/teammate/control` 暂停 200 / 未验证开始 409，`/health.companion` 可发现队友 API；真档 183 文件哈希不变 | 未在 Steam 双开路径复跑；外部接管路线尚未用真实外部 agent 端到端扮演一次完整战斗 |

## 4. 待办任务

优先级从高到低。没有明确负责人时记为“未分配”。不要把盘点或收尾做成完整 13 层自然通关。

已完成、移出待办的里程碑（不再单列）：v0.10.5 / v0.10.6 / v0.10.7 / v0.11.0 的发布、安装与工坊更新；90s continue 超时与只点一次；Trellis 纳入版本控制并归档 bootstrap；旧 stash 已 drop；依赖安全 #50/#51；验证闸门与自测；模型兼容矩阵；外部 streamable-http 客户端核查；非法预算安全上限、损坏配置备份恢复、MCP Origin 契约、停流超时契约、play_card 取消、空奖励 overlay、continue_game_over 等待原生结算；外部 agent 接管队友窗口（[issue #85](https://github.com/CharTyr/STS2-Agent/issues/85)，PR #97 合入 main `d80a19d`，2026-09-13 隔离实机验收通过）。原始记录保留在提交历史与 `history/` 下（[v0.11.0 发布记录](history/release-v0.11.0_2026-09-12.md)、[v0.10.7 工坊上传](history/workshop-upload-v0.10.7_2026-09-12.md)、[v0.10.5 订阅加载验收](history/workshop-load-acceptance_2026-09-09.md)）。

### 后置（本轮不做，留到下一次发布前复核）

1. **真实上游超长停流的端到端**：证据止于生产超时契约（生产请求超时 10 分钟、HTTP 客户端 11 分钟，22907b0/#80）与本机 loopback 回归；未接真实上游复现长停流。
2. **真实浏览器页面加载的 Origin 场景**：证据止于 MCP Origin 离线契约与本机探测，不是完整浏览器页面利用。
3. **英文文案母语审校**：v0.11.0 的英文界面与状态文案是机器翻译，未经母语者复核（见 [本地化验收](history/localization-2026-09-12.md) 的 "Not covered"）。
4. **v0.10.7 是否补 GitHub tag**：v0.10.7（`f9330ba`）只更新了 Steam 工坊，未打 tag、未建 Release，而该提交已包含在 `v0.11.0` 里；是否回填 tag 由发布者决定。
5. **工坊简体中文列表**：页面上仍是旧版文案，缺 v0.11.0 与 v0.12.0 就写进仓库的列表项（2026-09-13 抓取页面确认）。`ModUploader upload` 只有 `-w` / `-i`，没有语言参数，只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`。

### 剩余事项与证据边界

- Workshop 订阅加载已收口；未额外执行第二轮重启或升级回退，不将这些扩展项目计为已验证。
- 本页引用的实机结果都来自隔离副本与本机桩（2026-09-08 / 2026-09-10 / 2026-09-11 / 2026-09-12），不代表正式安装与真实存档。完整自然结束长局不作为文档收尾门槛。
- 已知观察：双开控制台 fight/die 会导致不同步；短路径结算中 total_losses 未观察到 +1，不能将 save_verified 扩大为所有统计字段均已验收。
- 产品边界：模型兼容矩阵主要是源码与离线协议证据，不代表所有服务商均经过实机测试。主动发言与可选语气已用本地桩完成隔离实机验收（见 history/validation-acceptance_2026-09-10.md、2026-09-11.md），但**未用真实模型验收**：真实模型在真实对局中是否在合适时机说出合适的话仍未验证。
- 本轮 09-12 五项目标（§1 标签后清单）只做了离线代码与文档收口：C# / MCP 单测 + 验证闸门 + 静态预检，没有实机复验；新增的屏幕名（`FAKE_MERCHANT` / `PATCH_NOTES` / `CARD_INSPECT` / `RELIC_INSPECT` / `FEEDBACK`）与时间线索引契约都还是代码契约层面的结论。

## 5. 后续维护规则

1. 只有 PRODUCT_PLAN_CURRENT.md 描述当前状态；COOP 根文件只做历史证据索引，不能再发展为第二任务板。
2. 每项能力同时记录实现提交/版本、发布 tag、验收证据日期与来源、剩余缺口。已发布只能来自可核对的 tag/release 证据。
3. 标签后提交必须单列为“主线未发布”，直到出现明确 release tag。
4. 原计划与旧 COOP 原文放在 history/，醒目标注“历史，不代表当前”。
5. README 入口持续指向本页和 COOP 历史索引；打包脚本依赖的 ./PRODUCT_PLAN_CURRENT.md 与 ./COOP_DELIVERY.md 相对目标字面保持不变。
6. 归档目录见 [history/README.md](history/README.md)。旧文档只保留时间点证据，不能重新作为任务板；原路径跳转页用于兼容引用和预检。
