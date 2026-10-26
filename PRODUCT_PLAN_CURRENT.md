# STS2 AI Agent：当前状态页
> **当前发布：v0.16.2（2026-09-24）**。tag `v0.16.2` @ `829ba23`（PR #201 dev→main 合并，两组 `contracts` CI 通过，dev 已快进同步）；[GitHub Release](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.16.2) 资产 783680 字节 / SHA256 `97EFF7FF…86A3C`；工坊物品 3796486050 于 15:28:17 更新（公开，`file_size` 1655271 = 本地内容字节和）。本版补双层规划/决策证据、Jev 回退可见性、脱敏诊断端点与资源清理。离线 882 C# + 423 Python、发布预检和 CI 通过；隔离 Steam-off 冒烟验证诊断与邀请拒绝路径，**新 Jev/截图边界和最终发布包的付费模型真机路径未复验**。见 [v0.16.2 发布记录](history/release-v0.16.2_2026-09-24.md)；上一版见 [v0.16.1 发布记录](history/release-v0.16.1_2026-09-24.md)。**工坊简体中文列表仍待网页端手工粘贴**。
> 下方各版本的发布、工坊与实机数据保留其原始日期，作为历史证据。

旧路线图见 [history/PRODUCT_ROADMAP_2026-09-05.md](history/PRODUCT_ROADMAP_2026-09-05.md)（历史），旧交付原文见 [history/PRODUCT_PLAN_CURRENT_2026-09-07.md](history/PRODUCT_PLAN_CURRENT_2026-09-07.md) 和 [history/COOP_DELIVERY_2026-09-07.md](history/COOP_DELIVERY_2026-09-07.md)。本页不继承历史文档中的审批、工作树或测试前执行约束。

## 1. 当前基线

- **主线 dev（未发布，v0.16.2 之后）：2026-10-27 实机验收，核心功能通过，模型相关项仍被外部阻塞。** 基线 `fa57fc8`（含 #213、#215、#216、#217、#220）。Windows 机器上首次带游戏程序集构建时发现 `BuildMapPayload` 的 CS0173 类型推断错误（离线套件编译不到该处），已修复并同步契约哈希，修复后 Release 构建 0 警告 0 错误。**不能据此判定全部验收通过。** 原始报告与 294 份证据在测试机的 `build/live-2026-10-27/`（未入库）。

  | 项目 | 结果 |
  | --- | --- |
  | 网络 MCP 认证、游戏内 REST 访问加固（#217）、原生 MCP、正常 Python 客户端 | 通过 |
  | 隔离档开局 | 通过（Silent 未解锁，改用 Ironclad） |
  | 悬浮窗（#216）：8 套主题、设置跳转条（非高级 4 段、高级 7 段）、约 500px 短窗口底栏、MCP 文本选择、切语言保留草稿 | 通过（主要视觉；未穷举每套主题的单/多人组合） |
  | 决策日志回滚、决策理由方括号（HTTP 路径） | 通过（有限覆盖：新增决策不拉回向上滚动的位置；未覆盖流式输出、回底跟随与缩放组合） |
  | 模型聊天回滚、模型聊天里的方括号卡名 | **BLOCKED**：没有成功的模型响应 |
  | 满药水奖励六步（#219 / #220）：`claimable=false`、409 `potion_slots_full` 拒领且无副作用、丢弃后可领、批量领取不卡住 | 通过 |
  | 游戏截图隐藏悬浮窗并恢复 | 通过 |
  | Steam 在线双开、零模型外部接管完整战斗、跨角色操作拒绝 | 通过 |
  | 联机保存续局（`run_id` 与座位不变）、主窗口退出后队友清理、队友端控制主机角色返回 403 `forbidden_actor` | 通过（均为零模型路径） |
  | 本地残缺模组副本冲突拦截 | 通过（拉起前失败、提示目录路径、无队友进程、文件不变；旧版/重复的平铺与嵌套副本未单独测） |
  | 模型视觉回答、队友自动游玩与跟图、DeepSeek/代理与思考开关、仅非战斗模式 | **BLOCKED**：服务商返回 HTTP 400 `insufficient credits`，另一条配置路由返回 403 认证失败 |
  | 工坊简体中文说明 | **未验收**：工坊页面无法访问，线上文案是否已替换未知，仍需网页端手工粘贴 `steam-workshop/description.zh-CN.txt` |
  | 3 到 4 人局 | **NOT_RUN**：没有额外在线玩家 |

  离线复核：12 项静态闸门全通过；C# 984 通过 / 2 失败（`NativeMcp.OriginHttpPolicy`、`LoopbackListener.RealLoopbackListenerResponds`，该机器启动真实 `HttpListener` 报“句柄无效”），Python 433 通过 / 4 失败（四个 knowledge 无 checkout 夹具：仓库外 TEMP 不可写，回退到 `mcp_server` 后向上找到了真实仓库）。两处均判为该机器环境问题，非产品回归，完整日志在测试机报告里。

  测试中还看到两处尚未定性的现象：部分中文状态消息在 API 原始数据里出现替换字符（错误码和路径正常，**尚未确认是否为新增回归**）；多层菜单都报告 `MAIN_MENU`，一次 `close_main_menu_submenu` 只退一层，过早「继续联机存档」会返回 409，逐层退回根菜单后正常、未丢档。真实档：A 轮 185 个文件哈希完全一致；C 轮只多出测试联机存档、进度和回放共 5 个预期差异，原单人续关未变，无新增损坏存档。发布前仍欠：补有额度的模型重跑 BLOCKED 项，并确认工坊中文说明。

- **第二轮 5 项行为修复**：重复动作、当轮 Token 检查、取消后的回执、主动发言预算顺序、非对象动作参数已进入本地 Unreleased。详见 [回合完整性修复记录](history/agent-turn-integrity-2026-09-21.md)。本轮沿用离线验证范围。

