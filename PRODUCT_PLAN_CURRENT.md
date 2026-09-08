# STS2 AI Agent：当前状态页

> 本页是仓库唯一的当前状态入口。更新时间：2026-09-08。
> 代码基准：`main` @ `bb26a21e915ba63c0408c90aacb0c6013f1add7f`；当前工作区有未提交的缺陷修复、验收脚本和文档修改，尚未提交或发版。
> 发布基准：最新 release 为 `v0.10.4`，标签 `1c86596aa28f748a1146fc6f172e5d71319850c4`；源码版本仍为 `0.10.4`。

旧路线图见 [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md)（历史），旧交付原文见 [history/PRODUCT_PLAN_CURRENT_2026-09-07.md](history/PRODUCT_PLAN_CURRENT_2026-09-07.md) 和 [history/COOP_DELIVERY_2026-09-07.md](history/COOP_DELIVERY_2026-09-07.md)。[COOP_DELIVERY.md](COOP_DELIVERY.md) 现在只是历史证据索引。本页不继承历史文档中的审批、工作树或测试前执行约束。

## 1. 当前基线

- `main` 比 `v0.10.4` 标签多 4 个提交。已确认的产品差异是 `19710ad` 空奖励 overlay 修复；`c69d3b3` 只忽略本地 `mod-uploader.log`。两者都没有新的发布标签。
- 当前发布归属止于 `v0.10.4`。标签后的空奖励修复、本轮缺陷修复和验收脚本仍属于主线候选，不能写成已发布能力。
- 本轮未创建 GitHub release，也未把候选装进 Steam `mods/`。

## 2. 已有验收证据与边界

以下结果来自 2026-09-08 对工作区候选构建的实际执行。证据目录：`build/validation-2026-09-08/`（已 gitignore，不作发布产物）。

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| C# 核心测试 | `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`：180 PASS，0 FAIL | 离线测试结果，不等于完整 Mod 或自然结束整局通过 |
| 停流超时回归 | `OpenAI.StalledBodyTimeout` / `StalledBodyUserCancel` 为 loopback 先回 header、正文停流，不是 mock 字符串 | 生产默认超时仍是 3 分钟；测试注入更短超时 |
| MCP Origin | 7 项离线测试通过；隔离 Host 实机：evil Origin 403、同源 200 并回显、无 Origin 200、无 `*` CORS | 不是完整浏览器页面利用 |
| Mod Release 构建 | `dotnet build` 0 warning 0 error；`build-mod.ps1 -SkipInstall` 生成 DLL/PCK | 只复制到隔离 `game/mods/`，未装 Steam、未打 ZIP |
| 隔离双开实机 | 2026-09-08 08:35 候选实机打到 13 层原生 GAME_OVER；暂停探测 request_delta=0；失联探测 companion_unreachable_after_stop | [full-run-result.json](build/validation-2026-09-08/full-run-result.json) outcome=full_run_ok stop=native_end，双方 API save_verified=true。双方 history `1788827793.run` 09:05:48 落地（win=false，13 节点，Mawler）。Host progress.save 聚合字段未更新。 |

候选绑定：HEAD `bb26a21` + 未提交工作区；隔离 DLL SHA256 `03BF7640524C3B14F25288F86BB09EDD8388CF275214E82E188178774A50A8F7`；隔离 exe SHA256 `8602C26BFFD2937E3841835FD8360EF8E974624A543E05977229FD3D062BE231`。Steam `mods/STS2AIAgent.dll` 仍是更早的 `5F86AF22…`，本轮未覆盖。真档快照 196 个文件哈希未变。

未做完整 `preflight-release`、发布包/ZIP 验证或 Workshop 安装验证。180 项测试不能替代自然结束整局。

## 3. 能力状态矩阵

