# STS2 AI Agent：当前测试前交付方案

本文是本工作树唯一的测试前执行入口。它描述修复后的代码如何被分层验收，不把静态阅读、历史记录或分支上的旧输出写成当前通过。旧规划见 [PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md)（历史）；交付证据记录见 [COOP_DELIVERY.md](COOP_DELIVERY.md)。

## 当前边界与基线

- 工作树：C:\Users\chart\Documents\project\sp-player-first-experience
- 分支：codex/player-first-experience
- 准确基线：HEAD 1be8e83（标签 v0.10.2）加上本工作树当前全部未提交修改。未提交修改包含代码、测试、脚本和文档；完整文件清单以该工作树的 git status --short 为准。
- 本轮状态：离线 A1–A6、B、C/G0 已通过。用户允许后已把 staging 装入 Steam `mods/`（旧文件备份在 `build/live-test-backup-mods`）。实机已验证 health/版本/未验证邀请拦截/原生 MCP；双开大厅进战斗超时失败。真实模型 F 未调用。日常档位 1 未开局，测试大厅走档位 3 后已切回 1。
- 本轮所有权：只维护 PRODUCT_PLAN_CURRENT.md、COOP_DELIVERY.md、CHANGELOG.md；不撤销其它代理或用户的修改，不提交、不合并、不发布、不上传 Workshop。
- 证据标签：代码改动（未验证）、静态检查（未运行）、自动化通过、实机通过、历史记录、已发布。只有取得对应新证据后才能使用后两个当前状态标签。

v0.10.2 只作为基线标签和历史版本名使用。它不表示本工作树叠加的未提交修改已经进入发布包。

## 给用户的一次确认

用户只需对下面的范围确认一次，代理即可按阶段执行，并在每个阶段形成证据后继续下一阶段：

> 同意在 C:\Users\chart\Documents\project\sp-player-first-experience、基线 1be8e83 + 当前未提交修改上，按本文件的 A–G 阶段执行离线门禁、隔离构建、ZIP 检查和指定的实机手动验收；允许写入工作树的 build/、bin/obj、.uv-cache 与临时目录；实机阶段只使用专用测试游戏目录、测试档位和隔离的双开设置文件；默认不访问真实存档、不调用真实模型、不运行发布/上传操作。真实模型测试另行确认模型、端点、单价来源、最大请求数/Token 和总预算后才能开始。任一失败先修复，再复测失败项及其受影响的完整矩阵；未有证据的项目保持“未运行”。

这次确认不自动授权以下动作：覆盖日常 mods/、打开或修改日常存档、向模型供应商发送请求、向 GitHub/Workshop 推送或创建 Release。即使当前打包脚本已经向构建步骤传递 -SkipInstall，C/G 阶段仍只允许写入专用测试目录。

## 阶段、命令和隔离矩阵

所有代码块都是“获确认后才执行”的候选命令；本次编写文档时没有执行其中任何一条。每条命令都标注独立的工作目录，避免在 mcp_server 中 cd 后继续使用仓库根目录的相对路径。

