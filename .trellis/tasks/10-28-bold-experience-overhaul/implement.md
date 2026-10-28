# 大胆改造：执行计划（implement.md）

## 0. 恢复工作指南（上下文丢失后先读这里，按顺序做）

1. 读 `prd.md`（尤其 §1 用户决定）→ 本文件 §3 找**第一个未勾选项** → `design.md` 对应章节 → 只读该步要改的源码。调研细节在 `research/*.md`（autoplay-pipeline / speed-and-performance / decision-quality / overlay-ux-and-hud / reliability-coop-mcp / landscape）。
2. 确认分支与进度：
   ```powershell
   git status --short; git branch --show-current; git log --oneline dev..HEAD
   git log --oneline --grep "\[W3\]"   # 查某个工作流做到哪
   ```
3. 每个新 shell 先设：`$env:UV_CACHE_DIR = "C:\Users\chart\Documents\project\sp\.uv-cache"`
4. 规则速记（违反即返工）：
   - 子智能体只能 `provider=magpie`、`model=clinepass/cline-pass/deepseek-v4.1-flash-1017`（别写 `magpie/clinepass/...`）。
   - 模型请求总额 ≤ 300（含探测与实机），每次花之前/之后更新 §6 台账。
   - 不发版、不改五处版本号、不碰 main；验证通过后 PR 合并进 dev（用户已授权）。
   - 新功能默认开启 + 设置可关。
   - 密钥只在 `build/live-config/endpoint.local.json`（不入库），不得出现在提交、日志、报告。
   - 不碰 `stash@{0}` 与 `.trellis/tasks/10-27-live-dev-validation/`。
   - 部署 DLL 前确认游戏已关闭；不关闭用户自己开的游戏（先问）。
   - 行数棘轮：新逻辑放新文件；`AgentLoop.cs` 0 余量、`server.py` 1 行余量。
   - 新文件 → project-map；游戏依赖 → KnownUncompiledSources；纯逻辑 → 测试工程 Compile Include + `TestRunner.cs` 注册；新字段 → `docs/api.md`；新中文 → 英文表。
5. 每完成一项：跑该项验证 → 勾选 → 提交（信息含 `[Wn]`）→ 设计有变就在 §7 记一行。
6. 若 §3 的某项因游戏版本/实机结果需要改方案：先改 `design.md`，再记决策日志，再改代码。

## 1. 验证命令（原样复制）

```powershell
# C# 构建（引用游戏程序集；游戏开着也能编译）
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release 2>&1 | Select-Object -Last 5

# C# 离线测试（沙箱内有 9 个已知环境性失败，见 design §1.1；只关心其它失败）
$out = dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release 2>&1
$out | Select-String '^FAIL' -Context 0,3
"PASS " + ($out | Select-String '^PASS').Count + " / FAIL " + ($out | Select-String '^FAIL').Count

# Python 测试
Push-Location mcp_server; uv run --locked python -m unittest discover -s tests 2>&1 | Select-Object -Last 6; Pop-Location

# 静态闸门 / 决策基准结构
python scripts/check_verification_gates.py
python scripts/decision_benchmark.py

# 核对装着的游戏程序集里的成员（新用到的游戏成员都要核）
ilspycmd -t <类型全名> "c:\program files (x86)\steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll"

# 合并前：提升权限复跑 C# 测试一次，确认 9 个环境性失败也通过（需用户批准）
```

## 2. 里程碑（依赖顺序；T1 必做、T2 应做、T3 可做）

