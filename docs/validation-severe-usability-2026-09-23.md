# 2026-09-23 严重可用性修复：实机验证计划

> **Historical snapshot（历史快照）：这是 2026-09-23 候选的原始验收计划，不是当前版本的通用操作规程，也不是验证报告。** 当日已执行的部分实机结果及失败项单独见 [live-validation-2026-09-23-results.md](live-validation-2026-09-23-results.md)；下文的“待执行”是原计划用语，不代表还没有进行任何实机操作。目标是验收 `dev` 的 `154fcc6`（连同此前的 `628ba75`），不要求打通三幕或花掉一个完整模型会话。原候选离线构建、814 个 C# 测试、420 个 Python 测试与静态闸门此前已通过；这些结果**不能**替代游戏内观察，也不是本轮后续源码修复的实机验收。不要把未触发的稀有场景写成通过。

## 0. 边界、优先级与停止条件

- **P0（必须）**：隔离启动/Mod 身份、截图不含 overlay、地图动画期间动作可用性、单人暂停/排队动作、奖励选卡提示、模型卡测试与 DeepSeek/Kimi 思考工具续轮、队友退出与等待真人选择。实际具备模型与联机前置条件的项目才可判通过；否则记录 *blocked* 与缺少的前置条件。
- **P1（条件触发）**：带重抽遗物的跳过、目标在请求前死亡的联机药水、事件跨幕时的 retryable 503、Jev 的休息/奖励/纪元/商店选项过滤、本地模型的非标准 tool-call 形状。这些依赖特定场景/服务商，可在自然进程中取证；不为制造稀有局面改写真实存档。
- **P2（回归巡检）**：MCP 自定义地址、模型错误展示、长联机状态行、UI 屏幕名、`build-mod.sh --skip-install`（POSIX 离线项，不算 Windows 实机项）、`start-game-session.ps1` 旧端口不释放时拒绝启动（另一个受控进程场景；不要为了测它杀死玩家现有游戏）。
- 遇到非预期真实存档写入、错误角色被操作、动作回包丢失/状态不明、模型异常扣费、游戏闪退或队友无法退出：**先暂停自动游玩，停止继续发动作，保存证据**；不要自动重放 `/action`。不通过的 P0/P1 需修复并重跑相关用例，不能带着它合 `main`。
- 所有有偿调用用测试专用低预算/请求上限，并由操作者确认账户额度和是否可使用真实 Jev/LLM。离线的 LLM 兼容单测不能证明某个服务商的线上协议。敏感证据只保留脱敏摘要，不保存 API key、完整请求体、token、存档或含隐私的原始截图到仓库。

## 1. 准备：隔离、部署、建立基线