| 阶段 | 目的与入口 | 明确 CWD / 环境 | 游戏、存档、模型副作用 | 粗略耗时 / 退出门槛 |
| --- | --- | --- | --- | --- |
| A1 | C# 核心测试：TestRunner 注册的全套测试，覆盖首次引导、角色探测、设置保存、删除绑定、暂停/恢复、恢复策略、预算、队友、对局边界、结算/存档契约、Agent loop、原生 MCP 和现有游戏契约 | CWD=<repo>；Windows PowerShell；.NET 9 SDK；不设置真实模型凭据 | 不启动游戏；测试中的 loopback/temp 文件只能留在测试临时目录 | 约 2–8 分钟；退出码 0，所有测试 PASS |
| A2 | Python MCP sidecar 单元测试 | CWD=<repo>\mcp_server；uv 按 uv.lock；建议 UV_CACHE_DIR=<repo>\.uv-cache | 不启动游戏、不访问 Mod、不调用模型；uv 缓存只写工作树 | 约 1–5 分钟；退出码 0 |
| A3 | Python profile 矩阵与原生工具对齐：guided / layered / full / debug gating，另含 test_native_tool_alignment.py | CWD=<repo>；PowerShell；RepoRoot=<repo>；STS2_ENABLE_DEBUG_ACTIONS 仅由脚本临时设置 | 只导入 Python、读取源码/离线数据；不启动游戏 | 约 1–3 分钟；工具集合和 debug 开关符合矩阵 |
| A4 | 版本和 README 安装规则的源码静态检查 | CWD=<repo>；Python 3.11+；不需要游戏或模型 | 只读文件；不生成 ZIP、不安装 Mod | 约 1 分钟；版本字段、链接和 mod/ 安装说明一致 |
| A5 | 原生命令失败传播检查 | CWD=<repo>；Windows PowerShell；脚本使用临时 fixture | 不启动游戏、不访问存档或模型；只写系统临时目录并清理 | 约 1 分钟；故障注入返回非零且不能输出成功 |
| A6 | 与 fix_delivery 协调的离线总门禁：preflight-release.ps1 | CWD=<repo>；PowerShell、.NET 9、Python、uv；-ProjectRoot <repo> -Configuration Release | 会构建和运行离线测试；不安装到游戏 mods/、不启动游戏；uv 可能访问依赖索引 | 约 5–15 分钟；所有离线类别、版本和文档门槛通过 |
| B | 安全构建 Mod staging，明确使用 -SkipInstall | CWD=<repo>；需 Godot 控制台可执行文件，优先显式 -GodotExe；可将 -GameRoot 指向专用测试目录 | 写工作树 bin/obj、build/mods/STS2AIAgent；-SkipInstall 不复制到游戏 mods/ | 约 5–15 分钟；DLL、PCK、mod_id.json 齐全且游戏目录未变 |
| C | 生成 Windows ZIP；package-release.ps1 当前会向内部 build-mod 传递 -SkipInstall，并在压缩前后调用 artifact checker | CWD=<repo>；同 B；-OutputRoot 指向工作树专用目录 | 只写工作树 release 目录和 ZIP；不会因内部构建写入游戏 mods/ | 约 2–8 分钟；源码检查、目录检查和 ZIP 检查均通过 |
| D | 已安装 Mod 的脚本 smoke / API 状态 | CWD=<repo>；专用游戏目录、专用 API 端口（例如 18080/18081）；设置文件用绝对临时路径 | 会启动/停止游戏并读写测试档位，可能写日志和 steam_appid.txt；不接触日常存档 | 约 10–20 分钟；只在专用安装后执行 |
| E | 实机手动产品旅程：设置、暂停、预算、失联、双开、完整结算与存档 | CWD=<repo> 仅用于记录；游戏在专用目录/账号；双开使用两个隔离 STS2_AGENT_SETTINGS_PATH | 会启动两窗口、占用测试端口并改变测试存档；默认不允许真实模型 | 单项 5–90 分钟；按功能矩阵逐项取证 |
| F | 真实模型成本和策略验收（单独确认） | 供应商、模型、端点、价格和限额由用户另行确认 | 会产生供应商费用并发送状态/提示词；不得继承日常 Key 或真实存档 | 视模型响应和局长而定；无报价来源不估算金额 |
| G | 安装、升级、回退和最终交付决定 | CWD=<repo>；只对专用测试安装目录操作 | 写测试 mods/、备份目录和测试设置；不推送、不上传，除非另行授权 | 约 15–40 分钟；通过后仍是候选包 |

### 可直接复核的离线命令