- **v0.16.0（当前发布基准）**：2026-09-24 发布，思考内容实时显示、双层模式宏观目标与提示对齐、覆盖层溢出根因修复、截图不含覆盖层；详见 [v0.16.0 发布记录](history/release-v0.16.0_2026-09-24.md)。下面两条是当时的本地状态，现已随本版发布。

- **（已随 v0.16.0 发布）本地 dev：v0.15.0 + Unreleased 审查修复**。`628ba75` 处理单人/多人模式互斥、同局下一手指令、Jev 429 预算和重试、会话归属以及 HTTP/双 MCP 简报；`455641f` 补游戏桥接与会话观察边界的规格。任务已归档于 [审查修复记录](.trellis/tasks/archive/2026-09/09-23-audit-remediation/prd.md)。这些是离线验证结果，尚未进行本轮实机双开与真实 Jev 验收。

- **2026-09-21 的历史本地 dev：v0.14.5 + 当时的 Unreleased 修复**。起点为 `65c9ad9`；当时工作覆盖主题保存、队友身份、两面状态差异、事件流生命周期及模型取消处理。旧版验证见 [当时的审查记录](history/dev-audit-2026-09-21.md)。

- **v0.14.0（历史发布基准）**：2026-09-21 发布。发布提交 `5f9ea72`（PR #174 的 `dev → main` 合并；
  功能经 PR #173 合入 `dev`，CI `contracts` 通过），tag `v0.14.0` 指向它。GitHub Release 资产
  `sts2-ai-agent-v0.14.0-windows.zip` 637550 字节 / SHA256 `37F95EED…2A70`。见
  [v0.14.0 发布记录](history/release-v0.14.0_2026-09-21.md)。
  内容见 [CHANGELOG.md](CHANGELOG.md) 的 `## v0.14.0` 段：决策解释链与决策日志（overlay /
  `GET /decisions` / 两面 MCP / SSE `decision_made`）、按屏注入的策略层、结构化队友信号与队友实况面板、
  **界面重做与四套配色主题**、生成式 OpenAPI 契约、Python client 两个载荷类型化、
  `GameStateService` 按屏拆分，以及原计划单独发版的 v0.13.1 事件流可靠性批（v0.13.1 有发布提交但
  从未发布，已并入本版）。
  **实机验收现状**（2026-09-20，隔离档 `default\2026092014`，游戏 v0.111.0，玩家真档未被写入）：
  `/health` ready 且 27/27 反射成员齐全、`mod-load --deep-check`、`state-summary`、
  `state-invariants`（0 失败 0 警告）、`patch-check`（含动作面基线 replay，`mismatches: []`）全部通过；
  **真实模型对局已跑通**——CommandCode / `deepseek/deepseek-v4.1-flash` 经 overlay「测试连接」
  两个角色均连通成功，无人值守自动游玩从 1 层打到 3 层，45 次决策全部归属同一 run id，
  26 条抽样里 22 条带模型自述理由，SSE `decision_made` 实时可见，最终由会话 Token 上限自动停止
  （实际 52 次请求 / 622,864 token）；**本地双开 co-op 亦跑通**——第二实例、共享 run、
  「跟随你的地图选择。」的协作规则与队友面板实况均有记录。细节见
  [live-validation-checklist.md](docs/live-validation-checklist.md)。
  **仍未做**：Steam 双开路径、完整自然对局、Vision 实机、typed intent 的线上路径、
  5.4/5.6、7.2 BESTIARY、7.3 机制矩阵补样。工坊渠道状态见发布记录的「工坊上传过程」一节。

- **v0.13.0（历史发布基准）**：2026-09-19 发布。tag `v0.13.0` 指向 PR #161 的
  `dev → main` 合并提交 `78b085f`；GitHub Release 资产 576033 字节 / SHA256 `8CFC0F45…8733`；
  工坊公开，`file_size` 1252357 与本地 content 字节和相等，内容 id `3840768014407368680`。
  发布候选的全量离线与实机验收通过；因重新构建的最终 DLL 与候选 DLL 字节不同，又对 GitHub 与
  工坊共同使用的 `6B9D90C8…FBE42` DLL 单独完成新局战斗、怪物数据、保存退出与继续对局短冒烟。
  见 [v0.13.0 发布记录](history/release-v0.13.0_2026-09-19.md)。

- **v0.12.5（历史发布基准）**：2026-09-17 发布，GitHub 与 Steam 工坊同日、同源、**各上传一次**。
  发布提交 `f361bdb`（PR #145 的 `dev → main` 合并），tag `v0.12.5` 指向它；Release 资产
  `sts2-ai-agent-v0.12.5-windows.zip` 562212 字节 / SHA256 `0E4A1518…CFED`；工坊 `file_size` 1238533
  与本地 content 字节和相等、内容 id `2962740913650521121`、可见性 0（公开）。
  见 [v0.12.5 发布记录](history/release-v0.12.5_2026-09-17.md)。

  两条玩家 / agent 能感知的运行时修复，**都由驱动真实游戏发现、并在打过补丁的构建上复验**：
  死亡后 `/state.screen` 恢复报 `GAME_OVER`（PR #142，此前整个结算阶段报 `COMBAT`，2346 个实机样本
  里该名字一次没出现过）；失败的游戏动作说出游戏给的原因（PR #143，12 条路径此前共用一句
  "the game task faulted"）。其余是 PR #141 的契约与工具收口。

  本版是**第一个不需要同号重发的版本**：这两条修复同样是 tag 之后才发现的，但等在
  `CHANGELOG.md` 新建的 `## Unreleased` 段里，直到本次发版才定版——而 v0.12.3 / v0.12.4 各有
  三个同号构建，正是因为当时没有这个段落可写。构建指纹也首次由打包脚本自动产出，
  发布记录里的哈希直接从 `build-fingerprint.json` 抄，不再事后手工采集。

