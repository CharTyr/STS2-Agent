# STS2 AI Agent：当前状态页

> 本页是仓库唯一的当前状态入口。更新日期：2026-09-11（v0.10.6 发布与 Workshop 上传核对）。
> 发布代码基准：v0.10.6 @ 2f75e4a；标签后主线变更单列在下方，不把文档更新视为新版本发布。
> 发布基准：[GitHub Release v0.10.6](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.10.6)，2026-09-11 发布；上一版 [v0.10.5](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.10.5)，2026-09-08。2026-09-11 通过 Steam Web API 核对 Workshop 物品 result=1、visibility=0（公开）、file_size 1083108 与本地内容一致。#81 与 #50/#51 已随 v0.10.6 发布。

旧路线图见 [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md)（历史），旧交付原文见 [history/PRODUCT_PLAN_CURRENT_2026-09-07.md](history/PRODUCT_PLAN_CURRENT_2026-09-07.md) 和 [history/COOP_DELIVERY_2026-09-07.md](history/COOP_DELIVERY_2026-09-07.md)。[COOP_DELIVERY.md](COOP_DELIVERY.md) 现在只是历史证据索引。本页不继承历史文档中的审批、工作树或测试前执行约束。

## 1. 当前基线

- 2026-09-10 主线未发布变更（任务树 `.trellis/tasks/09-10-repo-hardening-5goals`，证据见各子任务 `evidence.md`）：
  - 依赖安全收口：#50 的 `fastmcp` 由 3.1.0 升到 3.4.7（越过后者的修复版本 3.2.0，CVE-2026-32871），#51 的 `fast-uri` 由 3.1.0 升到 3.1.7（CVE-2026-13676 的修复线是 3.1.6）；`npm audit` 由 9 项漏洞（5 high）降为 0，`yauzl` 覆写仍为 3.2.1。
  - 新增离线验证闸门 `scripts/check_verification_gates.py`（依赖安全下限、`docs/api.md` 动作契约、文档快照标记、PowerShell 脚本编码）与自测 `scripts/test-verification-gates.ps1`，已接入 `preflight-release.ps1` 与 CI。`docs/api.md` 现在登记全部 55 个动作。
  - 归档 `docs/sts2-coverage-gaps.md` 至 `history/sts2-coverage-gaps_2026-03-10.md` 并保留跳转页；5 份带日期的验证记录补上历史快照标记。
  - 主动发言与可选语气已实现（默认关闭）。

- v0.10.4 之后合入、已随 v0.10.5 发布的产品提交：
  - 19710ad（#78）空奖励 overlay 不再卡 pending
  - 4b4da6e（#79）continue_game_over 等待原生结算写入，不再 15 秒强行 Enable 返回
  - 22907b0（#80）会话预算、损坏配置恢复、MCP Origin、停流超时、play_card 取消，以及协作验收脚本