| 能力 | 实现提交 / 版本 | 发布归属 | 验收证据 | 剩余缺口 |
| --- | --- | --- | --- | --- |
| 游戏内 UI、聊天、自动游玩 | `b86e693`；`v0.9.0` tag `f362f73` | 已发布 `v0.9.0` | 发布记录；较早实机记录为历史 | 当前 HEAD 的完整自然结束、结算和存档仍待实机 |
| AI 队友启动隔离与地图投票 | `7f17aa2`、`3eaeada`；`v0.10.0` tag `4620ac1`、`v0.10.1` tag `5501097` | 已发布 `v0.10.0–v0.10.1` | 发布记录；2026-09-08 隔离双开打到战斗/奖励/地图 | 2026-09-08 候选实机双方 API save_status=verified；run history 已写入；Host progress 聚合仍空 |
| 原生 MCP 推荐入口；Python 可选兼容 | `v0.10.2` tag `1be8e83`，发布提交 `2b46530` | 已发布 `v0.10.2` | 发布记录、离线 C# Origin 契约 | 隔离 Host 对本机 `/mcp` 做了 Origin HTTP 探测；外部 MCP 客户端连接/关闭未做 |
| 共享 skill 游玩合同与投票修复 | `690647e`、`43abd8e`、`6afb615`；`v0.10.3` tag `9607f16` | 已发布 `v0.10.3`，发布提交 `2bb6650` | 发布记录、当前离线测试；隔离实机出现地图投票和战斗 | 完整结算/解锁/存档未验收 |
| FTUE、时间线解锁与锁定选项 | `7941435`；`v0.10.4` tag `1c86596`，发布提交 `2157697` | 已发布 `v0.10.4` | 发布记录；离线测试；隔离实机点过 FTUE modal | 当前 HEAD 完整结算/解锁/存档未验收 |
| 首次配置、分角色连通测试与诊断 | 初版归属未细分；git 核实包含于 `v0.10.4` tag `1c86596` | 已发布 `v0.10.4` | 离线测试；隔离设置写入 verified fingerprint 后成功邀请 | 隔离实机 overlay 已看到损坏恢复提示和「重置本会话统计」按钮；隔离 overlay 在请求上限输入非法值后提示保留安全上限 50，文件仍为 tokens=10000 requests=50 |
| 恢复、预算与暂停控制 | 本轮工作区修复 + `v0.10.4` 既有实现 | 未进入新标签 | 离线测试；隔离实机暂停探测 `ok=true, request_delta=0`；token 上限 200000 耗尽后代理停发 | 失联已在隔离实机验证；超限后的游戏内文案/按钮未做 UI 手测 |
| 空奖励 overlay 不再卡 pending | `19710ad`；当前 `main` `bb26a21` | 未进入标签；发布归属未分配 | 离线测试 180 PASS | 未做当前完整预检、ZIP 和 Workshop 验证 |
| 主动发言与交流风格 | 只在历史计划中列为待办 | 未分配 | 本次未完整核查实现 | 先核查实现，再定义产品验收，不计入当前完成 |

## 4. 待办与验收标准

优先级从高到低排列；没有明确负责人时统一记为“归属：未分配”。

### P1：运行、配置与安全缺陷

1. **运行配置缺陷（归属：未分配）**：非法预算、停流超时、损坏恢复、Origin 策略已有失败回归和修复，离线测试绿。重置统计入口已接到真实按钮，隔离 overlay 的 AI 队友页可见「重置本会话统计」。损坏配置启动时 overlay 显示备份恢复提示且不含密钥。隔离 overlay 在请求上限输入非法值后提示保留安全上限 50，文件仍为 tokens=10000 requests=50。浏览器 Origin 页已打开，但 Computer Use 因 URL 策略中止；本会话 fallback 默认关闭了 MCP。MCP 开启时的 Origin HTTP 探测见失联轮。
2. **暂停、失联与预算运行验收（归属：未分配）**：隔离实机已覆盖暂停后 20 秒无新代理请求、token 上限耗尽后代理停发、以及杀掉队友进程后 Host `companion_process_exited=true` 且账本请求数不变。未知 usage 不显示为 0 仍主要是离线契约。