- **v0.12.4（工坊先行，随后补 GitHub 发布，当日两次同号重发）**：2026-09-15 更新到 Steam 工坊（物品 3796486050，公开，`file_size` 1233413，`time_updated` 2026-09-15 19:38:00，来源提交 `4b8e7c5`）。内容是 PR #132 的可靠性修复：邀请 / 继续队友立刻返回 `pending` 且并发第二发不再误报、请求计费补上此前漏记的路径、`state-invariants` 只在执行器 ready 时要求 `play_card`、首次 `--clientId` 启动的隔离档种子在两个平台都写进游戏真正读取的目录。GitHub 侧随 2026-09-16 的 `v0.12.4` tag 与 Release 一并补齐（资产 `sts2-ai-agent-v0.12.4-windows.zip` 557906 字节 / SHA256 `AD970DB1…`，与工坊第三次构建同源）。见 [Workshop 上传记录](history/workshop-upload-v0.12.4_2026-09-15.md)。
（**同号重发**：修掉一份 `/state` 响应里 `available_actions` 与 `combat.action_readiness` 自相矛盾的一帧竞态——战斗门禁改为一次载荷只求值一次并沿调用链共享，readiness 成为它的纯投影；重发后 `file_size` 1236997、`time_updated` 2026-09-15 23:34:57。）

- **v0.12.3（当前发布基准）**：2026-09-13 首发、**2026-09-14 两次同号重发**——版本号始终不变，只换构建，所以工坊与 GitHub 两侧各发布了三次。首发发布提交 `b0217b0`（`Release v0.12.3 for the Steam Workshop`，经 PR #108 合并，tag 当时指向它，先发工坊 22:31、后补 tag 与 Release）；第一次重发把 tag 与 Release 重切到 `0f60ec4`（PR #113 的合并提交，内容 = #105 + #106 + #111），同日 00:09 重新上传工坊；第二次重发把 tag 与 Release 重切到 `c2630a8`（PR #123 的合并提交，内容 = #116 + #117 + #118 + #119 + #120 + #121 + #122），同日 02:17 重新上传工坊。GitHub Release 资产 `sts2-ai-agent-v0.12.3-windows.zip` 现为 555061 字节、SHA256 `E882B653B48B15EF278CFD9E20CC443FDCA3E5D69A51E362A40B934E29E167DF`（第二次那次 552014 字节 / `B7684C9F…`，首发 549933 字节 / `B9DC1A07…`）；工坊 `file_size` 现为 1232901（前两次 1229829 / 1227269）。三份构建的**版本字符串相同**，只能靠大小或哈希区分——这是反复同号重发的代价，CHANGELOG 的 v0.12.3 段顶部写明了这件事。见 [v0.12.3 发布记录](history/release-v0.12.3_2026-09-13.md)。第三次构建相对上一份**只有一处玩家可见变化**（Continue 按钮待机不刷新），其余是日志、发布工具与离线契约测试；**本版未单独做完整实机验收**：#106 的实机证据随身带来（未配置模型时真实点击拉起队友、`/health` 报 `companion.auto_play: false`），#111 的两个界面入口已在 2026-09-14 完成实机点击验收（见 `docs/live-validation-checklist.md` 的 `[coop]` 段），正是那次验收发现了本版修掉的 Continue 缺陷。
- **v0.12.2（上一版）**：2026-09-13 发布，发布提交 `40d1464`（`Release v0.12.2`）经 PR #104 合并为 `72b2a81`（tag 指向合并提交，两者树内容相同），GitHub Release 资产 `sts2-ai-agent-v0.12.2-windows.zip`（549033 字节，SHA256 `35C1F0FC0ED3C664C0F74A73B5759486E4CA2BE92295CC47062FCC65552B5D9D`），CI 在 `40d1464` 上的 push 与 pull_request 两个 Validate run 均 success。工坊物品 3796486050 已更新：公开、`file_size` 1225733 与本地内容字节和相等、`time_updated` 2026-09-13 16:51:04、内容 id `8439947284938535648`。见 [v0.12.2 发布记录](history/release-v0.12.2_2026-09-13.md)。本版内容是联机接力：#85 的外部接管路线与主窗口 `POST /teammate/control`、#99 的共享模型门禁、#101 的三处实机收尾。**代码与实机跑过的那份逐字节相同**（实机证据即 #85 / #99 / #101 的隔离验收），标签之后只多了 README 链接修复、新增的 `packaged-links` gate 与一处 overlay 邀请路线修复（后两笔见「v0.12.2 标签后主线未发布变更」）。**本版未再单独做完整实机验收**，待办项见 `docs/live-validation-checklist.md`。
- **v0.12.1**：2026-09-13 发布，发布提交 `f0f3b2a`（`Release v0.12.1`）经 PR #95 合并为 `640c343`。本版内容是暂停边界（#88 / #89 / #92 / #93），唯一实机证据是 #93 的两轮隔离验收（`verify-capstone-pages.log`，FAILURES: 0）。见 [v0.12.1 发布记录](history/release-v0.12.1_2026-09-13.md)。工坊当时为 `file_size` 1213444、`time_updated` 2026-09-13 12:41:42。
- **v0.12.0**：2026-09-13 发布，发布提交 `69602c9`（`Release v0.12.0`）经 PR #86 合并为 `69887a3`（tag 指向合并提交，两者树内容相同），GitHub Release 资产 `sts2-ai-agent-v0.12.0-windows.zip`（533493 字节，SHA256 `C2B1F3229D6CF8E7D757D571AAF717004DDBCAFD4F8AAE1276A86A54A3AFA685`），CI 在 `69602c9` 上的 push 与 pull_request 两个 Validate run 均 success。工坊物品 3796486050 已更新：公开、`file_size` 1202181 与本地内容字节和相等、`time_updated` 2026-09-13 02:25:02、manifest `3382317012139913714`。见 [v0.12.0 发布记录](history/release-v0.12.0_2026-09-13.md)。新增 `continue_ai_teammate` / `CompanionAutoSelectCharacter`（#83 / #84）与状态可信度收口（`72c96fd`、`12c35b3`、`492722a`、`ca12a4f` 等）随本版发布。
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