- c69d3b3 只忽略本地 mod-uploader.log；374d7fd 只记录 Trellis journal。
- 本轮仓库卫生：90s continue 超时与不重复 Continue 已提交；Trellis spec/skills/platform 文件已纳入版本控制；stash@{0} 已 drop（ai-companion 旧脏树，功能已在 main）；00-bootstrap-guidelines 已归档。.trellis/.template-hashes.json 保持本地、不入库。
- v0.10.5 GitHub ZIP 已发布并安装到 Steam mods/；Workshop 内容已上传，本地订阅清单为 0.10.5。2026-09-09 查询物品详情 result=1、visibility=0（公开）。用户启用重启后，Workshop 加载来源、窗口及运行接口已核对通过，见验收记录。
- 曾未进入 v0.10.5 标签的提交（96bd410 工坊更新默认 public、15e483c/2838250 发布与 P3 记录、4f1ec66 收尾日志）现已随 v0.10.6 发布，见 P2.6。
- 2026-09-11：v0.10.6 已发布（GitHub Release + tag 2f75e4a）；真实 Steam 安装已更新到合并后构建（DLL 47CE0F90）；Workshop 物品 3796486050 已更新到 0.10.6，公开、file_size 1083108 与本地内容一致、标签 Tools & APIs / Utility / QoL 在位。
- 2026-09-11 标签后主线未发布变更（下一个 release tag 之前都按此单列）：
  - `d77982a` 卡牌网格选择元数据改读基类 `NCardGridSelectionScreen`，修复 #82 的升级/变形/附魔选牌屏（隔离副本实测：附魔 1/1/0 → 0/3/0、首次点击 10s 超时 → 151ms；变形 36ms/187ms；升级 173ms 无回归；事件多选 2/2/0 无回归）。
  - `7b02168` 战斗状态暴露自家宠物（`pets[]` / `pet_missing`），紧凑视图同步；Necrobinder 实测奥斯提 1/1 与 `DIE_FOR_YOU_POWER`。同批补测 Defect 球槽（`orbs[]`/`orb_capacity`/`empty_orb_slots`、DUALCAST 后清空）。
  - `03507dc` 修正状态页三处过期口径（停流超时为 10 分钟而非 3 分钟、主动发言实机状态、后置验证项边界）。
  - 2026-09-11 主动发言两处修复：每会话 6 条上限改为随自动游玩会话开始重新发放（此前只有手动「清零本会话统计」能恢复，说完 6 句后功能会永久沉默），75 秒间隔仍是跨会话全局约束；自动游玩开始时重置对局边界，修复「暂停期间开始新一局后，一按开始自动游玩就以 `run_end` 立即停止」。C# 核心测试 217 PASS / 0 FAIL。
  - 2026-09-11 `docs/api.md` 补登此前未记录的路由：`POST /session/control`、`GET /events/stream`、`POST /companion/control` 与 `POST /companion/message`，并补齐 `/health` 的 `stop_kind` 取值表。

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
| 简单选牌屏状态与确认（#81 / #82） | 1b7236a、27fa223；#82 修复 d77982a（主线未发布） | v0.10.6 已含 #81；#82 修复未发布 | 合并构建隔离实机：事件多选报 2/2/0、两次点击 30ms/131ms 完成、原生 chose cards 记录；Sea Glass（0/15 手动确认）暴露 confirm_selection 并 168ms 完成。#82：附魔屏 1/1/0 → 0/3/0、首点 10s 超时 → 151ms、单次确认 166ms；变形屏 36ms/187ms；升级屏 173ms 无回归 | 只有同一台机器上的隔离副本证据；未在正式安装或真实存档上复跑 |
| 战斗自家宠物与球槽状态 | 7b02168（主线未发布） | 未发布 | 隔离实机：Necrobinder `pets[]` 报奥斯提 1/1 与 `DIE_FOR_YOU_POWER`、`pet_missing=false`，紧凑视图同步；Defect `orbs[]`/`orb_capacity=3`/`empty_orb_slots`，DUALCAST 后清空 | 只有 Necrobinder / Defect 两个样本；其它召唤物与充能球类型未逐个采样 |
| 自动游玩停止原因分类 | bd93662（已随 v0.10.6 发布） | 已发布 v0.10.6 | 离线回归：连续 3 次空决策不再报 `run_end`；两条真实边界文案仍归 `run_end` | 分类基于停止文案匹配，不是结构化错误码 |
| 主动发言与交流风格 | a9d4478（v0.10.6）；音量上限与对局边界修复在主线未发布 | v0.10.6 已含基础实现 | 2026-09-10 隔离实机 + 本地桩验证触发、提示词、语气注入、只读 chat 路径、回复消费五项；2026-09-11 再验闸门：开关关闭时 50 次请求 0 条主动发言、战斗开始与结束均触发、9 次发送间隔 88–137 秒、发送后 12 秒的强制转移保持沉默、6 条后第 7 个时刻被拒、暂停再继续后额度恢复。见 [09-10 验收](history/validation-acceptance_2026-09-10.md) 与 [09-11 验收](history/validation-acceptance_2026-09-11.md) | 默认关闭的可选功能；仅战斗开始/结束触发，最多 6 句（每自动游玩会话）、间隔 ≥75 秒（跨会话）。真实模型在真实对局中的发言质量仍未验收 |
| 依赖安全（#50 / #51） | cd55fe1 | 已发布 v0.10.6 | fastmcp 3.4.7、fast-uri 3.1.7；npm audit total 0；MCP 49 项单测通过 | 只覆盖这两条报告与 npm 树，不是完整的第三方审计 |
| 文档契约与验证闸门 | c212594、6aabb4f | 已发布 v0.10.6 | `check_verification_gates.py` 四闸门全绿；自测漂移场景全部被拒；preflight 端到端 exit 0 | 静态检查，不能替代实机行为验证 |

