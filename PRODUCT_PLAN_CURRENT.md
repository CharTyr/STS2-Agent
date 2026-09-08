# STS2 AI Agent：当前状态页

> 本页是仓库唯一的当前状态入口。待办整理时间：2026-09-08（P0/P1.1 收口后更新）。
> 代码基准：本地 main 领先 origin/main（#80 cdd820a）。本轮额外提交：374d7fd journal、508c22d continue 90s/不重复点击、38610d7 Trellis 纳入版本控制，以及 GameOverSave 验收脚本。源码版本仍为 0.10.4。
> 发布基准：最新 release 为 v0.10.4，标签 1c86596。#78 / #79 / #80 已合进 main，尚未打新标签或发版。

旧路线图见 [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md)（历史），旧交付原文见 [history/PRODUCT_PLAN_CURRENT_2026-09-07.md](history/PRODUCT_PLAN_CURRENT_2026-09-07.md) 和 [history/COOP_DELIVERY_2026-09-07.md](history/COOP_DELIVERY_2026-09-07.md)。[COOP_DELIVERY.md](COOP_DELIVERY.md) 现在只是历史证据索引。本页不继承历史文档中的审批、工作树或测试前执行约束。

## 1. 当前基线

- v0.10.4 之后已合进 main、尚未发版的产品提交：
  - 19710ad（#78）空奖励 overlay 不再卡 pending
  - 4b4da6e（#79）continue_game_over 等待原生结算写入，不再 15 秒强行 Enable 返回
  - 22907b0（#80）会话预算、损坏配置恢复、MCP Origin、停流超时、play_card 取消，以及协作验收脚本
- c69d3b3 只忽略本地 mod-uploader.log；374d7fd 只记录 Trellis journal。
- 本轮仓库卫生：90s continue 超时与不重复 Continue 已提交；Trellis spec/skills/platform 文件已纳入版本控制；stash@{0} 已 drop（ai-companion 旧脏树，功能已在 main）；00-bootstrap-guidelines 已归档。.trellis/.template-hashes.json 保持本地、不入库。
- 未创建新 GitHub release，也未把当前 main 装进 Steam mods/。

## 2. 已有验收证据与边界

以下结果来自 2026-09-08 对隔离候选构建的实际执行。证据目录：build/validation-2026-09-08/（已 gitignore，不作发布产物）。

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| C# 核心测试 | 180 PASS，0 FAIL | 离线测试结果，不等于完整 Mod 或自然结束整局通过 |
| 停流超时回归 | loopback 先回 header、正文停流 | 生产默认超时仍是 3 分钟 |
| MCP Origin | 7 项离线测试通过；隔离 Host 实机探测 | 不是完整浏览器页面利用 |
| Mod Release 构建 | 0 warning 0 error；SkipInstall 生成 DLL/PCK | 只复制到隔离 game/mods/，未装 Steam、未打 ZIP |
| 隔离双开 13 层 | 2026-09-08 08:35 打到 13 层原生 GAME_OVER | full-run-result.json。Host progress 聚合当时未更新；不能顶替下面短路径 |
| 隔离双开 GAME_OVER 存档 | 2026-09-08 20:24-20:28，当前 main 隔离 DLL 72C72F02。第一场地图战斗空过团灭，双方 continue_game_over 各一次（4.22s / 2.42s），save_verified=true，双方 progress.save mtime 在 continue 后更新，返回主菜单。forced_return_suspected=false | gameover-save-result.json outcome=gameover_save_ok。控制台 fight/die 会触发多人数据不同步断线，短路径改为自然进战斗 + end_turn 团灭。total_losses 本局未增加（host 仍为 2），score 有更新。 |

候选绑定：隔离 DLL SHA256 72C72F022F7EC32563BCDFA5F3269449DFFF0956A764441E8D7565A8D8AED920；隔离 exe SHA256 8602C26BFFD2937E3841835FD8360EF8E974624A543E05977229FD3D062BE231。Steam mods/STS2AIAgent.dll 仍是更早的 5F86AF22，本轮未覆盖。真档快照未改。

未做完整 preflight-release、发布包/ZIP 验证或 Workshop 安装验证。

## 3. 能力状态矩阵