- **v0.12.2 的内容（已随该版发布）**：
  - `d80a19d`（PR #97，[issue #85](https://github.com/CharTyr/STS2-Agent/issues/85)）把「邀请 AI 队友」的前置要求按路线拆开——游玩模型已验证时队友照旧自走；未配置 / 未验证 / 验证失败时队友照样拉起、照样进图，但子进程拿到 `STS2_AGENT_AUTOPLAY=0`，停在原地等待外部接管，且这条路线根本不调用模型。两条路线共用的结构条件（不是队友实例、该角色没有正在跑的自动游玩、必须在主菜单）抽成 `CoopLaunchPolicy.GetStructuralError`，拆分只作用在模型这一档。同一 PR 新增主窗口的 `POST /teammate/control`（外部 agent 的开始 / 暂停入口，不需要队友会话令牌）与 `GET /health` 的 `companion` 区块（`api_host` / `api_port` / `process_id` / `auto_play`，不含令牌）。
  - 实机证据：2026-09-13 隔离主机（`--clientId 2026091001`、API `18080`）全程 HTTP 驱动，模型端点指向**死端口** `127.0.0.1:18199` 且 `roleTests` 为空。一次完整跑通：`invite_ai_teammate` 首次调用即 200 `completed`；队友以 `role=companion` 起在 `18081`；进图 20 秒后 `play_running=false`、`play_phase=paused`、`session_requests=0`、`stop_kind=null`；队友 API 暴露 `choose_map_node` 可被外部驱动；`/teammate/control` 暂停 200、未验证时开始 409、非布尔 400。玩家 Steam 真实存档 183 个文件哈希前后一致。脚本与证据：`build/validation-2026-09-13/verify-takeover.ps1`、`takeover-evidence.jsonl`（gitignore）。记录见 `docs/live-validation-checklist.md` 的「External-takeover route (issue #85)」。
  - 该 PR 还修掉三处实机发现：路线标记原先在启动校验**之前**写入，导致一次被拒的重试会把正在自走的队友误标成 `auto_play:false`；`/health` 在队友进程退出后仍继续报其端口；`teammate_control_failed` 文档写可重试、实现对 `retryable:false`。
  - `7a371c7`（PR #99）补齐开局门禁并记录真实外部 agent 验收：队友实例自己的 `POST /session/control {"running":true}` 此前会绕过模型验证直接启动进程内循环（宿主「继续游玩」/`/companion/control`/`/teammate/control` 都拒绝的同一请求它接受）——`SetCompanionRunningAsync` 现在同样过 `FirstRunSetup.ReadyToInvite`，暂停刻意不过门禁；实机确认队友侧 `running:true` 返回 409 `session_not_ready`、`running:false` 返回 200。
  - **真实外部 agent 端到端接管验收通过**：外部 agent（Grok 4.6，自带模型与上下文，只给仓库自带的 MCP 工具面与 play skill，不是进程内循环）在**未配置任何模型**的队友窗口上自己打完一场完整战斗——队友座位 16 次 `play_card` + 6 次 `end_turn` + 4 次 `confirm_modal` 加地图投票，`FUZZY_WURM_CRAWLER` 从 `121/121` 打到死（7 回合），领奖 `REWARD` → `MAP`，金币 `99` → `110`，第二场（`SHRINKER_BEETLE`）已开打时收工；全程队友 `play_running=false`、`play_phase=paused`、`session_requests=0`，日志 84 行机械核查 `/teammate/control`、`/session/control`、`run_console_command` 命中 0 次。证据 `build/validation-2026-09-13/external-agent-log.jsonl` + `external-agent-final-state.json`，驱动 `mcp-call.py`；玩家真档 183 文件哈希不变。
  - 上面三条当轮「记录未改动」的发现，已由 `d826935`（PR #101）全部收尾，见下一条。
  - `d826935`（PR #101）收尾三处实机发现，并修掉核对派生路径时新暴露的两个缺陷：
    - **`NCombatRulesFtue` 三次 `confirm_modal` 本来就是对的**（`NCombatRulesFtue.cs:165` `_totalPages = 3`，第三页之后那一下才 `CloseFtue`），缺的是「非末页返回 `pending` 时说的还是通用过渡文案」，读起来像卡住。现在非末页回 `Tutorial page advanced; the modal is still open. Call confirm_modal again.`（`FtueModalPolicy.IsMultiPageFtue`），其它弹窗文案不变。实机：三次点击依次 `pending` / `pending` / `completed`，第三次之后 `modal` 清空、`screen` 回到 `COMBAT`。
    - **`get_relevant_game_data` 的 `item_ids` 改为可省略**：省略时按当前屏幕从实况状态派生要查的 id（`GameDataFilter.SceneItemSources` ↔ MCP 侧 `_SCENE_ITEM_SOURCES`，两侧键与路径由 `test_scene_field_alignment.py` 钉死相等），场景没有对应内容时回落到牌库 / 遗物 / 药水这些角色级 id。实机走 MCP 工具面：`{"collection":"monsters"}` 正好返回该房间三只敌人、`{"collection":"cards"}` 返回手牌、`relics` 回落出 `BURNING_BLOOD`；显式传 `item_ids` 行为不变，空药水槽返回 `{}` 而不是臆造。
    - **`combat.enemies[].base_max_hp` 新增**：携带缩放前的 `Creature.MonsterMaxHpBeforeModification`，与 `monsters.min_hp` / `max_hp` 同量纲；`max_hp` 仍是缩放后实况值。双人局实机（两个实例数字一致）：TWIG_SLIME_S 元数据 7–11 / `base=9` / 实况 19，LEAF_SLIME_M 32–35 / 33 / 72，LEAF_SLIME_S 11–15 / 13 / 28，即 `base × 人数 × act0 系数 1.1` 取整。单人局是退化情形（`playerCount == 1` 直接跳过缩放），所以原始症状只能在联机局复现。
    - 顺带修掉：屏幕归类到的场景在这块屏上载荷为 `null` 时（`FAKE_MERCHANT` 归为商店却没有 `shop` 载荷）逐段走 JSON 路径会踩进 null 抛异常——路径遍历补上 kind 守卫，且场景侧派生为空时改回落到角色级 id；C# 与 Python 两份镜像对空串 id 的处理也统一（原先 C# 收、Python 跳）。
    - 证据：`build/validation-2026-09-13/verify-round-3.jsonl`、`verify-fixes-3.log`、`verify-base-hp-mp.log`（gitignore）；记录写进 `docs/live-validation-checklist.md`「Those three findings, fixed and re-verified in the game」；`docs/api.md` 补 `base_max_hp` 与可省略的 `item_ids`。C# 382 PASS / 0 FAIL、Python 167 OK、七道闸门全绿、mod 构建 0 警告；玩家真档 183 文件哈希前后一致。
  - 已随 v0.12.2 发布；队友窗口本身没有 overlay（`ModEntry` 对 companion 跳过），这一点已写进文档。

- **v0.12.3 的内容（已随该版发布）**：
  - `b64e7e7`（PR #105）打包 v0.12.2 时被产物检查拦下：README 里 #97 新增的相对链接没进打包改写表，产物中留下一个指向未打包文件的链接。两处 README 的链接改回带 `./` 的形态，并新增第八道离线 gate `packaged-links`——它从 `package-release.ps1` 解析改写表、从 `check_release_package.py` 直接 import 产物清单与链接规则（不复制粘贴，避免清单漂移后 gate 说谎），在源文档上重放打包时的改写再校验剩下的本地链接是否都在产物里。破坏性验证：写回裸链接即报错并点名目标，逐字节还原后转绿。**这一笔只动文档与脚本，不影响已发布的 DLL/PCK。**
  - `0f63d6d`（PR #106，作者 sachi4clover）把 #85 的路线拆分补进游戏内界面：F8 窗口的「邀请 AI 队友」按钮此前调的是只走自动游玩的重载，模型未验证时点它会被旧门禁拒掉，而同一个请求走 `POST /action` 却能拉起队友等待外部接管——**路线拆分到了 API，没到玩家真正会点的那一个按钮**。现在按钮按 `FirstRunSetup.Evaluate(settings).ReadyToInvite` 选路，tab 的两行说明随路线切换，并用源码契约测试 `CoopRoute.OverlayInviteRoute` 钉住（把旧写法还原回去，该测试立刻转红）。**这一笔改了 mod 代码**，所以它没有进 v0.12.2 的 GitHub 产物；已随下面的 v0.12.3 工坊更新发给订阅者。
  - **`b0217b0`（PR #108）把这两笔发成 v0.12.3 首发**：版本号五处升到 `0.12.3`、`uv lock` 重新生成、CHANGELOG 的 `Unreleased` 段定版、`workshop.json` 的 changeNote 更新。工坊于 2026-09-13 22:31 先上传（物品 3796486050，`file_size` 1227269 与本地内容字节和相等、内容 id `5284257893537057643`），同日补上 tag `v0.12.3` 与 GitHub Release（资产 549933 字节，SHA256 `B9DC1A07…`）。见 [v0.12.3 发布记录](history/release-v0.12.3_2026-09-13.md)。
- **v0.12.3 同号重发（2026-09-14）**：
  - `6a327d3`（PR #111，作者 sachi4clover，接 #83 / #84 / #106 的界面部分）把两个此前只存在于 API 的联机入口补进 F8 窗口的「AI 队友」页：邀请按钮**上方**新增 **「禁用自动选角」**勾选框（勾上 = `CompanionAutoSelectCharacter: false`；toggle 即刻写设置，因为这个 tab 没有独立 Save，而队友在启动时读设置），下面的说明行随两种状态切换——不勾时仍是原来的自动选角文案，勾上后变成「第二窗口停在选角界面，让 AI 自己决定，或你切过去替它选完点出发」；邀请按钮**下方**新增 **「继续上次联机对局」**，走与 `continue_ai_teammate` 相同的 runtime 入口（`ContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None)`），只在 host 主菜单且有联机存档时可用——游戏自带的读档按钮走 Steam 联网，认不出本地直连的 NetId，会把那份存档改名成 `.VAL.corrupt`。busy 文案只作用于被按下的那枚按钮，另一枚只是置灰；tab 进入视野时重读 Continue 的可用性。新增源码契约测试 `CoopRoute.OverlayEntries` 钉住接线，两个 README 与 CHANGELOG 的 `## Unreleased` 段同步。
  - **绕开 HTTP 路由不等于绕开保护**（Sourcery 在 #111 上给了一条 blocking finding，判定为误报，已在 PR 下逐条回帖）：`CoopLaunchPolicy.GetError` 在 runtime 入口 `AgentRuntime.cs:804-815` 本来就有同一份判断（与 `GameActionService.cs:5224-5237` 同一调用、同一 `requireVerifiedPlayModel` 推导），两个存档 NetId 预检查也在 `DualInstanceCoordinator.ContinueLocalCoopResultAsync`（`DualInstanceCoordinator.cs:74-94`，注释明写是给不走 HTTP executor 的调用方的 backstop）里重做了一遍，协调器另外自己复核 `MAIN_MENU`（`DualInstanceCoordinator.cs:69`）。
  - 离线证据（head `b42bc5a`）：C# 384 PASS / 0 FAIL；`scripts/preflight-release.ps1` 全绿，八道 gate（api-doc / api-facts / doc-marks / docs-tracked / lockfile / packaged-links / ps1-syntax / script-encoding）逐条 OK；CI contracts 与 Sourcery 均绿；破坏性验证三次都转红（去掉勾选框回写 / Continue 改走非路线重载 / 勾选框语义取反），随后逐字节还原。**这一笔本来改完时还没进任何产物，是 2026-09-14 的同号重发把它带进 `0.12.3` 的**——重发后工坊与 GitHub 两侧都含这两项；该项的实机点击在当天就补上了（见下一条）。

- **v0.12.3 第二次同号重发（2026-09-14，第三次构建）**：
  - `61176f4`（PR #116）修掉实机验收发现的那处缺陷：Continue 按钮的可用性只在 tab 进入视野时刷新，所以面板就停在「AI 队友」页时，启动弹窗期间被正确置灰的按钮在弹窗消失后**一直是灰的**（API 从头到尾都提供该动作），必须切走再切回才恢复。现在 `RefreshContinueAvailability()` 在面板 800ms tick 里、且该页可见时重读，源码契约测试 `CoopRoute.OverlayEntries` 钉住（移除该调用 → 383 PASS / 1 FAIL，已做破坏性验证）。修复在同一台实机复验：弹窗在时亮度 364（灰），弹窗消失 4 秒后 660（亮），全程未切 tab。
  - `6a4b3a0`（PR #117）补 MCP 错误信封与事件流诊断：`get_game_data_*` 失败时保留 `code` / `status_code` / `retryable`（原先被丢掉），`wait_until_actionable` 不再用宽 `except Exception` 把断掉的事件流吞成普通超时（改为捕获 `Sts2ApiError` / `OSError` / `TimeoutError` 并返回 `event_stream_error`），skill 补 `AnyPlayer` 联机需 `target_index`、debug 参考补 `wait_for_event`。
  - `3d2eefb`（PR #118）与 `9cc7114`（PR #119）是发布工具：`packaged-links` 闸门补上破坏性用例（此前八道闸门里只有它没有），preflight 改跑共享的 `check_release_metadata.py` 而不是自带一份漂移副本（原先没有版本格式校验、`pyproject.toml` 按「第一处 `version =`」读），产物检查改为从产物内部读三处版本源（`mod/mod_id.json` / `mcp_server/pyproject.toml` / `mcp_server/uv.lock`）并拒绝不一致（新增 `test_release_artifact_versions.py`，含两个破坏性用例）。
  - `7875c7e`（PR #120）与 `a42bb60`（PR #121）是诊断：事件 / 休息点的 `CanChooseEventOption` / `CanChooseRestOption` 此前用空 catch 返回 false，探测抛异常与「这个房间没有可选项」完全无法区分，动作会从 `available_actions` 里默默消失——现在仍然 fail closed，但异常进日志；`GameActionService.cs` 里另外十二处空 catch（取消链、奖励 drain 三条兜底、`confirm_bundle` 两条按键兜底、`continue_game_over` 三个注入点、Godot 环境变量读取）同样补上带链路名的 `Log.Warn`，返回值与控制流不变。新增 `SurfacedAction.RoomProbesDoNotSwallowFailures` 与 `ActionDiagnostics.NoWordlessRecoveryCatch` 两条源码契约，都做过破坏性验证。
  - `cd5683b`（PR #122）把有界等待契约从「规范化后的行形状」改成读语法树：原先 `await entry.OnTryPurchaseWrapper(...)`（参数表位置要求分号）与 `await completedTask!`（空宽容操作符）都能蒙过去，而且只扫 `GameActionService.cs` 一个文件；现在遍历四个驱动游戏的文件（新增 `DualInstanceCoordinator.cs` / `LocalDualInstanceLauncher.cs` / `AgentOverlayHost.cs`），只接受「指向本 mod 成员」「`Task.` 竞争」「带活 `CancellationToken` 的调用」三类，任务值必须由有界调用产出。破坏性验证两次：`GameActionService.cs` 里加一处裸 await 转红，同一形状放进 `DualInstanceCoordinator.cs` 也转红并点名该文件。
  - `c2630a8`（PR #123）把上面七笔发成第三次构建：版本号五处**一处未动**，CHANGELOG 的 v0.12.3 段改为「republished twice on 2026-09-14」并加了诊断那一节，`workshop.json` 的 changeNote 以 Continue 修复打头，发布说明新写 `notes-v0.12.3-recut-2.md`。
  - 离线证据（head `c2630a8`）：`scripts/preflight-release.ps1` exit 0；C# 386 PASS / 0 FAIL；MCP 单测通过；八道 gate 与 gate 自测全绿；发布元数据一致。三次破坏性验证（Continue tick / 空 catch / 裸 await）都先转红后逐字节还原。


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
| 外部 agent 接管队友窗口（#85） | `d80a19d`（PR #97）+ `7a371c7`（PR #99） | **已随 v0.12.2 发布** | 2026-09-13 隔离实机：死模型端点 + 空 `roleTests` 下 `invite_ai_teammate` 仍 200 `completed`，队友 `auto_play:false`、进图 20 秒 `session_requests=0`，`/teammate/control` 暂停 200 / 未验证开始 409，`/health.companion` 可发现队友 API；队友自身 `/session/control` 过同一道门禁（`running:true` 409 / `false` 200）；外部 agent（Grok 4.6，只给 MCP 工具面）在无模型队友窗口上打完一整场战斗；真档 183 文件哈希不变 | 未在 Steam 双开路径复跑（只在隔离离线主机验过） |
| 三处实机发现收尾：分页 FTUE 文案 / 场景派生元数据 id / 联机基础血量 | `d826935`（PR #101） | **已随 v0.12.2 发布** | 2026-09-13 隔离实机：FTUE 三击 `pending`/`pending`/`completed` 后回 `COMBAT`；MCP 面省略 `item_ids` 按屏返回手牌 / 敌人 / 遗物，显式 id 与空集合行为不变；双人局 `base_max_hp` 9/33/13 对实况 19/72/28（`×2×1.1`），两实例一致；真档 183 文件哈希不变 | FTUE 只在单人档复现（联机档该 FTUE 已完成）；`FAKE_MERCHANT` 的回落只有离线单测（未实机走到该屏） |
| 联机界面入口的实机验收与 Continue 修复（#111 / #116） | `6a327d3`（#111）、`61176f4`（#116） | **已随 v0.12.3 第三次构建发布** | 2026-09-14 隔离实机（`--windowed --force-steam off --clientId 1`，API 18080，联机测试档）：鼠标点击勾选框即刻写 `companionAutoSelectCharacter: false`、说明行切换、队友停在 `CHARACTER_SELECT` 且 70 秒后仍 `is_ready=false`；鼠标点击 Continue 拉起队友（PID 71620 / API 18081），双方到 `MULTIPLAYER_LOAD` → `embark` → `COMBAT`，血量 80/80、金币 113/112、层数 2 与存档吻合；**发现并修掉**面板停在「AI 队友」页时 Continue 长期置灰的缺陷，修复后复验亮度 364（灰）→ 660（亮）且未切 tab | 实机是隔离离线主机（本地直连路径，正是该功能存在的理由），未在 Steam 联网读档路径复跑；环境里一个无关的 2026-05 旧 mod DamageMeter 会在 `OnRunStarted` 抛 `MissingMethodException`，测试期间移出后还原 |

## 4. 待办任务

优先级从高到低。没有明确负责人时记为“未分配”。不要把盘点或收尾做成完整 13 层自然通关。

已完成、移出待办的里程碑（不再单列）：v0.10.5 / v0.10.6 / v0.10.7 / v0.11.0 的发布、安装与工坊更新；90s continue 超时与只点一次；Trellis 纳入版本控制并归档 bootstrap；旧 stash 已 drop；依赖安全 #50/#51；验证闸门与自测；模型兼容矩阵；外部 streamable-http 客户端核查；非法预算安全上限、损坏配置备份恢复、MCP Origin 契约、停流超时契约、play_card 取消、空奖励 overlay、continue_game_over 等待原生结算；外部 agent 接管队友窗口（[issue #85](https://github.com/CharTyr/STS2-Agent/issues/85)，PR #97 合入 main `d80a19d`，2026-09-13 隔离实机验收通过）。原始记录保留在提交历史与 `history/` 下（[v0.11.0 发布记录](history/release-v0.11.0_2026-09-12.md)、[v0.10.7 工坊上传](history/workshop-upload-v0.10.7_2026-09-12.md)、[v0.10.5 订阅加载验收](history/workshop-load-acceptance_2026-09-09.md)）。

### 已归档的标签后变更记录

2026-09-16 至 2026-09-19 的标签后变更清单（v0.12.4 之后、随 v0.13.0 发布的三批）已移至 [history/PRODUCT_PLAN_CURRENT_2026-10-26-archive.md](history/PRODUCT_PLAN_CURRENT_2026-10-26-archive.md)，其中的数字属于原记录时间。

### 查漏补缺扫描的结论（2026-09-18，简报已消化并删除）

一次系统性扫描（反射依赖、动作契约覆盖、依赖安全、文档引用方式、可观测性）的结果。
**A 与 B1 已实施**，其余并入下方后置清单。

**已做：**

1. **上游破坏此前是盲区。** mod 编译期引用 `sts2.dll`，公开 API 改名会让构建立刻红；
   例外是 **23 个按字符串反射的游戏私有成员**（后来又找出 4 个藏在「按参数传方法名」的辅助函数后面，现为 27），它们失效时 `GetField` 返回 null、
   调用点退回默认值——agent 拿到 `max_players: 0`，分不清「大厅零人」和「读不到了」。
   现在启动时逐个解析，`/health` 的 `compatibility` 区块点名缺失项与受影响功能，
   `status` 由它推导而不再写死 `"ready"`。
2. **`abandon_run` 补上行为契约**：它是唯一销毁玩家进度的动作，此前 12 个无契约动作之一。
   钉住的重点是**它在哪停**——只点开确认弹窗，销毁要靠随后的 `confirm_modal`。

**核实注册表花了三轮，值得记下来**：`extraction/decompiled/` 是 2026-03-10 的，
装着的 `sts2.dll` 是 2026-08-16 的。照过期的反编译看，`StartRunLobby._maxPlayers` 像是已经没了、
被公开属性 `MaxPlayers` 取代——**那会是一份关于不存在的 bug 的自信报告**。
直接读装着那份程序集的 ECMA-335 元数据才看清：它在；而 `NUnlockScreen` 的六个字段确实不在该类型上，
因为它们各自在具体子类上（`_relics`→`NUnlockRelicsScreen` 等），读取方遍历六个名字、每屏只命中一个——
**代码是对的，按 `NUnlockScreen` 登记才会让探测连报六次假警**。闸门一旦狼来了就会被关掉。

**刻意不做的**：简报提议改 `ReflectionMemberAccessor.TryGetValue` 以区分「成员不存在」与「值为 null」。
它的四个调用点本来就正确处理了缺失，启动探测已经回答了真问题，
再加一个没人调用的重载是投机代码——这个仓库有体量棘轮。

**已实机验证（2026-09-18，游戏 v0.111.0，隔离实例）**：探测**第一次实机运行就抓到一个真 bug**——
`NEndTurnLongPressBar._longPressDuration` 是**静态**字段，而读它的代码用的是实例绑定标志，
所以自那行写下来起从未读到过真值，一直用写死的 `0.45`，游戏实际是 `0.5`。
离线读元数据得出的「22 个全部存在」对名字而言正确、对问题而言无关：**名字在不在，说明不了绑定标志够不够得着它**。
修复后复验 `status: ready` / 22 检查 / 0 缺失；注入故障后 `/health` 精确点名。
同时两轮大重构（PR #148 / #150）**首次有了实机证据**：主菜单 `/state` 200（v0.12.4 的回归未复现）、
全链路进图开战、`end_turn` 回合推进。详见 [实机清单](docs/live-validation-checklist.md) 的 2026-09-18 条目。

**实机之后又补了一处结构性缺陷**：第一版探测和读取点**各查各的**、各用各的绑定标志，
所以探测能在读取点坏着的时候报 `ready`——把 `_longPressDuration` 的 bug 放回去、注册表保持正确，
**离线 420 条测试和 11 道闸门全绿**。实机那次抓到它有一半是运气：两处犯了同一个错。
现在调用点只能向注册表要成员，一次查找、一组标志，探测报的就是 mod 实际拿到的。
契约也不再只认下划线开头的名字——之前有 5 个方法/属性名从它下面溜过去，
其中 `NEndTurnButton.CanTurnBeEnded`（私有 getter，决定 `end_turn` 是否就绪，缺失时恒答「就绪」）现已登记。

把注册表变成唯一查找源的同时，**挖出 4 处在装着的游戏上从未成功过的反射读取**：
`continue_game_over` 在按钮上找三个处理器名（一个都不存在）、game-over 覆盖层在 `NGameOverScreen` 上
找两个实际属于 `NCombatRoom` / `NRewardsScreen` 的方法。它们都挡在真正干活的代码前面、从未生效，
行为不受影响，已删除。第 4 处不无害——见下条。

**`GET /data/monsters` 的 `moves` 恒为空数组——已修（单独 PR）。** `MonsterModel.MoveNames` 在装着的
`sts2.dll` 里已不存在，导出一直是 `moves: []`。看了旧实现才发现它从来只是一次公开的本地化表查询
（`LocManager.Instance.GetTable("monsters").GetLocStringsWithPrefix(...)`），这三个 API 在装着的版本里
都还是公开的——所以修法不是换一套反射，而是**干脆不用反射**：直接调用，编译器检查，上游再改名构建就红。
实机三轮才修对：第一轮 104/107 有招式但 9 个怪物混入台词、出现重复（同一前缀下还挂着台词 key）→ 只取
`.title`；第二轮重复归零，但千足虫三个分段仍空（它们共用一套文案，key 不是自己的 id）→ 前缀改从怪物自己的
Title key 推出；第三轮 107/107、零重复，其余 104 个与上一轮逐条一致。

**其余 11 个无行为契约的处理函数——已补（`HandlerContract.*`，10 条）。** 三条对全部适用、按表检查：
拒绝所依据的谓词与动作面提供该动作所依据的一致（`crystal_set_tool` 原本内联了一份
`CanPlayCrystalSphere`，改为直接调用）；拒绝是点名动作的 409；`completed` 只能来自观察到的变化。
其余按处理函数钉住最容易被「顺手改掉」的一两处，如群体药水不得指定目标、宝箱遗物的
下标检查与选取必须出自同一个同步器。补契约时顺带修了 `open_character_select` 一处未判空（原来是 500）。

**`/state` 构建耗时——已补。** 计时放在 `BuildStatePayload` 内部而不是 `/state` 路由外面：动作响应与
SSE 刷新构建的是同一份载荷、跑在同一条游戏线程上，路由计时一次也看不到它们。阈值 100 ms 是推出来的
（60 帧下 6 帧），不是测出来的——手头日志只有 9 个 `/state` 样本，定不了分位；`/health` 的
`state_build` 摘要就是今后定阈值的数据来源。

**规格里的行号锚点——已改为按符号引用。** 实数是 41 个（三份规格），替换时逐条核对目标：
`build_parser` 的锚点已漂到另一个函数中间，三条测试链接落在空行上，「client definition」指着
`Sts2ApiError`。`doc-links` 闸门从「只拒绝越界的行号」改为拒绝一切行号锚点，自测补了范围内的用例。

**按名字猜成员的读取——已全部改为按类型。** 对着装着的 `sts2.dll` 逐个核对「候选名单」式读取：
多数名字根本不存在。两处是真缺陷——遗物 `stack` 读不存在的 `Amount`，恒为 null；卡牌修饰词七个
候选名只有 `Keywords` 存在，而它的枚举值被提取成空，于是所有卡都没有关键词/附魔标签（真正的附魔
属性是单数 `Enchantment`，从没在名单里）。其余是死回退。`DuckTypedNames` 现在也要求每个条目仍被使用。

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

1. 只有 PRODUCT_PLAN_CURRENT.md 描述当前状态；history/ 下的文件只做历史证据，不能再发展为第二任务板。
2. 每项能力同时记录实现提交/版本、发布 tag、验收证据日期与来源、剩余缺口。已发布只能来自可核对的 tag/release 证据。
3. 标签后提交必须单列为“主线未发布”，直到出现明确 release tag。
4. 原计划与旧 COOP 原文放在 history/，醒目标注“历史，不代表当前”。
5. README 入口持续指向本页；打包脚本依赖的 ./PRODUCT_PLAN_CURRENT.md 相对目标字面保持不变。
6. 归档目录见 [history/README.md](history/README.md)。旧文档只保留时间点证据，不能重新作为任务板；2026-10-26 起 docs/ 与根目录不再保留只剩跳转的空壳页。