## 4. 待办任务

优先级从高到低。没有明确负责人时记为“未分配”。不要把盘点或收尾做成完整 13 层自然通关。

P0 已完成：90s continue 超时已提交；Trellis 纳入版本控制并归档 bootstrap；收尾日志 4f1ec66 已推送；旧 stash 已 drop。2026-09-09 文档维护将旧规划移入 history/ 并保留兼容入口。

P1 已完成：当前 main 隔离 DLL 双开 GAME_OVER 存档短路径；continue 只点一次且 90s 超时；控制台 fight/die 不能用于双开。P1.3：高级设置「重置本会话统计」见 ui-budget-scrolled3.jpg；请求上限=1 后 health stop_kind=budget、session_requests=1，overlay 显示「已达到会话请求次数上限」和下一步（提高上限 / 重置本会话统计 / 继续游玩）。证据 p13-overlimit-result.json。真实上游超长停流的端到端与真实浏览器页面加载的 Origin 场景仍后置（两者的生产契约、离线回归与本机 loopback/探测已落地，见上表）。

已合进 main、不再当待办：非法预算保留安全上限、损坏配置备份恢复、MCP Origin 契约、停流超时契约、play_card 取消、空奖励 overlay、continue_game_over 等待原生结算。

P2 发布与安装

- P2.1 完成：v0.10.5 已发布，覆盖 #78/#79/#80。版本号四处同步（mod_manifest.json、mod_id.json、Router.cs、pyproject.toml + uv.lock）。
- P2.2 完成：CHANGELOG Unreleased 清理为 v0.10.5 段。
- P2.3 完成：preflight-release 全绿；package-release 生成 sts2-ai-agent-v0.10.5-windows.zip；artifact check 通过。
- P2.4 完成：安装前备份保留在 build/backup-steam-mods-2026-09-08/；v0.10.5 已安装到 Steam mods/（DLL 哈希匹配 release）；旧嵌套目录已备份并清理。
- P2.5 完成（订阅加载冒烟范围）：Steam mods/ 与 Workshop 本地订阅清单均为 v0.10.5；2026-09-09 Workshop result=1、visibility=0（公开）。96bd410 将已有物品更新默认设为 public，首次 ID=0 仍 private，显式 Visibility 优先。用户启用重启后，截图确认窗口显示；本次日志第 40–41 行从 Workshop 目录加载 DLL/PCK，未发现重复加载错误；/health 为 0.10.5、ready，/state 和动作接口为 MAIN_MENU。证据见 [验收记录](history/workshop-load-acceptance_2026-09-09.md)。
- P2.6 完成（v0.10.6 发布）：版本号五处同步（mod_manifest.json、mod_id.json、Router.cs、pyproject.toml、uv.lock），`check_release_metadata.py` 通过；CHANGELOG 的 Unreleased 条目并入 v0.10.6 段；preflight、四闸门、C# 214 PASS / 0 FAIL、MCP 49 项通过；tag v0.10.6 指向发布提交 2f75e4a，GitHub Release 资产 `sts2-ai-agent-v0.10.6-windows.zip` 已上传。
- P2.7 完成（Workshop 更新）：打包工作区 build/steam-workshop/sts2-ai-agent-v0.10.6，用 ModUploader 以 `--id 3796486050` 更新已有物品（不新建）。上传后 Steam Web API 复核 result=1、visibility=0、file_size 1083108 与本地一致、标签在位；工作区已写入 mod_id.txt。操作要点：上传前必须重启 Steam 客户端，否则 SubmitItemUpdate 长期停在 PreparingConfig/PreparingContent——本次两次未重启的尝试都卡死，重启后同一条命令 16 秒完成。该现象与系统代理（本机 127.0.0.1:10808）无关：直连与走代理对 Steam 域名都时通时断，重启后即可成功。