以下每段命令都从新的 PowerShell 会话开始，CWD 不共享：

    # A1；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    dotnet run --project '.\STS2AIAgent.Tests\STS2AIAgent.Tests.csproj' -c Release

    # A2；CWD: C:\Users\chart\Documents\project\sp-player-first-experience\mcp_server
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience\mcp_server'
    $env:UV_CACHE_DIR = 'C:\Users\chart\Documents\project\sp-player-first-experience\.uv-cache'
    uv run --locked python -m unittest discover -s tests -v

    # A3；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    $env:UV_CACHE_DIR = 'C:\Users\chart\Documents\project\sp-player-first-experience\.uv-cache'
    powershell -ExecutionPolicy Bypass -File '.\scripts\test-mcp-tool-profile.ps1' -RepoRoot 'C:\Users\chart\Documents\project\sp-player-first-experience'

    # A4；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    python '.\scripts\check_release_metadata.py'
    python '.\scripts\check_release_package.py' --source-root 'C:\Users\chart\Documents\project\sp-player-first-experience'

    # A5；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\test-native-exit-propagation.ps1'

    # A6；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\preflight-release.ps1' -ProjectRoot 'C:\Users\chart\Documents\project\sp-player-first-experience' -Configuration Release

A6 是当前仓库静态可确认的离线总门禁候选。它不是本轮已运行证据。fix_delivery 需要在最终交付前确认它仍是离线总门禁，并保持其源码检查调用使用 --source-root；实际 ZIP 检查由 G0 的 --artifact 输入完成。

### 构建、打包和最终 ZIP 检查

    # B；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\build-mod.ps1' -ProjectRoot 'C:\Users\chart\Documents\project\sp-player-first-experience' -Configuration Release -SkipInstall -GodotExe '<absolute-path-to-Godot-console.exe>'

B 的 -SkipInstall 是必需参数。B 会写工作树构建产物，但不会将 DLL/PCK/manifest 复制到游戏 mods/。

    # C；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\package-release.ps1' -ProjectRoot 'C:\Users\chart\Documents\project\sp-player-first-experience' -Configuration Release -OutputRoot 'C:\Users\chart\Documents\project\sp-player-first-experience\build\release-confirmation' -GodotExe '<absolute-path-to-Godot-console.exe>'

C 当前会在内部构建步骤传递 -SkipInstall，并在压缩前检查 release 目录、压缩后检查实际 ZIP。生成的目录名和 ZIP 名由 mod_manifest.json 版本和冲突后缀决定，当前没有真实产物，不能预写名称、哈希或通过状态。

检查器一次只能走一个模式：`--source-root` 或 `--artifact`，不能写在同一条命令里。

    # G0 源码合同；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    python '.\scripts\check_release_package.py' --source-root 'C:\Users\chart\Documents\project\sp-player-first-experience'

    # G0 产物；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    python '.\scripts\check_release_package.py' --artifact '<absolute-path-to-generated-release.zip>'

G0 源码模式检查安装说明与打包脚本接线；产物模式检查真实目录或 ZIP 的 `mod/` 与包内 Markdown 链接。两条都未运行，没有真实 ZIP、哈希或通过记录。package-release.ps1 内部也会对目录和 ZIP 各跑一次产物检查。

## 功能验收全量矩阵

