# STS2 AI Agent：当前评估与成熟产品开发计划

评估日期：2026-09-05。以本次 `git fetch origin` 后的远程 `main` **27f2b704991e8142f73b5a0b80c7a73c6feb8037** 为准。本文是当前执行建议；旧版 PRODUCT_ROADMAP.md 的调研基线与待办状态已过时，产品定位和长期验收原则继续保留。

## 1. 产品判断

执行更新：集成测试与 CI/预检修复已提交至 [PR #61](https://github.com/CharTyr/STS2-Agent/pull/61)，提交 `accd605`。修复分支完整本地预检通过（97 项 C#、48 项 Python、profile、Mod 编译、元数据及文档），实际预检失败注入通过。远程 Actions 因 GitHub 账户账单锁定未启动任何步骤；A 阶段的远程 CI 门槛未达成，PR 尚未合并。下文 main 评估结果保留为修复前基线。

项目已从“外部 AI 操作游戏的接口”发展为“游戏内可配置、可交流、可控制的 AI 队友”。游戏动作覆盖、多人启动、模型接入和 MCP 基础已经比较完整，适合定位为功能型 Beta；当前证据还不足以支持稳定版承诺。

建议产品主线保持：**玩家操作自己的角色，邀请一个能交流、会配合、有个性的 AI 队友共同冒险。** 本地双开是核心旅程，单人代打是辅助模式，MCP 面向高级用户。成熟产品的重点是可靠完成这一旅程，并让玩家理解队友状态、控制行为和成本。

下一阶段优先级：合并后的稳定基线 → 安全可控的整局体验 → 首次使用与成本/配置管理 → 有趣的协同互动 → 可持续发布。不能以增加工具数量或少数演示局替代完整验收。

## 2. 远程、本地与发布版本

| 范围 | 本次核实结果 | 计划含义 |
| --- | --- | --- |
| 远程 main | `27f2b70`，已合并 #53/#54/#57/#58/#59/#60 | 后续开发应基于这一集成结果，不重复实现这些 PR |
| 本地 HEAD | `24d3244`，分支 `codex/ai-companion-experience`；相对 main 落后 4 个提交，没有独有提交 | 当前工作区有未提交改动，同步前须保留并审查 |
| 本地未提交工作 | 配置路径/离线双开隔离、预检退出码、CI workflow、元数据检查和测试脚本等 | 只能算候选工作，不能算远程已交付 |
| 最新 GitHub Release | [v0.9.2](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.9.2)，2026-08-31 | 新 main 能力不等于已进入用户安装包；main 版本字段仍为 0.9.2 |
| 远程自动化 | main 没有 `.github/workflows/`；本次 `gh run list` 返回空 | 需要把本地 CI 候选完成审查并合入，获得真实运行证据 |
| 待合 PR | [#56 原生存档档位识别与选择](https://github.com/CharTyr/STS2-Agent/pull/56) | 优先评审，支持“继续哪一局”和存档身份明确；不等同于完整测试存档隔离 |
| 开放 issue | [#50 uv.lock 依赖报告](https://github.com/CharTyr/STS2-Agent/issues/50)、[#51 package-lock.json 依赖报告](https://github.com/CharTyr/STS2-Agent/issues/51) | 核实实际版本、发布范围与影响后修复，不照抄报告中的漏洞数量 |

### 近期 PR 对计划的影响

| PR/提交 | 已进入主线的能力 | 剩余工作 |
| --- | --- | --- |
| [#53](https://github.com/CharTyr/STS2-Agent/pull/53) | 原生结算、解锁、保存校验和 MCP 写动作响应不确定时的防重放 | 普通/跨解锁阈值结算、重新读取存档和双人结算实机回归 |
| [#54](https://github.com/CharTyr/STS2-Agent/pull/54) | 事件选项本地化变量 | 多事件、中英文和缺失字段回归 |
| [#57](https://github.com/CharTyr/STS2-Agent/pull/57) | 卡组多选动作确认语义 | 选择、取消、确认及等待状态的场景回归 |
| [#58](https://github.com/CharTyr/STS2-Agent/pull/58) | 战斗 CanPlay 就绪诊断 | 将等待原因转为玩家能理解的状态；验证真实战斗 |
| [#59](https://github.com/CharTyr/STS2-Agent/pull/59) / `24d3244` | 队友身份绑定、会话通信、历史进入决策、只读聊天、暂停/恢复、动作队列与结算等待修正 | 双开完整旅程、断线恢复、真实模型策略效果；PR 正文及 COOP_DELIVERY 的“暂停待实现”已落后于代码 |
| [#60](https://github.com/CharTyr/STS2-Agent/pull/60) | Windows 保留端口的有界动态后备与绑定测试 | 合并后的测试项目修复、真实环境启动验收 |

### 本次复验

使用远程 main 的独立临时快照，未带入本地未提交代码。未修改业务代码、切换当前分支、启动游戏、调用付费模型或发布。

| 检查 | 结果 |
| --- | --- |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` | **失败，退出码 1**：测试 csproj 两次显式引用 `Server/LoopbackListener.cs`，触发 NETSDK1022；测试未执行 |
| `uv run --locked python -m unittest discover -s tests -v`（mcp_server） | **48 项通过**，退出码 0 |
| `uv run --locked python ../scripts/run_sts2_validation.py mcp-tool-profile` | **通过**，guided 10、guided debug 11、full 63，failures 为空 |

以上是当前集成主线的结果。旧文档的 48/57/63 项 C# 通过、profile 失败，以及各 PR 合并前的通过数量，均不能替代本次基线。没有进行完整 Mod 构建、实机整局或用户测试。

## 3. 当前最重要的缺口

1. **可信集成基线。** PR 分别通过不保证合并后通过，本次重复编译项就是实例。预检和 CI 必须能够挡住失败，文档要跟随集成结果更新。
2. **异常恢复和有限运行。** `AgentRuntime.AutoPlayLoopAsync` 仍在错误后固定延迟继续，缺少清晰的连续失败/无进展停止策略。已有暂停生命周期应复用，补充故障原因与“仅当前局”边界。
3. **成本透明。** `LlmCompletion` 没有 usage；客户端流式响应仍先 `ReadAsStringAsync`。玩家尚不能可靠知道本局成本、设置硬限制或实时观察请求进度。
4. **配置恢复与双进程写入。** 远程 SettingsStore 仍使用进程内锁、固定 `.tmp` 和复制覆盖，读取异常静默回到默认值；API key 随普通设置保存。需要跨进程协调、备份迁移和凭据分离。
5. **完整队友体验。** 通信和建议上下文已有；共同目标、关键时刻主动交流、战后反馈、低打扰频率和失联恢复仍需完善。消息进入 prompt 不等于策略正确采纳。
6. **可交付证据。** 缺少本次可核实的双开整局、首次用户成功率、支持矩阵和安装升级回退证据。测试框架、真实局面与用户体验须分别验收。

## 4. 开发阶段与退出门槛

时间是粗略估算：一名熟悉项目的主要开发者，配合稳定的实机 QA 和少量真实模型预算；约 8–12 周，需在第一阶段结束后复估。按门槛推进，不按日期跳过验证。

| 阶段 | 交付内容 | 退出门槛 | 预估 |
| --- | --- | --- | --- |
| A：集成基线 | 修复重复编译项；审查本地 CI/预检候选；统一远程与本地基线；更新状态文档；评审 #56；核实依赖报告 | 干净 main 快照核心测试、Python、profile、版本一致性全通过；故意失败能阻止预检；远程 CI 有成功记录；完整 Mod 编译通过 | 3–5 个工作日 |
| B：可靠整局 | 自动运行错误分类、有限退避、无进展暂停、仅当前局；复用现有暂停控制；明确等待/已提交动作；角色与存档身份；断线后的状态核对 | 401、429、5xx、超时、UNKNOWN、无动作、响应丢失、暂停及竞争测试；确认暂停后无新写动作；真实双开完成战斗到双方结算/存档 | 1–2 周 |
| C：普通玩家可用 | 组队引导、模型工具/视觉能力检测、配置迁移与恢复、凭据分离、usage/预算、取消与增量展示、脱敏诊断包 | 首次组队流程可由新用户独立完成；预算触发后不新发请求；usage 缺失显示未知；双进程保存/损坏恢复/升级测试通过 | 2–3 周 |
| D：有趣且有依据的队友 | 轻量共同目标、意图交流、战后短评、可选风格、主动发言频率；固定场景与对局评估 | 玩家建议可追踪到后续决策；始终只操作自身角色；发言能对应真实状态；新用户反馈和策略/成本对比有记录 | 2–3 周 |
| E：稳定发布 | GitHub/Workshop 一致性、干净安装/升级/回退、兼容性矩阵、诊断与支持入口、RC 观察 | 至少 30 次双开端到端样本；无未解决 P0；支持范围有实机证据；至少 7 天 RC 观察通过 | 1–2 周，含观察期 |

依赖：A 是所有后续合入的门槛；B 先确定会话生命周期与执行结果语义，C/D 才能可靠复用。诊断记录应从 B 开始，贯穿成本、策略评估与用户支持。

## 5. 接下来两个迭代的任务清单

| 顺序 | 任务 | 主要范围 | 实施 PR 与状态 | 验收/依赖 |
| --- | --- | --- | --- | --- |
| 1 / P0 | 去掉测试项目重复引用并复跑集成测试 | `STS2AIAgent.Tests.csproj` | [PR #61](https://github.com/CharTyr/STS2-Agent/pull/61) (已就绪) | 修复 NETSDK1022，100% 保留所有测试；全量核心测试通过 |
| 2 / P0 | 合入可信 CI 与预检 | `.github/workflows`、`scripts` | [PR #61](https://github.com/CharTyr/STS2-Agent/pull/61) (已就绪) | 新增 GitHub Actions 工作流，加固 Invoke-CheckedNative 原生退出码传播与失败注入 |
| 3 / P0 | 对齐开发基线及交付文档 | Git 分支、PRODUCT/COOP 文档 | [PR #65](https://github.com/CharTyr/STS2-Agent/pull/65) (就绪) | 梳理规范 PR 链条 (#61~#64)，同步测试基准与交付指标 |
| 4 / P0 | 双开主旅程实机验收包 | Multiplayer、验证脚本 | 本地实机在线 | 不污染日常存档；邀请、入厅、准备、消息、暂停、战斗、结算实测 |
| 5 / P1 | 运行恢复策略 | AgentRuntime、AutoPlayRecovery | [PR #62](https://github.com/CharTyr/STS2-Agent/pull/62) (已就绪) | 连续 3 次失败停止、2/4s 指数退避、正常等待不误扣预算、保留 HTTP 状态与配置错误退出 |
| 6 / P1 | 战局边界与离线账号作用域 | CurrentRunBoundary、CoopLaunchPolicy | [PR #62](https://github.com/CharTyr/STS2-Agent/pull/62) & [PR #63](https://github.com/CharTyr/STS2-Agent/pull/63) | 战局边界离开回菜单停止；离线双开自动递增 --clientId，隔离两窗口账号与存档 |
| 7 / P1 | 配置与凭据多实例隔离 | SettingsStore、环境变量 | [PR #63](https://github.com/CharTyr/STS2-Agent/pull/63) (已就绪) | 支持 STS2_AGENT_SETTINGS_PATH 隔离双实例配置并发写入，单测覆盖隔离与回退 |
| 8 / P1 | 会话诊断与预算基础 | LlmTypes、SessionBudgetGuard、UI | [PR #64](https://github.com/CharTyr/STS2-Agent/pull/64) (已就绪) | 提取 SSE/JSON Usage；统一游玩/视觉/对话计入；支持 MaxTokens/MaxRequests 硬上限；UI 实时展示 |
| 9 / P1 | 核实依赖报告 | #50/#51、锁文件、发布包 | 待后续跟进 | 确定实际暴露范围与修复版本，再运行回归 |

第一迭代集中完成 1–4 并启动 5；第二迭代完成 5–8，9 根据核查结果插入。不要同时启动全部大型功能，也不要重做 #59 的通信与暂停。

## 6. 成熟产品的验收口径

- **首次体验：** 5–8 名未参与开发的玩家中至少 80% 在 10 分钟内组队并完成第一场协同战斗，保留每个失败原因。
- **流程：** 至少 30 次人机联机端到端会话，目标至少 95% 到达自然结束并完成双方结算；正常战败属于流程成功。此为内部样本门槛，不宣传为统计可靠率。
- **控制：** 确认暂停后无新写动作；已提交动作如实等待；队友不操作人类角色；响应不确定不得盲目重放。
- **成本：** 每次模型调用有 usage 或明确未知；费用只作带单价来源的估算。缺 usage 时仍用请求数/时长做可执行的硬上限。
- **乐趣：** 记录“像队友的时刻”和“打断体验的时刻”，目标至少 80% 测试玩家愿意再次组队，报告样本数。胜率不能替代此指标。
- **策略：** 固定游戏/Mod/模型/提示词版本、角色、难度和种子条件，比较合法性、流程、楼层、胜率、耗时与 token；先建基线再承诺提升。
- **维护：** 异常可通过脱敏诊断定位；安装、升级、回退和支持平台有证据；发布包与版本说明一致。

## 7. 架构与范围控制

保留 Mod + 可选 MCP 架构。把会话生命周期、恢复、预算和诊断做成无 Godot 依赖的核心模块，利用现有独立测试；GameThread 负责游戏操作。GameStateService/GameActionService 随热点修复逐步拆分，不安排整体重写。

复用现有 AutoPlaySession、CompanionConnection、TeamConversation 与动作防重放逻辑；扩展统一控制权与状态一致性测试。新增 API 字段向后兼容，旧客户端继续可用。

1.0 暂缓云托管、账户订阅、复杂语音、多 Agent 编排、训练管线和向量记忆库。共同目标先采用有界、可解释的结构化摘要，以真实体验和评估结果决定是否增加复杂度。

## 8. 证据位置与限制

远程代码依据：[测试项目](https://github.com/CharTyr/STS2-Agent/blob/27f2b704991e8142f73b5a0b80c7a73c6feb8037/STS2AIAgent.Tests/STS2AIAgent.Tests.csproj)、[AgentRuntime](https://github.com/CharTyr/STS2-Agent/blob/27f2b704991e8142f73b5a0b80c7a73c6feb8037/STS2AIAgent/Agent/AgentRuntime.cs)、[SettingsStore](https://github.com/CharTyr/STS2-Agent/blob/27f2b704991e8142f73b5a0b80c7a73c6feb8037/STS2AIAgent/Config/SettingsStore.cs)、[LLM 类型](https://github.com/CharTyr/STS2-Agent/blob/27f2b704991e8142f73b5a0b80c7a73c6feb8037/STS2AIAgent/Llm/LlmTypes.cs)、[LLM 客户端](https://github.com/CharTyr/STS2-Agent/blob/27f2b704991e8142f73b5a0b80c7a73c6feb8037/STS2AIAgent/Llm/OpenAiCompatibleClient.cs)。

本次远程状态来自 GitHub CLI/API 与 fetch；PR 的历史测试自述不当作本次实测。临时验证快照位于 `C:/Users/chart/AppData/Local/Temp/sts2-assessment-d566692e5c4f4da5bd615dcc5a937d12/source`。本计划不声称完成依赖漏洞审计、真实模型质量评估、用户访谈或实机兼容性测试。