| 里程碑 | 内容 | Tier | 依赖 |
| --- | --- | --- | --- |
| M0 | 地基 W0：分支、计划入库、基线、AgentLoop 腾位、设置项 | T1 | — |
| M1 | 性能地基 W9.0–9.2 + W6/§9.4：探针、回合轻量快照、一次读取可操作状态、读取期限 | T1 | M0 |
| M2 | 省钱核心 W6：计量 → 流式降级规则 → 游戏内工具清单 → 末句 → PromptStateDiet → 视觉按需 → 缓存命中 | T1 | M1 |
| M3 | 加速 W1：TurboPolicy/Controller、写盘守卫、崩溃恢复、UI | T1 | M0 |
| M4 | 强制动作 W2：AutopilotPolicy + 接入 + 回执来源 | T1 | M1 |
| M5 | 整回合规划 + 战斗算术 W3 + W4.1/4.2（AgentViewVersion 13） | T1 | M2、M4 |
| M6 | 自愈 W7.0–7.5、7.7（T2：7.6 输出上限、7.8 轨迹、7.9 乱码） | T1/T2 | M2 |
| M7 | 观看体验 W8.1–8.6（T2：8.7、8.8） | T1/T2 | M5 |
| M8 | 策略大脑 W5.1–5.3 + W4.3 Jev 数字 + W4.4 基准 v2（T3：W5.4 角色页） | T2 | M5 |
| M9 | MCP 两面 + 联机补强 W10 + W9 其余 | T2 | M5 |
| M10 | 文档 W11 + 全量离线验证 + Release 构建 | T1 | 以上 |
| M11 | 实机验证（预算内）+ 修复 | T1 | M10 |
| M12 | PR → CI → 合并 dev → spec/journal 收尾 → 向用户汇报 | T1 | M11 |

每个里程碑结束：离线测试 + 闸门全绿才进下一个。时间/预算紧张时按 Tier 砍：先砍 T3，再砍 T2 中靠后的项，T1 不砍。

## 3. 执行清单

### M0 地基（W0）
- [x] 0.1 `git switch -c feature/bold-overhaul`（基于 dev@89d5fd3）；提交本任务目录：`docs(trellis): plan the bold experience overhaul [W0]`。
- [x] 0.2 跑 §1 全部命令，结果记到 §7（已知基线：C# 1023/9 环境性失败，Python 439 OK）。
- [x] 0.3 AgentLoop 腾位（design §3.2）→ `AgentLoop.RoleProbes.cs`；测试工程 Compile Include；核对 5 个源码契约；project-map。提交 `refactor(agent): move role probes out of AgentLoop.cs [W0]`。
- [x] 0.4 设置项（design §3.4 全表 + `OverlayVisibleOnStart` 新安装默认 true）：`AgentSettings`、`SettingsClone`、`EnsureValidShape`、`CompanionPlaySettings`/`CompanionSettingsPatch`（同步项）；测试：往返、非默认克隆、非法修复。UI 控件随各工作流加。提交 `feat(config): switches for the overhaul, on by default [W0]`。

### M1 性能地基（W9.0–9.2、§9.4）
- [x] 1.1 `GameStateService.Probe.cs`：`ProbeActionable()`（直接复用已是共享函数的 `EvaluateCombatActionGate`，无需另抽 `AdvanceCombatActionGate`），`BuildTurnProbe()`；KnownUncompiledSources、project-map。
- [x] 1.2 `GameBridge.WaitUntilActionableAsync`：每帧探针、只在判定可操作的那一帧（或超时）构建一次；`StateBuildTiming` 加 `actionable_probes/actionable_builds/event_poll_builds`（纯逻辑测试）；`/health.state_build` 文档行。
- [x] 1.3 `IGameBridge.WaitUntilActionableWithStateAsync`（接口默认实现＝旧的「等→读」，测试假桥无需改）；`GameBridge` 覆盖实现（同一游戏线程单元内探针+构建；所有状态读取带 `StateReadBudget` 期限）；`PlayOnceAsync` 合并「读→等→读」；`AgentLoopTests` 断言读取次数。
- [x] 1.4 `AutoPlayLoopAsync` 改用 `BuildTurnProbe()`（同源同帧字段，不补整份构建，见 §7）；`ActionabilityProbeContractTests` 钉住。
- [x] 1.5 验证（C# 1037/9 环境性、闸门全绿、Release 0 警告）+ 提交 `perf(agent): probe actionability instead of building /state every frame [W9]`。