| 编号 | 能力与覆盖 | 自动化证据（获准后） | 实机/手动证据与验收标准 | 默认资源 / 状态 |
| --- | --- | --- | --- | --- |
| M1 | C# / Python 基础契约 | A1、A2；C# TestRunner 与 mcp_server/tests/ | 不需要实机；所有命令退出码 0，失败项阻断后续 | 无模型、无游戏；未运行 |
| M2 | 原生 MCP 与 Python 跨端契约 | McpServiceTests：禁用 403、initialize/session、tools/list、tools/call、notification 202、client config；PlayerExperienceTests.NativeMcpToolsMatchGuidedActContract；test_native_tool_alignment.py；A3 profile 矩阵 | 专用 Mod 启动后，从一个可用 MCP 客户端发现 health/state/data/wait/act 工具；确认 option_index、target_index、card_index 字段一致，通知无伪响应，关闭时返回可理解的 403 | 先离线契约；外部客户端连接未运行 |
| M3 | 首次引导与分用途 Test Connection | PlayerExperienceTests 默认未验证、游玩验证、Key 变更、对话成功不掩盖游玩 401、fresh fingerprint；CompanionStartupTests.FirstRunProvider | 依次测试对话/游玩/视觉角色；结果显示正确角色、成功/失败和下一步；游玩未验证时不可邀请；视觉未使用时显示未使用/未验证而不伪造成功 | 无真实 Key；无安全 fixture 时阻塞；未运行 |
| M4 | 配置保存、重载、迁移和删除保护 | SettingsStoreTests round-trip/missing/migration；PlayerExperienceTests endpoint/model removal；A1 | 分别绑定对话、游玩、视觉并保存，切页/重启 overlay 后仍在；删除被引用端点/模型先显示角色和替代步骤；取消不删除；重绑定并保存后才允许删除；未引用对象可删除 | 只写测试设置；未运行 |
| M5 | 各角色错误路径 | C# fake 覆盖游玩 401、用途状态和错误下一步；OpenAiCompatibleClientTests 覆盖协议解析 | 对话/游玩/视觉分别观察 401、429、5xx、超时和坏地址；错误归属于触发用途，不能被其它用途成功结果覆盖；不无限重试；可修正后单独重测 | 真实模型另行确认；未运行 |
| M6 | 暂停 / 恢复 / 聊天接管 | AutoPlaySessionTests、AgentLoopTests、TeamConversationTests、PlayerExperienceTests.PlayerFacingMapsPauseAndConfigError | 模型请求等待时点暂停；已提交动作可完成，之后不派发新动作；显示暂停原因；暂停期间可聊天且不执行 act；恢复只启动一个新回合，无重叠或重复 | 优先无费用响应；未运行 |
| M7 | 预算、usage 已知与未知 | SessionBudgetGuardTests 覆盖 token/request 上限、in-flight、恢复累计、超限立即停止；OpenAiCompatibleClientTests 覆盖 JSON/SSE usage；PlayerExperienceTests.MissingUsageIsNotDisplayedAsZero | 无 usage 响应显示“未知/尚无”而非 0；极小 request/token 上限在下一轮前停止；暂停/恢复不清零；聊天/队友回复共享预算；超限后无新请求 | 无模型费用；未运行 |
| M8 | 网络失联、退避、恢复和不可重试错误 | AutoPlayRecoveryTests 覆盖 401/403/429/5xx、无动作、等待、取消退避；AgentLoopTests 覆盖取消/失败动作 | 仅在无费用 mock 或另行授权环境中：请求等待时临时断网，观察有限退避和可见错误；恢复后手动继续并读取最新状态；配置类错误不无限重试；不盲目重放不确定动作 | 无安全 mock 时阻塞；真实网络故障需单独确认；未运行 |
| M9 | 双开启动、账号/设置/端口/角色隔离 | CompanionStartupTests、LoopbackListenerTests、TeamConversationTests；现有 test-multiplayer-lobby-flow.ps1 覆盖大厅/端口/投票前置 | Host 邀请 companion；health 同时匹配 service/ready/role/port/PID；重复点击不重复开进程；离线双开有不同 clientId；主/副设置文件互不覆盖；companion 只操作自己的角色 | 会改测试进程、端口和存档；未运行 |
| M10 | 双开共同旅程和协同战斗 | 现有 test-multiplayer-lobby-flow.ps1、test-coop-play-together.ps1（只用真实参数）；A1 队友/边界测试 | 建房→加入→选角→ready→开局过场→同一地图节点→至少一场战斗→奖励/休息→回地图；主窗口可发消息；暂停/恢复只控制 companion；投票、角色、状态一致 | 默认不接真实模型；策略效果按手测；未运行 |
| M11 | 完整结算、解锁、回主菜单和存档 | GameOverContractTests、ProgressSaveVerificationTests 覆盖 summary/continue/return、物理 save 缺失/损坏/不匹配/读取失败；CurrentRunBoundaryTests 覆盖离开 run | 专用存档正常完成一局或正常战败，等待 summary 动画，走原生继续/解锁/返回流程；两窗口确认 save verified；重启后进度仍在；回主菜单后队友停止且不自动新局。无脚本时只手动操作 | 会改测试存档；未运行 |
| M12 | 安装、升级、回退 | A4、C、G0 ZIP 结构/链接检查；B staging | 从 ZIP 只复制 mod/ 到专用 mods/，按 README 的 Steam 入口启动；确认 overlay/health/版本/设置；升级前备份旧 DLL/PCK/manifest，覆盖后检查旧遗留 manifest；回退恢复备份 | 只操作专用安装和备份；未运行 |
| M13 | 发布候选和支持边界 | A6、C、G0；fix_delivery 最终检查器确认记录 | 记录版本、ZIP 路径/哈希、安装升级、失败/修复/复测和支持矩阵；没有用户发布授权时只停在候选包，不创建 Release/Workshop | 未发布；未运行 |