1. 确认 `git rev-parse --short HEAD` 与目标一致，工作区状态记入报告。读取 `STS2AIAgent/mod_manifest.json` 版本（本候选为 `0.15.0`）；用构建后部署的 DLL SHA256 和 `/health` 的 `mod_version`、`instance_role`、端口确认**测的是新构建，不是旧 DLL**。
2. **先让现有游戏正常关闭**；`scripts/start-game-session.ps1` 默认会强杀所有 `SlayTheSpire2` 进程，不要在玩家正在玩时直接执行。`build-mod.ps1` 会复制 DLL/PCK 到游戏 mods 目录；先核对目标安装路径及旧文件备份（如需要），避免 Workshop 与本地两个同名 mod 造成错版。部署：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/build-mod.ps1 -Configuration Release
   ```
3. 使用不同于 Steam 正式档的 **新 clientId**，并给 agent 设置独立路径。命令仅作示例，按本机路径调整；`-ExtraArguments` 当前按空格拆分，不支持带空格的单个引号参数。启动脚本会为隔离 clientId 准备 `settings.save`，并固定指定 API 端口；不要用真实 Steam 账号启动带调试动作的测试局。
   ```powershell
   $env:STS2_AGENT_SETTINGS_PATH = (Join-Path (Resolve-Path .).Path 'build/live-2026-09-23/agent-settings.json')
   New-Item -ItemType Directory -Force (Split-Path $env:STS2_AGENT_SETTINGS_PATH) | Out-Null
   powershell -ExecutionPolicy Bypass -File scripts/start-game-session.ps1 `
     -ApiPort 18080 -Attempts 240 -DelaySeconds 1 `
     -ExtraArguments '--windowed --force-steam off --clientId 2026092301'
   $base = 'http://127.0.0.1:18080'
   $h = (Invoke-RestMethod "$base/health" -TimeoutSec 5).data
   $h | Select-Object mod_version,game_version,instance_role,play_phase,stop_kind,companion
   ```
   只在确实需要受控触发事件/弹窗的阶段另行加 `-EnableDebugActions` 启动**独立测试局**；默认阶段保持调试动作关闭。不要把测试密钥放进脚本或日志。
4. 记录正式 Steam profile 的文件清单/摘要（如果要声称“未影响正式存档”，必须前后比较同一清单），确认隔离目录/设置路径不是正式档。建立 `build/live-2026-09-23/` 本地证据目录（`build/` 被忽略），记时间、commit、游戏版本、mod 版本、DLL SHA256、端口、进程 PID、模型名（不记录密钥）、用量上限；对于有多实例，分别记录 host/companion PID、端口、角色。不要将完整 settings、session JSON 或敏感响应提交。
5. 冒烟：`python scripts/run_sts2_validation.py mod-load`、`state-summary`、`state-invariants`，再读 `/health` 和 `/state`。`/health.status` 正常、反射兼容项不缺失、状态屏幕合理；初始 `/state` 与 `/actions/available` 不矛盾。失败则停止，先排查 mod 是否启用、端口是否由旧进程占用。

## 2. 执行矩阵（按前置场景分组，避免无目的打完整局）

每项记录 **before**（屏幕、run_id、相关列表和可用动作）、**操作**（UI/API 与参数）、**response**（HTTP、`status`、`stable` 或错误 code）、**after**（新 `/state` 和 `/health`）、所用模型请求数与判定。先读 `/state` 再执行动作；`pending` 不是失败，等状态稳定后重新读，**响应不明时只读状态，不重发动作**。

| ID / 优先级 | 前置与最短操作 | 通过标准与证据 |
| --- | --- | --- |
| A1 P0 屏幕名/截图 | 主菜单或战斗，显示 overlay；记录 UI 屏幕文字和 `/state.screen`。调用 `GET /vision/screenshot` 保存本地 JPEG，并**人工查看**。 | HTTP 200、JPEG 有效、屏幕名一致且界面没有明显卡顿；截图含游戏画面**不含 overlay**；截图后 overlay 正常恢复。若截图路由循环等待，记录耗时并停测。 |
| A2 P0 地图行进 | 有可选节点的 `MAP`：读取 `map.options` 和 `available_actions`；点一个合法 `choose_map_node`，在动画期快速采集 `/state`（若 API 未及时返回则人工录屏并随后采样）。 | 行进中不再宣称 `choose_map_node` 可用；抵达下个房间后动作集正常且只移动了一次。不要为制造连点在首个动作未确认时再发第二次。 |
| A3 P0 选卡奖励 | 战后有卡牌奖励，调用 `collect_rewards_and_proceed` 或 `resolve_rewards`（不指定卡牌），在选卡屏取响应与新状态。 | 明确表示需选择卡牌，`pending_card_choice=true`（在适用状态载荷），停在奖励屏，未替玩家随意选牌；随后由人选合法卡或跳过，流程可继续。 |
| A4 P1 跳过/重抽 | 自然遇到卡牌奖励带“跳过”和其他 alternative（如重抽），至少一个按钮禁用；确认按钮状态后发 `resolve_rewards` 的跳过请求，参照 `docs/api.md` 的 index 定义。 | 点击**第一个可用 alternative**，而非不可用项；奖励界面/状态有对应变化，无无限 pending。没有对应遗物/禁用按钮则记 blocked，不冒充覆盖。 |
| A5 P0 模型卡测试 | 测试隔离配置里的游玩模型，在 UI 模型卡按“测试”，并查看角色验证记录/邀请按钮；另用一个明确无效的测试端点做失败路径（不触发真实密钥泄露）。 | 成功测试使游玩角色标记 verified、可走自动游玩邀请；失败卡展示原因且不会显示绿色通过。不要为了失败测试覆盖唯一可用设置；结束后恢复测试配置。 |
| A6 P0 思考 + 工具续轮 | 授权真实费用后，用 DeepSeek/Kimi 思考模式低预算开一局/一场战斗，让模型至少完成 **一次调用工具、接收结果、再调用工具**。 | 不在第二轮因 `reasoning_content` 报 400；动作真正改变状态，请求数/Token 记入预算，`/health.stop_kind` 非 config/failed。只出一次工具调用不能算通过。其他 OpenAI 兼容服务商可作对照，但不是替代。 |
| A7 P0 暂停/超时 | 自动游玩且有未完成回合时，从游戏窗口切换出去/最小化，发一次 `POST /session/control` `{"running":false}`（也可恢复窗口后点暂停）；记录 409 `pause_pending` 是否出现及最终阶段。 | 暂停最终变为 `play_phase=paused`、请求数不再增长；恢复窗口后**不会执行暂停时仍在排队的旧动作**。若动作已开始，可正常收尾，不应误判为排队动作。此项要刻意测到“游戏线程不再泵帧”条件；单纯平常暂停只能证明基础 UI。 |
| A8 P0 队友等人 | 用隔离 host 邀请 AI 队友且按配置让其自动游玩；真人在 `MAP` 选路点停留超过 150 s（不启动下一行动），查看 host/companion `/health`。 | 队友仍在线、不会以 `failed` 和“游戏可能卡住”为由停止；真人选路后队友能接续。要核实 `WaitingForPlayer` 确实触发，若测试期间是模型等待/其他房间则不算此项。 |
| A9 P0 host 退出 | **另开一局**邀请队友，记录两个 PID 与端口。先正常关闭 host，观察 companion 是否退出；再在全新受控双开里尝试强杀 host（仅限这两个记录过的测试 PID），观察 watchdog。 | 两种路径 companion 都自行退出，host/companion API 消失，无孤儿进程，也不再消耗模型请求。watchdog 每 3 s 检查一次；留合理游戏关闭时间，异常 15–30 s 仍存活应标失败并由操作者手工善后。不要误杀其他游戏实例。 |
| A10 P1 联机药水目标 | 合法联机战斗，自然遇到目标死亡/目标失效的 `AnyPlayer` 药水，带显式 `target_index` 调用；准备记录双方 HP/状态和药水数量。 | 被指定目标不再合法时返回明确拒绝或可解释错误，**绝不能默默把药水用在本地角色**。若当前局无条件制造，记 blocked；不要在目标健康时误把消耗药水当成测试。 |
| A11 P1 Jev 双层 | 已配置并验证真实 Jev，双层开关开启；分别在有明确 `false` 标志的奖励、休息、纪元、商店状态观察 Jev 决策/选项（可用 `/decision-snapshot` + 决策日志辅助，不要求每种状态都在同一局出现）。 | Jev 不提出 `claimable=false`、`enabled=false`、`actionable=false`、`affordable=false` 或 `stocked=false` 的选项，缺失标志保持兼容；若无明确 false 标志，只能记“未覆盖该分支”。记录请求数和是否回退 LLM，不要把 Jev 不作选择直接记失败。 |
| A12 P1 特定异常 | 事件结束与幕切换恰好重叠时 `proceed`；或兼容服务商返回数值 tool id/对象参数/缺 id、SSE 前置注释。 | 事件转场缺 `NMapScreen` 时给 retryable 503 而非 500；服务商返回的工具调用仍被正确执行。自然未遇到转场窗口/非标准回复则记 blocked，离线单测已经覆盖解析形状，但不是线上实测。 |
| A13 P2 MCP/脚本 | 不启动第二个游戏；保持 18080 在线，用 `STS2_API_BASE_URL=http://127.0.0.1:18080` 运行 network MCP 启动脚本并由独立客户端只读 `health_check`。 | 连的是隔离实例而非默认 8080；读到同一 mod 版本/角色。验证完成关闭 MCP。`start-game-session.ps1` 端口占用拒启另找受控环境测，不能在活跃局强杀所有游戏进程。 |