### M2 省钱核心（W6）
- [x] 2.1 计量：决策记录 + `AgentTurnResult` 增加 `tool_rounds`、`read_tool_rounds`、`stream_demoted`（additive，`docs/api.md`、`DecisionLogTests`）。
- [x] 2.2 `CompleteWithToolsAsync`：流式无工具调用 → 仅回复全空时降级；有正文先 `ActJsonParser`，解析不出追加「You must call act now」再来一轮（`AgentLoopTests` 覆盖三种情况与请求计数）。
- [x] 2.3 `AgentTools.PlayInGame`（act + 元数据查询 + wait_until_actionable）；`PlayWithModelAsync` 用它；`AgentTools.Mcp` 不变；处理 `McpPlayerSkillTests.SkillTracksLivePlayContract`（读测试，必要时有意更新并写原因）；末句改为「状态已最新，不要 get_game_state，现在 act（战斗用 then）」。
- [x] 2.4 `PromptStateDiet.cs` + 测试（每条规则、合法 JSON、关键键保留）；`PlayWithModelAsync` 与 Jev 请求使用；`card_value_note` 原文移入静态前缀。
- [x] 2.5 `VisionPolicy.cs` + 测试；`PlayWithModelAsync` 按策略截图；设置页「截图」三选一 + Loc。
- [x] 2.6 `LlmUsage.CachedPromptTokens`（流式/非流式解析）+ 会话统计显示 + `/health.session` 字段 + 文档。
- [x] 2.7 验证 + 提交（可拆）：`feat(agent): one model request per decision [W6]`、`feat(agent): leaner prompts and screenshots on demand [W6]`。

### M3 加速（W1）
- [x] 3.1 `TurboPolicy.cs` + `TurboPolicyTests`。
- [x] 3.2 `TurboController.cs`（Engage/Release/RecoverAtStartup/InstallPrefsGuard，Harmony 守卫挂 `PrefsSaveManager.SavePrefs`；先 ilspycmd 核对 `PrefsSaveManager` 与 `NetService.Type`、`IsMultiplayer` 成员名）。
- [x] 3.3 接线：`AutoPlayLoopAsync` 开头 Engage、外层 finally Release；`ModEntry` 启动恢复/关停尽力恢复/安装守卫；`/health.turbo`；源码契约测试。
- [x] 3.4 UI：设置页「游戏速度」分段开关 + HelpCard；播放页档位药丸；SpeedX 提示；布局契约；Loc。
- [x] 3.5 验证 + 提交 `feat(turbo): the game's own fast mode while the AI plays, restored after [W1]`。

### M4 强制动作（W2）
- [x] 4.1 `AutopilotPolicy.cs` + `AutopilotPolicyTests`（design §5.1 每条正反例 + 永不自动清单）。
- [x] 4.2 `AgentTurnResult.Source`；`AgentLoop.Autopilot.cs`；`PlayOnceAsync` 接入（Sandpit 后、Jev 前）；门（开关/非战斗/排队指令）。
- [x] 4.3 `RecordTurnReceipt` 用 Source；`PlayerLabels.Source("autopilot")`；`DecisionLogView`；`docs/api.md`；`AgentLoopTests`（零请求、来源、让位、开关、重复仍会停）；设置页开关。
- [x] 4.4 验证 + 提交 `feat(agent): forced moves without asking the model [W2]`。

