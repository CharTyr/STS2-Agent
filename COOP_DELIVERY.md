# AI 队友交付跟踪

## 2026-09-06 玩家首次成功与运行状态（代码实现，验证未跑）

工作树：`C:\Users\chart\Documents\project\sp-player-first-experience`，分支 `codex/player-first-experience`，基于 `origin/main` `1be8e83` / 发布 v0.10.2。

本批补：安装说明与 zip `mod/` 结构、首次引导、分用途连通测试、设置保存/删除保护、主窗口运行状态与暂停文案、usage 未知、脱敏诊断、MCP 选择矩阵。不重做恢复/预算/原生 MCP 核心策略。

**证据状态：** 代码已写，测试用例已更新但**尚未执行**任何构建或测试。实机、真实模型、zip 产物检查均待授权。

后续静态返工：诊断字段全部走脱敏；暂停完成回调按会话身份隔离；模型测试失败只清对应用途的瞬时停止；删除被引用端点/模型改为先改绑再删；打包检查分 `--source-root` / `--artifact`；`RuntimeExperienceRegressionTests` 已接入 TestRunner。

**2026-09-06 离线验证（工作树 `sp-player-first-experience`，HEAD `1be8e83` + 未提交修改）：** A1 C# TestRunner 全绿；A2 Python 49 项通过；A3 guided 10 / layered 18 / debug 11 / full 63；A4 版本与源码包装通过；A5 退出码传播通过；A6 预检通过（Mod Release 编译 0 警告 0 错误）；B SkipInstall 构建成功；C/G0 zip `build/release-confirmation/sts2-ai-agent-v0.10.2-windows.zip` 产物检查通过。

**2026-09-06 实机（用户允许写入 Steam `mods/`）：** 已备份旧文件到 `build/live-test-backup-mods`，装入本工作树 staging。`/health` 报 `mod_version=0.10.2`、`api_port=18080`、`mcp_enabled=true`、`mcp_url=http://127.0.0.1:18080/mcp`。未验证配置下 `invite_ai_teammate` 返回 409「配置已填写，但尚未验证游玩模型」。原生 MCP initialize 200、tools/list 含 health/state/act 及 option_index/target_index/card_index。Agent 设置用 `build/live-test-settings/settings.json`。双开大厅脚本在档位 3 上完成建房/加入/选角/ready/进图投票，等待战斗超时失败。结束后切回档位 1 并关闭游戏。未调用付费模型。日常档位 1 未开局。

旧工作区 `sp` @ `24d3244` 的未提交改动未迁入；经核对，相关 FirstRun/Companion 文件主线已有。

---

## PR 系列与分支能力进展（2026-09-05）

> **基线说明**：远程 `main`（`27f2b70`）当前处于 `STS2AIAgent.Tests.csproj` 重复引用 `LoopbackListener.cs` 导致 `NETSDK1022` 编译失败的状态。以下能力按规范拆分为候选 PR 链条提交，各分支内部单测与预检已通过，但**在全部合入 `main` 并重新运行集成验证前，不能视为主线或正式 Release 的已交付基线**。

### 候选 PR 链条与当前状态：