> `A7` 的低层排队竞争条件、`A10` 的死亡瞬间和 `A12` 的短暂跨幕窗口很难稳定手工制造。只要可重复性不足，就记录准确的观察边界，用现有契约/单测辅证，不得把“没有复现”写为实机通过。

## 3. 回归、收尾和报告格式

- 每个阶段前后执行只读 `/health`、`/state`，留一组聚焦摘要（屏幕、run_id、`available_actions`、play_phase、stop_kind、请求/Token 数、companion PID）。UI 取图要本地脱敏。`GET /vision/screenshot` 自身是截图测试，不可用其验证 overlay 是否可见（它按设计隐藏 overlay），overlay 恢复需直接查看游戏窗口。
- 阶段结束先用 `POST /session/control` 传 `{"running":false}`，确认暂停；双开先从 UI/正常关闭 host，再确认 companion 退出。只有 **A9 的受控崩溃路径**允许对已记录的测试 PID 用 `Stop-Process -Id <pid> -Force`；绝不对 `Get-Process SlayTheSpire2 | Stop-Process` 全量误杀。
- 如果称正式档未动，重复同样的正式 Steam profile 清单/摘要并比对；隔离存档可能因游玩而变化，这是预期。保留 `build/live-2026-09-23/` 本地证据；不要提交 `*.save`、settings、`sessions/`、密钥、原始带隐私截图。
- 如使用调试动作，另列“调试合成”与“自然游玩”的证据，且不要把 `inject_event_churn` 当成自然动作/屏幕转场证明。前一轮 [live-validation-checklist.md](live-validation-checklist.md) 的截图曾**包含** overlay，因此本轮 A1 必须重新拍摄新版图片，不能复用旧证据。

建议在报告逐条填写：

```text
环境：日期/commit/游戏版本/mod 版本/DLL SHA256/隔离 clientId/host 端口/companion 端口（密钥不记录）
ID：A1…A13
状态：PASS | FAIL | BLOCKED | NOT_RUN
前置：所在 screen、run_id、关键标志、模型与预算上限
操作与时间：UI 或 API，必要时 HTTP code、status、stable、error.code、retryable
前后：关键 state/health 差异、请求与 Token 增量、相应的本地证据文件名
判定：与本表哪条标准一致/不一致；失败的复现步骤与首次异常时间
```

只有完成适用的 P0 项、对所有 P1 项明确写出 PASS 或无法触发的原因、并无回归/存档污染时，才可向 `dev → main` PR 给出“候选可合并”的建议；未执行或被阻塞的关键 P0 要显式列为验收缺口，不能用离线结果代替。