### M5 整回合规划 + 战斗算术（W3 + W4.1/4.2）
- [x] 5.1 核对 `NetCombatCardDb`（已核）与卡牌类型枚举成员；原始 `combat.hand[].combat_card_id/card_type`，compact `cid/type`；`AgentViewVersion` 13；`docs/api.md` + 重命名表。
- [~] 5.2 `CombatThreatPolicy.cs`（纯，`CombatRisks` 改为读它）+ `CombatMathPolicy.cs`（纯：threat 汇总、kill_options、max_block）+ 测试；`GameStateService.CombatMath.cs`（按目标预览矩阵 + 缓存）；compact `combat.threat`、`hand[].vs`；`combatMathEnabled` 门；载荷声明（先量 `Payloads.cs` 余量，design §7.2）；文档。
- [ ] 5.3 `TurnPlanPolicy.cs` + `TurnPlanPolicyTests`；共享向量 `mcp_server/tests/fixtures/turn_plan_vectors.json` + `TurnPlanVectorTests`。
- [ ] 5.4 `AgentTools.ActParameters` 增 `card_cid`/`then`；`AgentLoop.TurnPlan.cs`；`CompleteWithToolsAsync` 接入；`ExecuteActAsync` 支持 `card_cid`；回执 `PlanSteps/PlanStopReason`；决策记录 `turn_id/plan_index/plan_size`（只第一行带费用）；`turnPlansEnabled` 门（关时 `then` 被忽略并在结果说明）。
- [ ] 5.5 提示词：`SKILL.md` 共享契约 + `screen-playbooks.md` 战斗段 + `PlayPrompt` 静态前缀（算术与计划说明）+ `scene_guidance.py` 镜像；`PlaybookSectionsTests`/`McpPlayerSkillTests`。
- [ ] 5.6 `AgentLoopTests`：三步计划 1 次请求；抽新牌停；目标死停；主动作被拒不执行 then；暂停中途取消；cid 解析；开关关闭。
- [ ] 5.7 验证 + 提交 `feat(state): stable card ids, card types and combat math [W3][W4]`、`feat(agent): plan a whole combat turn in one request [W3]`。

### M6 自愈（W7）
- [ ] 6.1 `FailureKind`（产生处赋值）+ `AutoPlayRecovery.Observe` 按类处理 + 证据时钟 + 游戏线程超时进等待；`StopKindPolicy` 优先用 kind；`AutoPlayRecoveryTests`。
- [ ] 6.2 能力探测（`AgentRuntime.ModelTest.cs` 加流式探针，测试时 ≤1 次额外请求）+ `providerCapabilities` 记录 + 按能力选流式 + 运行时学习写回；模型卡片显示；测试用注入 handler。
- [ ] 6.3 正文 JSON 动作在有工具能力时也执行；`ActIndexValidator.ResolveUniqueIndex` + 测试。
- [ ] 6.4 停机文案「发生了什么 + 你可以做什么」；`PlayerFacingSession` 下一步；Loc。
- [ ] 6.5（T2）输出上限 `maxOutputTokens`（先用本地网关实测字段兼容，记台账）。
- [ ] 6.6（T2）回合轨迹环形缓冲 + `/diagnostics.recent_turns` + 悬浮窗「为什么停了」。
- [ ] 6.7 验证 + 提交 `feat(agent): classify failures and heal instead of stopping [W7]`、`feat(llm): remember what a provider can do [W7]`。

### M7 观看体验（W8）
- [ ] 7.1 面板消失修复（design §8.1）+ 边缘标签色点 + 收起提示行；测试（`PlayerExperienceTests` 等）。
- [ ] 7.2 标题行与决策记录走 `PlayerLabels`；有意更新 `OverlayTabContractTests` 两处断言。
- [ ] 7.3 `StallHintPolicy.cs` + 测试 + 接入 `_soloNext`。
- [ ] 7.4 `LiveIntent.cs` + `HudModel.cs`（纯）+ 测试；`AgentLoop` 的 `reportIntent`（Jev/LLM/自动处理/计划步执行前）；`AgentRuntime` 持有与清除规则。
- [ ] 7.5 `HudAnchors.cs`（游戏依赖）+ `AgentHud.cs`（CanvasLayer 120、Ignore 鼠标）：H1/H2/H3/H5；截图隐藏（`HideForCapture`/`OverlayVisibleProbe` 规则）；`hudMode`；project-map、KnownUncompiledSources。
- [ ] 7.6 `RunSummary.cs`（纯）+ `AgentOverlayHost.RunSummary.cs` 成绩单卡片 + 复制 + `/health.last_run_summary`。
- [ ] 7.7 暂停热键 `pauseHotkey`（先确认 F7 未被游戏占用）+ HUD 横幅文案。
- [ ] 7.8（T2）H4 地图脉冲；决策记录「为什么」展开；术语与公开文档对齐。
- [ ] 7.9 验证（含 `OverlayLayoutContractTests`、宽度规则）+ 提交 `feat(ui): show what the AI is doing on the game screen [W8]`、`feat(ui): run report card and clearer status [W8]`。