1. **[PR #61](https://github.com/CharTyr/STS2-Agent/pull/61) (`codex/integration-validation`, commit `accd605`)** — **已合并至 main (Merged)**
   - 修复测试工程 NETSDK1022 重复编译引用，保留 100% 单测；新增 `.github/workflows/validate.yml` CI 工作流；在 `scripts/lib-checked-native.ps1` 实现 `Invoke-CheckedNative` 加固预检退出码。
   - 分支独立验证：97 项 C# 核心测试通过、48 项 Python 测试通过、Mod Release 编译 0 警告 0 错误。
2. **[PR #62](https://github.com/CharTyr/STS2-Agent/pull/62) (`codex/autoplay-recovery`, commits `577953d` + `a280799`)** — **已合并 (Merged)**
   - 实现 `AutoPlayRecovery.cs`（连续 3 次失败停止、2/4s 指数退避、正常等待不消耗预算、保留 HTTP 状态码）；战局边界 `CurrentRunBoundary.cs` 保护（回菜单/大厅即刻停止）。
   - 分支验证：107 项 C# 核心测试通过。
3. **[PR #63](https://github.com/CharTyr/STS2-Agent/pull/63) (`feat/coop-offline-and-settings-isolation`, commits `b0d1197` + `f9c9015`)** — **已合并 (Merged)**
   - 离线双开账号隔离：副窗口自动递增 `--clientId {id + 1}`；极值 ClientId 安全防护。
   - 配置隔离：`SettingsStore` 支持 `STS2_AGENT_SETTINGS_PATH` 环境变量，启动器自动派生 `settings.companion.json` 并首次同步，杜绝并发覆盖。
   - 分支验证：116 项 C# 核心测试通过。
4. **[PR #64](https://github.com/CharTyr/STS2-Agent/pull/64) (`feat/agent-token-usage-and-budget`, commits `be3b4d8` + `265953f`)** — **已合并 (Merged)**
   - LLM Token 使用量统计（JSON / SSE stream_options 解析）；多轮工具与视觉模型调用统一度量；会话预算硬护栏 `SessionBudgetGuard.cs`（`MaxSessionTokens` / `MaxSessionRequests`）；预算守卫与会话累计计数、对话拦截全面联动同步。
   - 分支验证：120 项 C# 核心测试通过。
5. **[PR #65](https://github.com/CharTyr/STS2-Agent/pull/65) (`docs/roadmap-and-delivery-alignment`)** — **已合并 (Merged)**
   - 统一收拢规划文档与交付状态，明确主线失败基线与分支证据边界。

### 测试证据边界划分：
- **远程 main（含 PR #56, #61）**：
  - PR #61 已合入 main 修复了 NETSDK1022 问题；PR #56 已合入支持原生 Profile 切换。
- **集成分支全量验证（汇集 #56, #61-#65 全量能力）**：
  - C# 核心测试套件：121 / 121 项 100% PASS（含 #56 Profile 切换与 #64 预算保护）。
  - Python MCP 测试套件：48 / 48 项 100% PASS。
  - Mod Release 编译：0 警告 0 错误。
  - 发布前预检 `scripts/preflight-release.ps1`：Exit Code 0 全部通过。

---

## 自动运行恢复（2026-09-05，后续分支）

- 基于集成修复提交 `accd605`，独立分支 `codex/autoplay-recovery`。
- 自动运行连续三次错误或无动作决策后停止，前两次间隔 2/4 秒；成功动作重置计数。等待玩家/游戏不增加计数，也不清除已有失败。
- HTTP 状态保留到模型异常；不可重试的 4xx 配置错误立即停止并提示检查。401/403/429/5xx 不再因错误正文含 stream 而额外重发非流式请求。408/429/5xx 使用有界恢复。
- 暂停可取消退避，停止原因通过原有会话观察器显示；本批不改变聊天控制权。
- 此批仅解决决策失败恢复，不等于完整“无进展检测”：动作返回成功却局面不变、UNKNOWN 长时间等待、当前局结束边界、预算和失联恢复仍需后续实现与实机验收。

## 2026-09-05 集成验证与发布门禁

- 基于远程 main `27f2b70` 修复测试项目重复引用 `LoopbackListener.cs` 导致的 NETSDK1022，保留全部测试。完整发布预检已在独立工作树通过：97 项 C# 核心测试、48 项 Python 测试、guided/layered/debug/full profile、版本元数据与文档检查；Mod Release 编译 0 警告/0 错误。
- 新增无游戏安装依赖的 Windows CI。预检显式检查所有原生命令退出码；故障注入实际启动预检子进程，验证构建退出 23 时整个预检失败且不输出成功。
- 更正下方历史状态：主窗口暂停/恢复已随 #59 合入，包含队友控制接口、暂停等待和取消边界；完整双开实机验收仍未完成。
- 本批没有部署游戏、进行真实模型/实机验证或发布稳定版；远程 CI 状态以对应 PR 的检查结果为准。后续继续推进运行恢复、配置/预算与整局体验，不能把门禁完成当作整个计划完成。

目标：按 PRODUCT_ROADMAP.md 推进成熟产品；双开是 Mod 的主要卖点，突出 AI Agent 和人类一起玩杀戮尖塔的有趣交互。

工作分支：`codex/ai-companion-experience`，从 main `71c3e41` 建立。不能用单人自动游玩或仅修改营销文案代替交付。

## 用户完整旅程与完成证据

| 旅程 | 必须交付 | 证明方式 | 状态 |
| --- | --- | --- | --- |
| 邀请队友 | 配好模型，从主窗口可靠启动第二实例、加入同一大厅、选角准备；不重复开进程、不影响用户当前存档 | 真实双开；默认/保留端口、退出/超时、重复点击测试 | 进行中 |
| 商量打法 | 玩家在主窗口向 AI 队友发消息，AI 理解并回应；无需切到第二窗口 | 正确实例路由、消息回传、实机对话记录 | 通信/界面已实现，待实机 |
| 共同战斗 | 消息和共同目标进入队友后续决策；AI 只控制自己，说明关键意图，可回应玩家建议 | 请求上下文和游戏动作证据，双方角色状态 | 消息进入后续请求已测试，实机策略效果待验证 |
| 随时接管 | 主窗口暂停/恢复 AI，暂停时仍能聊；动作不重复执行；失联可恢复 | 控制接口/取消边界/并发单测已覆盖（见 #59）；双人完整实机闭环待验收 | 接口已合入主线，实机端到端待验收 |
| 有趣的同伴 | 可选交流风格，关键时刻主动简短发言、战后反馈，控制打扰频率 | 真局面回应与玩家体验测试，不伪造固定夸赞 | 待实现 |
| 完整冒险 | 共同路线、事件/奖励、商店/休息、战斗、结算/解锁/存档全流程 | 双人完整实机回归，包括失败局 | 待验证 |
| 成熟交付 | 配置/预算/诊断、CI 门禁、双渠道安装升级与支持矩阵 | PRODUCT_ROADMAP.md 的 M0–M4 及 1.0 指标 | 待完成 |

## 2026-09-05 第一批实现

- 已调整产品主线及 1.0 验收，双开不再是可被跳过的实验能力。
- 已实现 HTTP 实际绑定检查与有界动态端口后备，覆盖 Windows 保留区、绑定竞争、显式端口不可用和资源关闭；队友端口探测也使用 HTTP 而非仅 TCP。
- 已为 `/health` 增加向后兼容的 `process_id`；队友启动必须同时匹配 service、ready、角色、端口和本次 PID，不再用字符串包含 ready 判定。
- 已在队友进程退出时提前结束等待，区分进程退出与仍在运行但未连通，外部取消不再被吞掉。
- 已将人类窗口默认页改为“AI 队友”，邀请前保存模型设置、检查主菜单/自动游玩/模型端点条件；邀请期间禁用重复点击，底层保留实际子进程句柄，超时后也不重复开队友。
- 自动化证据：新增 9 项启动/身份/组队前置条件测试；57 项核心测试通过，其中包含真实 loopback HttpListener 请求。完整 Mod Release 编译通过，0 警告/0 错误。
- 未部署、未完成实机双开验收；模拟的 Windows 保留区不能替代真实保留区环境复验。队友显式端口探测与启动之间仍存在绑定竞争，需要在会话建立/重试设计中处理。

## 第二批：队伍交流

- 已为每次新队友进程生成独立会话令牌（仅通过子进程环境传递）。新增 `/companion/message`，要求同伴角色、本机访问、正确令牌与有界消息正文。
- 主窗口新增队伍交流区，消息送达 AI 队友的游玩模型，回复显示在主窗口；请求先验证队友 PID，失败不自动重发。
- 队伍聊天与游玩共用回合互斥，可在自动游玩期间排队回复，也可在暂停时聊天。聊天强制只读，显式代打词和模型返回的 act 都不能绕过；聊天不会启动/恢复游玩。
- 最近 12 条队伍消息供后续游玩参考，明确标为历史上下文，要求重新从状态解析索引。新进程更换会话时清空，重复邀请同一实例保留对话。
- 新增 6 项测试：只读边界、建议进入后续请求、历史有界/清除、会话授权、HTTP 传输正文/令牌和 PID 替换拒绝。63 项核心测试通过；完整 Mod Release 编译 0 警告/0 错误。
- 实机双开、真实模型回复、界面布局和策略效果仍未验证。主窗口远程控制接口与暂停边界已随 #59 合入主线；双人端到端实机闭环、AI 主动发言、风格和低打扰策略仍待推进实现与验收。

下一步：主窗口队友控制与实机双开验证，再扩展主动发言和协同目标。不能把当前通信实现标记为整个目标完成。