P3 支持范围与卫生

- P3.1 已完成：docs/model-compatibility-matrix.md（工具调用/JSON fallback/SSE usage/超时/401/429/5xx/无 usage，均带测试或源码证据）。
- P3.2 已完成（2026-09-08 结论，2026-09-10 被后续实现取代）：docs/proactive-chat-review.md。原结论为"无主动发言路径"；现在已实现默认关闭的主动发言与可选语气，触发点、音量上限和只读保证见该文档的更新章节。
- P3.3 已完成：外部 mcp 客户端（streamable-http）initialize/list_tools/ping/关闭会话成功；guided profile 10 工具、无 run_console_command（debug 门控生效）。证据 build/validation-2026-09-08/p33-external-mcp-client.json。
- P3.4 已完成既定核查：2026-09-08 记录未发现可合入的 Dependabot PR #50/#51；历史规划引用的是同号 issue，不能据此认定安全报告不存在。锁文件约束与 yauzl/#20 的检查记录见 2838250；该结论不是依赖安全审计，也不代表以后无需更新依赖。

### 剩余事项与证据边界

- Workshop 订阅加载已收口；未额外执行第二轮重启或升级回退，不将这些扩展项目计为已验证。
- 后置验证：真实上游超长停流的端到端、真实浏览器页面加载的 Origin 场景；两者的证据当前止于生产超时契约 + 本机 loopback 回归、MCP Origin 离线契约 + 本机探测。完整自然结束长局不作为此次文档收尾门槛。
- 已知观察：双开控制台 fight/die 会导致不同步；短路径结算中 total_losses 未观察到 +1，不能将 save_verified 扩大为所有统计字段均已验收。
- 产品边界：模型兼容矩阵主要是源码与离线协议证据，不代表所有服务商均经过实机测试。主动发言与可选语气已在 2026-09-10 用本地桩完成隔离实机验收（触发、提示词、语气注入、只读 chat 路径、回复消费五项，见 history/validation-acceptance_2026-09-10.md），但**未用真实模型验收**：真实模型在真实对局中是否在合适时机说出合适的话仍未验证。P3 的"完成"指支持范围核查完成。

## 5. 后续维护规则

1. 只有 PRODUCT_PLAN_CURRENT.md 描述当前状态；COOP 根文件只做历史证据索引，不能再发展为第二任务板。
2. 每项能力同时记录实现提交/版本、发布 tag、验收证据日期与来源、剩余缺口。已发布只能来自可核对的 tag/release 证据。
3. 标签后提交必须单列为“主线未发布”，直到出现明确 release tag。
4. 原计划与旧 COOP 原文放在 history/，醒目标注“历史，不代表当前”。
5. README 入口持续指向本页和 COOP 历史索引；打包脚本依赖的 ./PRODUCT_PLAN_CURRENT.md 与 ./COOP_DELIVERY.md 相对目标字面保持不变。
6. 归档目录见 [history/README.md](history/README.md)。旧文档只保留时间点证据，不能重新作为任务板；原路径跳转页用于兼容引用和预检。