### M8 策略大脑（W5、W4.3、W4.4）— T2
- [ ] 8.1 reward/shop 的 `rarity/type/price` + `run.deck_stats`（纯 `DeckStatsPolicy.cs`）+ 文档。
- [ ] 8.2 `MapRoutePolicy.cs` + 测试 + compact `map.route_advice`。
- [ ] 8.3 `RunJournalStore.cs` + 测试；局末写入；静态前缀注入事实摘要；设置页清空按钮。
- [ ] 8.4 Jev 选项文本加数字（`JevOptionEnumerator` 纯输入）+ `JevAgentLogicTests`。
- [ ] 8.5 基准 v2（`docs/decision-benchmark.json` schema 2 + 算术用例 + 本地计算器对照）+ `scripts/test-decision-benchmark.py`。
- [ ] 8.6（T3）角色策略页。
- [ ] 8.7 验证 + 提交 `feat(agent): deck stats, route advice and a factual run journal [W5]`、`feat(jev): numbers in fast-judgment options [W4]`、`test(benchmark): math cases [W4]`。

### M9 MCP 与联机（W10、W9 其余）— T2
- [ ] 9.1 原生 MCP `act` 支持 `card_cid/then`（同一 `TurnPlanPolicy`）+ `McpServiceTests`。
- [ ] 9.2 Python：`play_tools.py`（迁出 act）+ `turn_plan.py` + 向量测试 + 对齐测试；行数检查。
- [ ] 9.3 skill 模式 A/B 文案、删除「一直循环」；`mcp_server/README.md` 注明。
- [ ] 9.4 联机：邀请预检、队友接口自检文案、消息状态。
- [ ] 9.5 W9 其余：事件流自适应（`EventPollingCadence.cs`）、悬浮窗刷新省略、加速下等待地板（哈希契约按规矩）。
- [ ] 9.6 验证 + 提交 `feat(mcp): whole-turn plans on both MCP surfaces [W10]`、`feat(coop): explain why an invite cannot start [W10]`、`perf: adaptive event polling and a quieter overlay [W9]`。

### M10 文档与全量离线验证（W11）
- [ ] 10.1 project-map、api.md、openapi.json（`scripts/api_schema.py`）、CHANGELOG Unreleased、两份 README、`.trellis/spec/mod/*.md`。
- [ ] 10.2 §1 全部绿；Release 构建 0 警告；提升权限复跑 C# 全绿一次。
- [ ] 10.3 提交 `docs: document the overhaul [W11]`。

### M11 实机验证（预算：总 ≤ 300，已用 4；本阶段计划 ≤ 220，留 ≥ 70 机动）
- [ ] 11.1 准备：`Get-Process SlayTheSpire2` 确认未运行；备份 `mods/STS2AIAgent.*` 与 `mod_id.json` 到 `build/live-2026-10-28-overhaul/backup/`（含 SHA256）；构建部署（写游戏目录需提升权限，向用户申请一次）；测试设置 `build/live-2026-10-28-overhaul/settings.test.json`（本地网关、`workbuddy-ai/deepseek-v4.1-flash`、会话请求上限 120），`$env:STS2_AGENT_SETTINGS_PATH` 指向它；第三方 Mod 干扰时临时移出并**事后还原**。
- [ ] 11.2 启动：`scripts/start-game-session.ps1`（隔离 `--clientId 2026102801`）→ `/health` ready → 记 DLL SHA256、`/health.compatibility`。
- [ ] 11.3 基线对照（新开关全关，≤ 40 请求）：同角色打 1–2 场普通战斗，记墙钟、请求数、token、`state_build`。
- [ ] 11.4 新版（全开，≤ 150 请求）观察清单，证据存 `build/live-.../`：
  - W1：`/health.turbo.applied=true`、动画明显加速；暂停后游戏设置 Fast Mode 与原值一致；退出重开仍一致。
  - W2：强制动作 `source=autopilot`、0 请求（结束回合/单路地图/奖励收尾/宝箱/新手弹窗）。
  - W3：≥ 3 个回合用 `then`；记录每回合请求数；至少 1 次偏离停止并正确重新决策。
  - W4：`combat.threat`、`hand[].vs` 与画面数字一致（抽查 3 牌 × 2 敌）。
  - W6：每决策请求数（目标 ≈1.0–1.2）、截图次数、缓存命中率有值。
  - W7：一次 config 错误立停且文案正确（临时改错模型名，0 额外请求）。
  - W8：HUD 横幅/高亮/连线/气泡可见；`/screenshot` JPEG 不含 HUD 与面板；成绩单在局末出现（预算内打不到局末就用失败结束一局）；F7 暂停可用。
  - W9：`actionable_builds` 远小于基线；主观无掉帧。