| 能力 | 实现提交 / 版本 | 发布归属 | 验收证据 | 剩余缺口 |
| --- | --- | --- | --- | --- |
| 游戏内 UI、聊天、自动游玩 | v0.9.0 | 已发布 v0.9.0 | 发布记录 | 完整自然结束仍待实机；短路径结算已做 |
| AI 队友启动隔离与地图投票 | v0.10.0-v0.10.1 | 已发布 | 隔离双开 | 控制台 fight/die 在双开会不同步断线 |
| 原生 MCP | v0.10.2 | 已发布 | Origin 契约 | 外部 MCP 客户端连接/关闭未做 |
| 共享 skill 与投票 | v0.10.3 | 已发布 | 离线测试；隔离实机 | 完整 13 层结算不是当前门槛 |
| FTUE 与时间线解锁 | v0.10.4 | 已发布 | 离线测试；隔离 FTUE | 无 |
| 首次配置与诊断 | v0.10.4 | 已发布 | 隔离邀请成功 | 超限后游戏内文案/按钮未做 UI 手测 |
| 恢复、预算与暂停 | v0.10.4 + #80 | 未进入新标签 | 暂停探测 ok | 超限 UI 手测仍缺 |
| 空奖励 overlay | 19710ad | 未进入标签 | 180 PASS | 未做完整预检、ZIP、Workshop |
| GAME_OVER 等待原生结算 | 4b4da6e（#79） | 未进入标签 | 2026-09-08 20:28 隔离双开 continue 4.22s/2.42s，save_verified，progress mtime 更新 | total_losses 未观察到 +1 |
| 主动发言与交流风格 | 历史待办 | 未分配 | 未完整核查 | 先查代码再定产品边界 |

## 4. 待办任务

优先级从高到低。没有明确负责人时记为“未分配”。不要把盘点或收尾做成完整 13 层自然通关。

P0 已完成：基线页与 main 对齐；90s continue 超时已提交；Trellis 纳入版本控制并归档 bootstrap；journal 随 Trellis 一起待推 origin；旧 stash 已 drop。

P1.1 / P1.2 已完成：当前 main 隔离 DLL 双开 GAME_OVER 存档短路径；continue 只点一次且 90s 超时。控制台 fight/die 不能用于双开（多人数据不同步弹窗）。

仍待办：

- P1.3 高级设置「重置本会话统计」和超限后游戏内文案/按钮的边角手测。overlay 早先已见过损坏恢复与重置按钮，超限文案仍缺一眼。真实上游超长停流、浏览器 Origin 页可后置。

已合进 main、不再当待办：非法预算保留安全上限、损坏配置备份恢复、MCP Origin 契约、停流超时契约、play_card 取消、空奖励 overlay、continue_game_over 等待原生结算。

P2 发布与安装

- P2.1 决定要不要打 0.10.5，覆盖 #78/#79/#80。版本号三处同步：mod_manifest.json、Router.cs、mcp_server/pyproject.toml。
- P2.2 清理 CHANGELOG.md 的 Unreleased。
- P2.3 preflight-release + package-release。
- P2.4 安装、升级、回退可复核。
- P2.5 Steam / Workshop：当前 main 未覆盖 Steam mods/。

P3 支持范围与卫生

- P3.1 OpenAI-compatible 模型兼容矩阵。
- P3.2 核查主动发言、交流风格、低打扰策略。
- P3.3 外部 MCP 客户端连接/关闭。
- P3.4 Dependabot #50 / #51；独立评估。

## 5. 后续维护规则

1. 只有 PRODUCT_PLAN_CURRENT.md 描述当前状态；COOP 根文件只做历史证据索引，不能再发展为第二任务板。
2. 每项能力同时记录实现提交/版本、发布 tag、验收证据日期与来源、剩余缺口。已发布只能来自可核对的 tag/release 证据。
3. 标签后提交必须单列为“主线未发布”，直到出现明确 release tag。
4. 原计划与旧 COOP 原文放在 history/，醒目标注“历史，不代表当前”。
5. README 入口持续指向本页和 COOP 历史索引；打包脚本依赖的 ./PRODUCT_PLAN_CURRENT.md 与 ./COOP_DELIVERY.md 相对目标字面保持不变。