## 实机命令与手动边界

项目已有的实机脚本只能按其真实参数调用：

    # M9 的大厅和端口前置；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\test-multiplayer-lobby-flow.ps1' -ProjectRoot 'C:\Users\chart\Documents\project\sp-player-first-experience' -HostApiPort 18080 -ClientApiPort 18081

    # M10 的既有双开流程；CWD: C:\Users\chart\Documents\project\sp-player-first-experience
    Set-Location -LiteralPath 'C:\Users\chart\Documents\project\sp-player-first-experience'
    powershell -ExecutionPolicy Bypass -File '.\scripts\test-coop-play-together.ps1' -HostApiPort 18080 -CompanionApiPort 18081 -Minutes 25

这些脚本会启动游戏并读写测试状态，不能在默认用户安装或日常存档上运行。test-full-regression.ps1 也会启动/停止游戏；只有在专用游戏目录和用户确认测试安装副作用后才可使用。它不替代 M3–M8、M11–M12 的手动验收。

设置保存/删除、暂停/恢复、网络断开、真实模型成本、完整结算存档、安装升级没有一个可静态确认的单一安全脚本时，采用矩阵中的手动步骤和验收标准。没有可用的无费用网络故障 fixture 时，不以真实模型请求填空。

## 失败、修复和复测规则

1. 任一命令非零、断言失败、UI 状态不符、双窗口状态不一致、存档未验证或 ZIP 结构不符，立即标为“失败/待修复”，不继续宣称后续阶段通过。
2. 修复由拥有相应代码文件的代理完成，并在记录中写出原因、修改文件和新证据；不得只重跑而不修复，也不得只修复而不复测。
3. 复测顺序为：失败用例/手动场景 → 同一文件或模块的相关矩阵 → A6 或对应实机回归。受影响的 C#、Python、原生 MCP、ZIP 或双开契约都要重新纳入范围。
4. 失败输出、请求 ID、版本、端口、PID、设置路径和存档类型只记录脱敏摘要；诊断导出默认不含聊天正文、API Key、Authorization 或会话令牌。
5. 未得到新证据前保持“未运行”；历史日志中的 PASS 只能放在历史区域，不能升格为当前基线或当前发布状态。

## 成本与授权边界

离线 C#/Python/MCP 契约和 ZIP 文档检查不需要真实模型费用；游戏本体的测试时间和本机资源另行记录。真实模型费用无法从仓库、请求数或旧日志可靠推导，必须在 F 阶段开始前由用户单独确认：供应商、模型、计费单位/价格来源、最大请求数、最大 Token、最长时长和总预算。若服务不返回 usage，只能显示未知并依靠请求数/时长硬上限，不能把未知换算成 0 或虚构金额。

## 当前未完成

- A1–A6、B–G 全部尚未在本工作树运行；没有当前自动化、构建、ZIP、API、游戏、真实模型或发布证据。
- G0 的 --source-root / --artifact 检查器接口已由 fix_delivery 协调到文档，实际脚本与参数仍需在执行前静态复核；当前未运行、未生成 ZIP、未通过。
- 不访问真实存档、不调用真实模型、不清理其它工作树、不提交、不公开发布。