- [ ] 11.5 修复实机问题：复现证据 → 离线回归测试 → 修 → 实机复验（预算内）。
- [ ] 11.6 收尾：恢复 `mods/` 原文件（核 SHA256），清理测试进程与临时设置；写 `build/live-2026-10-28-overhaul/report.md` 与 `docs/live-validation-checklist.md` 入库摘要（如实区分已观察/仅离线/未验证）。

### M12 合并与收尾
- [ ] 12.1 `git push -u origin feature/bold-overhaul`；`gh pr create --base dev`（中文摘要 + 证据 + 未验证项）；等 CI `contracts` 绿。
- [ ] 12.2 合并 PR 进 dev；本地 dev 快进；不碰 main。
- [ ] 12.3 `trellis-update-spec`；任务归档与 journal（按 Trellis 流程）。
- [ ] 12.4 用通俗语言向用户汇报（做了什么、怎么用、哪些实机验证过、哪些没有、是否发版由用户决定）。

## 4. 回滚点
- 每个里程碑独立提交，可 `git revert`。
- 运行时：关闭对应设置（design §15）。
- 实机部署：`build/live-2026-10-28-overhaul/backup/` 原文件拷回 `mods/`。

## 5. 风险登记
| 风险 | 信号 | 应对 |
| --- | --- | --- |
| `Instant` 某屏等待误判 | 实机连续 `pending` | 默认改 `fast`，记录屏幕名 |
| 装着的 dll 与反编译不一致 | 构建失败/反射缺失 | `ilspycmd -t` 核对，按装着的改 |
| 算术拖慢构建 | `state_build` p95 上升 | 缓存键检查/只在玩家回合算/降级 |
| 模型不用 `then` | 实机每回合仍多请求 | 调提示词与战斗 playbook 示例 |
| 提示词瘦身降低质量 | 实机明显乱打 | 逐条回退 diet 规则（每条独立可开关的常量） |
| 文件超预算 | `SourceShape` 失败 | 先移出代码到新文件，不抬预算 |
| 请求预算不足 | 台账接近 300 | 停非关键实机项，报告未验证项 |
| 契约测试冲突 | 哈希/文本契约失败 | 按 remarks 规矩有意更新并写原因 |

## 6. 模型请求台账（上限 300）
| 时间 | 用途 | 请求数 | 累计 |
| --- | --- | --- | --- |
| 2026-10-28 18:40 | 本地网关工具调用探测（2 模型 × 流式/非流式） | 4 | 4 |