| 位置 | 当前状态 | 剩余验收 |
| --- | --- | --- |
| [`AgentOverlayHost.cs`](STS2AIAgent/Ui/AgentOverlayHost.cs) / [`SessionBudgetLimits.cs`](STS2AIAgent/Config/SessionBudgetLimits.cs) | 非法输入保留安全值并提示；空/`0`=不限。离线测试绿；高级选项复选框实机可见 | 实机输入非法值后上限保持 50 |
| [`OpenAiCompatibleClient.cs`](STS2AIAgent/Llm/OpenAiCompatibleClient.cs) | 独立 `requestTimeout`；用户取消与 408 超时分开。loopback 停流测试绿 | 未对真实上游做超长停流 |
| [`SettingsStore.cs`](STS2AIAgent/Config/SettingsStore.cs) | 损坏先备份；隔离 overlay 显示「配置读取失败，已备份原文件并改用默认配置」 | 无 |
| [`PlayerFacingSession.cs`](STS2AIAgent/Agent/PlayerFacingSession.cs) | 文案指向「重置本会话统计」；隔离 overlay 队友页按钮可见 | 高级设置里的同名按钮未截到 |
| [`NativeMcpServer.cs`](STS2AIAgent/Server/NativeMcpServer.cs) | Origin 只锚定配置 EndpointUrl；隔离 Host 实机 HTTP 探测通过 | Edge 无头从 http://127.0.0.1:19000/ fetch MCP 得到 FETCH_BLOCKED |

### P2：完整旅程与发布验收

1. **隔离双开旅程（2026-09-08 08:35 候选已通过脚本门闩）**：configure → invite → combat/rewards/map → 原生 GAME_OVER → return。证据：full-run-result.json outcome=full_run_ok，pause_probe ok，disconnect companion_unreachable_after_stop，DLL SHA256 `03BF7640524C3B14F25288F86BB09EDD8388CF275214E82E188178774A50A8F7`。双方 history `1788827793.run` 同时写入。已知限制：continue 不再于 15 秒后强行点亮返回按钮，改为等待原生结算写入 progress.save。隔离 0 层 die 已验证 save_verified，且文件在 continue 之后更新。08:35 双开局 Host 聚合字段仍对应该旧 DLL；预算耗尽由同日 100/300 cap 跑次证明，本局 325/600 未触顶。

2. **安装、升级与回退（归属：未分配）**：运行源码包装检查、Release ZIP、专用目录安装、升级备份和回退。验收要求 `mod/` 内容、版本、入口文案、旧文件清理和恢复备份均可复核；Workshop 订阅安装另取实机证据。

### P3：支持范围

- 建立 OpenAI-compatible 模型兼容矩阵：工具调用、JSON fallback、SSE usage、超时、401/429/5xx 和无 usage 响应。本轮 GMI MiniMax 在修正代理 User-Agent 后可完成带工具的战斗请求；6 次 usage 缺失已标未知，不宣称精确 token 硬上限。
- 核查主动发言、交流风格和低打扰策略的实际实现及产品边界；在核查前只保留为历史计划待办。

## 5. 后续维护规则

1. 只有 `PRODUCT_PLAN_CURRENT.md` 描述当前状态；COOP 根文件只做历史证据索引，不能再发展为第二任务板。
2. 每项能力同时记录实现提交/版本、发布 tag、验收证据日期与来源、剩余缺口。`已发布` 只能来自可核对的 tag/release 证据；历史实机、源码审查、契约测试和构建结果不得互相升格。
3. 标签后提交必须单列为“主线未发布”，直到出现明确 release tag；版本号不能单独证明发布归属。负责人未被明确指定时写“未分配”。
4. 原计划与旧 COOP 原文放在 `history/`，醒目标注“历史，不代表当前”；只修复搬迁造成的相对链接，不把旧审批或工作树条件带回当前页。
5. README 的入口标签持续指向本页和 COOP 历史索引；如调整描述，保留打包脚本依赖的 `./PRODUCT_PLAN_CURRENT.md` 与 `./COOP_DELIVERY.md` 相对目标字面。