## 7. 决策日志
| 时间 | 决定 | 原因 |
| --- | --- | --- |
| 10-28 18:35 | 用户：实机允许 300 请求；验证后直接合并 dev；先不发版；新功能默认开启 | prd §1 |
| 10-28 18:39 | 实机用本地网关 `http://127.0.0.1:3425`（密钥与模型在 `build/live-config/endpoint.local.json`） | 用户提供；原 CommandCode 端点可能不可用 |
| 10-28 18:45 | 加速用官方 `PrefsSave.FastMode` + 写盘守卫，不做 SpeedX 式时长缩放 | 官方路径；`GodotTimerTask` 是联机超时；本机装有 SpeedX 会相乘 |
| 10-28 19:05 | 写盘守卫挂内层 `PrefsSaveManager.SavePrefs()` | 外层 `SavePrefsFile` 只是转调，内层还有直接调用（ilspycmd 已核） |
| 10-28 19:05 | 整回合规划用 `act` + `then` + `card_cid`（`NetCombatCardDb` 稳定 id），不另设 `act_plan` 工具 | 一个工具更少 schema；cid 精确定位；MCP 两面同参 |
| 10-28 19:05 | 保留 MCP 侧 `then`（逐步校验、非 completed 即停、逐步结果）；不新增 `step` 工具，改 skill 文案为模式 A/B | SpireBench 实证批量有益；reliability 报告担忧逐条化解；减少两面对齐成本 |
| 10-28 19:05 | 提示词瘦身只作用于游戏内提示词，API 的 compact 视图不删字段 | 向后兼容规则；省钱主要发生在游戏内模型 |
| 10-28 19:05 | 战斗算术：`combat.threat` 常开 + `hand[].vs` 带缓存放 compact（不另开 `/combat-plan` 路由） | 两面 MCP 自动获得、无需新工具；缓存控制成本 |
| 10-28 19:05 | 跨局记录只给事实摘要，不让模型写「教训」 | SpireBench：模型自写记忆 17% 错误且过度保守 |
| 10-28 19:05 | F8 是开关面板不是暂停；新增可配置暂停热键（默认 F7，需先确认未被占用） | UX 报告指出原设想文案错误 |
| 10-28 | 基线：C# 1023 PASS / 9 环境性 FAIL（沙箱），Python 439 OK | design §1.1 |
| 10-28 23:40 | （会话中断后恢复）M1：不抽 `AdvanceCombatActionGate`；不做「探针不可用时每 4 帧构建」；`run_id` 变化不补整份构建 | `EvaluateCombatActionGate` 本就是构建与探针共用的唯一推进点；探针抛异常时与旧构建一样向上抛，回退路径无收益；`BuildTurnProbe` 与整份构建读同一来源、同一游戏线程单元，已是会话规则要求的「新鲜帧」 |
| 10-28 23:40 | 用户：子智能体可用 magpie `clinepass/cline-pass/deepseek-v4.1-flash-1017` 做具体实现，「合理使用，但不要依赖」 | 主线关键改动自己写，子智能体只做边界清楚的独立块并由我复核 |
| 10-29 00:30 | 进度（commit 8fc8ac9 / 6302c2b / 623ad9a）：M2、M3、M4 全部；M5.1 完成；M5.2 用 `CombatMathPolicy` + `GameStateService.CombatMath.cs` 完成 threat/vs/kill_options/max_block，**未**把 `CombatRisks` 抽成 `CombatThreatPolicy`（改为 `lethal = threat.Lethal ‖ end_turn_will_kill_player`，二者不会矛盾；自身伤害三项与 CombatRisks 同源）。播放页档位药丸未做（HUD 横幅时补） | 少动有哈希契约的 `BuildCombatLethalRiskPayloads` |
| 10-29 00:30 | `vs` 段数只在可确定时给出：`CalculatedHits`/`Repeat`/反编译确认写死两段的 8 张牌（按类名）；出牌时才计算段数的牌 hits=null | `TwinStrike` 等把段数写在代码里；错数比没数更糟 |
| 10-29 00:30 | 自动处理在 `end_turn_will_kill_player` 时不结束回合（偏离 design §5.1 规则 3） | 已知致命的结束回合交给模型与护栏 |
| 10-29 00:30 | 计划/算术说明放战斗 playbook 切片而非 PlaySystem | PlaySystem 12,000 字符预算已用 ~11.9k |
| 10-29 00:30 | 暂停热键 F7：反编译确认 F6/F7 只在 `NGame.IsTrailerMode` 下是游戏调试键 | design §8.6 前置核对 |
| 10-29 00:30 | magpie 子智能体 5 个并行约 90 分钟均未落盘（PromptStateDiet 被停时留下文件，审阅后采用并补测试）；其余模块自写 | 「不要依赖」的实际情况；之后只派小而短的任务 |
