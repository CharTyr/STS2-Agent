# STS2 AI Agent

<div align="center">

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

**《杀戮尖塔 2》（Slay the Spire 2）游戏内 AI 助手与自主队友 Mod**

[English README](./README.md) • [当前状态页](./PRODUCT_PLAN_CURRENT.md) • [API 文档](./docs/api.md) • [MCP 工具指南](./mcp_server/README.md)

</div>

---

## 🌟 项目亮点

- 🎮 **游戏内悬浮窗界面**：在游戏内按 **F8** 即可随时唤起，无需切屏或打开浏览器。
- 🤖 **OpenAI 兼容端点接入**：可配置兼容的 chat-completions 服务，并按模型设置思考强度。工具调用、视觉、流式和 usage 支持取决于服务商与模型；对话和游玩需分别测试。
- 🃏 **自主游玩 (Auto-Play)**：通过文本状态执行战斗、选牌、商店、事件和路线选择，可选视觉上下文。能否完成整局及策略质量取决于模型和游戏场景，验收边界见当前状态页。
- 👥 **本地双开联机组队 (AI Teammate)**：一键在本地拉起副游戏窗口并自动组队，你操控自己的角色，AI 队友操作它的角色，共同爬塔。
- 💬 **队伍实时自然语言交流**：在人类窗口直接向 AI 队友发战术指令（如“集火右怪”、“走商店路线”），AI 队友会回复并根据讨论调整出牌。
- 🛡️ **会话预算与异常恢复**：统计服务商返回的 Token 使用量和请求数，达到上限后停止新请求，并限制异常重试。缺失 usage 显示未知；预算护栏不等于账单金额保证。
- 🔌 **开发者友好**：内置本地 HTTP API (`:8080`)；游戏内「MCP」页可打开 MCP（同一端口 `/mcp`），支持 Cursor、Claude、Codex 等外部客户端接入。

---

## 🚀 新手 3 分钟极速上手

订阅 [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050)，等下载完成，然后 Steam → **带 Mod 启动 / Play with Mods**。不用拷任何文件。工坊页面是给玩家看的短说明；这份 README 写得更细，需要时再来看。

这个 Mod 还在开发中。有的功能可能不完整，也可能出错。欢迎把建议和遇到的问题发到 [GitHub Issues](https://github.com/CharTyr/STS2-Agent/issues)。

### 第 1 步：安装 Mod

**方式 A：Steam 创意工坊（推荐）**

1. 打开 [Steam 创意工坊页面](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) 点订阅，等下载完成。
2. 在 Steam 启动游戏时选择 **带 Mod 启动 / Play with Mods**。不用拷任何文件。
3. 若提示代码不受信任：接受后把游戏完全关掉再开；进 Mods 打开 **STS2 AI Agent**，再重启一次。

**方式 B：GitHub 发布包**

1. 从 [GitHub Releases](https://github.com/CharTyr/STS2-Agent/releases) 下载 `sts2-ai-agent-v*-windows.zip` 并解压。
2. 只把压缩包 **`mod/`** 目录里的这三个文件复制到游戏的 `mods/`（没有就新建）：
   ```text
   STS2AIAgent.dll
   STS2AIAgent.pck
   mod_id.json
   ```
3. 最终目录示例：
   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIAgent.dll
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIAgent.pck
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\mod_id.json
   ```
   不要把整个 zip、也不要把 `mcp_server/` 拷进 `mods/`。`mcp_server/` 只给开发者可选使用。

**不要重复安装：** 如果已经订阅工坊，又手动拷过 GitHub 文件，请删掉 `mods/` 里那份手动文件，只保留工坊订阅，否则可能看到两份 Mod。

### 第 2 步：进入游戏并呼出界面
1. 从 Steam 选择 **带 Mod 启动 / Play with Mods** 启动《杀戮尖塔 2》。
2. 在任意游戏界面按下 **`F8`**（可在设置中修改），或点击屏幕右侧边缘灰色的 **`AI`** 标签，即可打开 Agent 控制悬浮窗。

### 第 3 步：快速配置模型
1. 首次打开会进入 **设置 → 快速配置**：选服务商（DeepSeek、智谱 GLM、Kimi、通义千问、OpenRouter、OpenAI、硅基流动、Ollama、LM Studio 或自定义地址），填 API Key；点 **获取 Key** 会打开该服务商的 Key 页面。模型已按服务商填好常用的一个，右边的下拉可以换（本地 Ollama / LM Studio 可不填 Key，模型名填你本地已下载的）。
2. 点 **保存设置**：服务商、Key 或模型有改动时会自动测试一次（**测试主模型** 可以随时再测）。连通和工具调用都通过才能自动游玩；通过后「游玩」页顶部的提示会消失。
3. 点 **去游玩**：单人点「开始自动游玩」，双打把顶部切到「多人」再邀请 AI 队友。

请求上限、代理和思考协议、外部 Agent 等细节见 [模型配置细节](#模型配置细节)。

### 第 4 步：开始体验！

#### 体验方式 A：单人自动代打
- 开始一局正常的单人游戏。
- 悬浮窗切换到 **「游玩」** 页面，点击 **「开始自动游玩」**（或点「单步」一步步看决策）。
- AI 会读取当前卡牌与场面，自主选择出牌、奖励和路线；不保证每一步都是最优策略。

#### 体验方式 B：与 AI 队友双人联机（最推荐玩法！）
- 回到游戏**主菜单**。
- 悬浮窗切换到 **「游玩 → 多人」** 页面，点击 **「邀请 AI 队友」**。
- 系统会自动拉起第二个游戏实例并加入本地联机大厅！你控制主角色，AI 队友控制副角色。
- 主窗口会显示队友是否在连、正在等你/等游戏/请求模型，或为什么停、下一步点哪里。点 **暂停队友** 会立刻反馈；已提交的动作会先完成。
- 可以直接用文字和队友商量路线与集火策略。
- 队友默认拿大厅预选的角色并自动准备。想自己给它选角（在队友窗口里选，或通过队友 API 先 `select_character` 再 `embark`），勾上邀请按钮上面的 **「手动选队友角色」**（就是把 mod `settings.json` 里的 `companionAutoSelectCharacter` 设为 `false`）；此时队友会停在选角界面，让 AI 自己决定或等你来选，没有超时。
- 想接着打上次存下的联机对局，点邀请下面那枚 **「继续联机存档」**：主菜单上有联机存档时可用，它用本地直连开房读档并把队友拉回来。这份存档不要用游戏自己的读档按钮开：那条路走 Steam 联网，认不出本地直连的玩家 id，会把存档改名成坏档。

#### 选项 C：把队友窗口交给外部 Agent
- 在 **游玩 → 多人** 明确选择 **外部 Agent** 再邀请。HTTP/MCP 的已有邀请动作仍兼容按模型验证状态选择路线。
- 这条路线不需要配置或验证任何模型就能拉起队友——它根本不调用模型，所以没有模型也不会失败。队友照常拉起、照常加入大厅、照常准备。
- 进图后队友会停在原地，不会自己出牌：它的 `/health` 报 `play_phase: "paused"` 与 `session_requests: 0`，因为这条路线根本不调用模型。
- 从外部用队友实例自己的 `GET /state` 与 `POST /action` 驱动那个角色。队友入队后，**游玩 → 多人** 会显示它的接口地址，并有 **复制地址** 按钮；也可以从主窗口 `GET /health` 的 `data.companion.api_port` 找到（该端口通常不是 8080）。队友只能操作自己的角色，越界会返回 403 `forbidden_actor`；这条路线下 `data.companion.auto_play` 为 `false`。
- 用**主窗口**的 `POST /teammate/control`（请求体 `{"running": true|false}`）开始 / 暂停它。这是受支持入口，不需要会话令牌（主窗口自己持有队友会话令牌）。`running: true` 仍然需要已验证的游玩模型，与游戏内「继续游玩」按钮是同一道门禁；`running: false` 随时可用。

两条路线共用同一组结构条件（必须在主菜单、这是人玩的窗口、当前角色没有正在跑的自动游玩），只有模型这一档不同。完整契约见 [docs/api.md](./docs/api.md)。

### 模型配置细节
- **测试主模型** 通常发 2 个请求，最多等 45 秒；测试中按钮变成「取消测试」，再点一次就取消。只有工具调用通过才可自动游玩，能聊天不代表能出牌。
- 新安装默认限制自动游玩 1500 次请求（大约够打完一整局），达到上限会暂停，在游玩页点「清零计数」可以接着玩；可以调整，留空或填 0 为不限。旧配置中的不限设置会保留。模型检测也会调用服务，Token 和请求数不等于实际账单，费用以服务商为准。
- 对话与自动游玩共用快速配置的模型；多端点、视觉、思考协议和高级预算在 **显示高级选项** 中。代理模型名称可任意命名，思考协议可显式指定，无需改成 `deepseek-chat`。
- 游玩页切到 **多人**，选择 **AI 自动游玩** 后邀请。要让 Claude Code / Cursor / Codex 控制队友，明确选择 **外部 Agent**；该模式入队后等待外部指令，不需要模型。
- ChatGPT 订阅不包含 OpenAI API Key 或 API 额度。

---

## 🎮 核心玩法详解

### 1. 自动游玩 (Auto-Play)
- **文本状态决策**：compact 状态包含手牌、能量、怪物意图、遗物和血量等信息，视觉为可选补充；离线状态与动作测试不等于整局通关验收。
- **可选视觉增强**：若配置了支持视觉的模型或外挂视觉模型，系统在需要时会自动截屏作为补充上下文辅助决策。
- **实时看板**：游玩页面实时显示当前会话消耗的 Prompt Tokens、Completion Tokens、总 Token 数以及请求次数。

### 2. 双人本地联机 (Co-op Companion)
- **两条组队路线**：界面明确选择 AI 自动游玩或外部 Agent。前者要求工具调用验证，后者入队后等待外部指令（见 **选项 C**）。
- **零干扰隔离启动**：离线模式下自动继承 `--force-steam off` 并分配增量 `clientId`；启动器自动为副实例派生并首次克隆独立的 `settings.companion.json` 配置文件。彻底杜绝两个实例共用存档或并发写入配置导致的踩踏与冲突。
- **队伍交流机制 (Team Conversation)**：
  - 在人类窗口直接向 AI 队友发起讨论（如：“这回合我全力防御，你来输出”、“下层我们走问号房”）。
  - AI 队友使用游玩模型回复，并将近期的交流内容注入后续动作决策。
  - 聊天为只读安全设计：绝不会擅自替人类玩家出牌，也不会替暂停状态的队友代打。
  - 对话默认由玩家发起；可选「主动发言」默认关闭，开启后 AI 队友只在战斗开始与结束时各说一句，并可在「发言风格」中选择语气（轻松搭档 / 沉稳参谋 / 简短简报）。主动发言不会代打，也遵守暂停与预算上限。

### 3. AI 实时参谋 (Interactive Chat)
- 切换到 **「对话」** 页面，可随时向模型请教游戏理解。
- 勾选「附带局面」，大模型即可感知你当前的完整卡组、剩余生命与战局状态，提供针对性的构筑建议与出牌策略。

---

## 🛡️ 可靠性与安全护栏（玩得安心）

| 安全机制 | 功能说明 | 对玩家的保障 |
|---|---|---|
| **会话预算护栏 (`SessionBudgetGuard`)** | 统计服务商返回的 JSON/SSE usage 与请求数，新请求前检查上限 | 达到上限后停止新请求并提示下一步；已发请求仍可能增加消耗，缺失 usage 显示未知 |
| **连续异常自动熔断 (`AutoPlayRecovery`)** | 连续 3 次空动作或决策异常时自动停止，并采用 2s / 4s 指数退避重试 | 遇到游戏未知卡死或模型输出不可行操作时停止新请求，限制重复消耗 |
| **不可重试错误即刻停止** | 识别 401（未授权）、403 等配置类 HTTP 错误时立即终止 | 避免 API 密钥填错后仍陷入无限死循环调用 |
| **战局生命周期边界 (`CurrentRunBoundary`)** | 严格绑定当前对局 ID (`runId`) | 投降、通关、退出至主菜单或大厅时自动终止游玩，绝不跨局乱出牌 |
| **双开环境安全隔离 (`CoopLaunchPolicy`)** | 自动隔离离线身份与副实例配置文件 | 双开不串存档、不覆盖设置，稳定流畅联机 |

---

## 🛠️ 高级用户与开发者指南

### 系统架构

```text
┌───────────────────────────────────────────────────────────┐
│                    Slay the Spire 2                       │
│  ┌─────────────────────────────────────────────────────┐  │
│  │             STS2AIAgent (C# Mod)                    │  │
│  │  - Godot In-Game Overlay UI (F8)                    │  │
│  │  - OpenAI-compatible Client & Budget Guard          │  │
│  │  - Autoplay Decision Loop & Recovery Controller     │  │
│  │  - GameThread Action / State Synchronizer           │  │
│  │  - Local Dual-Instance Process Launcher             │  │
│  └───────────────────────┬─────────────────────────────┘  │
└──────────────────────────┼────────────────────────────────┘
                           │ 本地 HTTP API (:8080)
                           │ 可选 MCP (:8080/mcp)
                           ▼
        外部智能体 (Cursor / Claude Desktop / Codex)
```

### 开发者 HTTP API

Mod 默认在本地启动 HTTP 服务（默认端口 `8080`，遇冲突自动动态选择）：

- `GET /health`：服务健康检查，返回 `api_port`、`instance_role`、`mcp_enabled` 与进程 PID。在主窗口、且本次组队已连上队友时，还会额外返回 `companion` 块：队友的 `api_host`、`api_port`、`process_id`，以及本条路线的 `auto_play`。
- `GET /state`：获取完整原始游戏状态 JSON。
- `GET /actions/available`：获取当前所有合法动作清单与参数 Schema。
- `GET /events/stream`：订阅游戏状态转换的 SSE 长连接流。
- `POST /action`：执行具体游戏动作（例如 `play_card`、`choose_map_node`、`proceed` 等）。
- `GET /data/{collection}`：导出打包的游戏元数据集合（`cards`、`relics`、`monsters`、`potions`、`events`、`powers`、`characters`）。
- `POST /session/control`：启动 / 暂停本机角色的自动游玩（`{"running": true|false}`）。
- `POST /teammate/control`：在**主窗口**上开始 / 暂停 AI 队友（`{"running": true|false}`）。仅 loopback、仅主窗口；这是外部 agent 受支持的控制入口（对应选项 C）。
- `POST /companion/control` / `POST /companion/message`：控制 AI 队友实例 / 给队友发消息（仅本地双开）。这是队友实例自身的受控端点，由主进程带本次会话令牌调用；外部调用方改用 `POST /teammate/control`。
- `POST /mcp`：可选 MCP（Streamable HTTP）。默认关闭，在悬浮窗「MCP」页打开。

### MCP 怎么选

| 我的场景 | 用哪个入口 | 需要什么 | 如何确认连上 |
| --- | --- | --- | --- |
| 自己和 AI 一起玩 | 游戏内悬浮窗，不必开 MCP | 只装 Mod | 按 F8 / 点 AI，能打开「游玩 → 多人」 |
| Cursor / Claude / Codex 操作游戏 | **原生 MCP**：MCP 页打开开关 | 只装 Mod | 复制页面上的实际地址（端口可能不是 8080） |
| stdio、layered/full、兼容旧客户端 | 可选 Python `mcp_server/` | Python + uv | 在发布包根目录运行 `scripts/test-mcp-tool-profile.ps1` |

外部客户端步骤：F8 → **设置 → MCP** → 勾选 **启用 MCP** → 在「客户端配置」选 Claude Code / Cursor / Codex → **复制配置**（Claude Code 是一条终端命令，Cursor 是 JSON，Codex 是 `~/.codex/config.toml` 片段）。地址与 HTTP API 同一端口，只监听 `127.0.0.1`。不要写死 `8080` 或 `8765`。

Python sidecar 不是玩家必装，也不再作为推荐入口。

默认形态（把端口换成「MCP」页上显示的那个）：
   ```json
   {
     "mcpServers": {
       "sts2-ai-agent": {
         "type": "http",
         "url": "http://127.0.0.1:8080/mcp"
       }
     }
   }
   ```
内置 MCP 工具在内置自动游玩所用的工具面之上多一个 `health_check`：`get_game_state`、`get_available_actions`、`act`、`get_game_data_*`、`wait_until_actionable`。默认的 `guided` 档还提供 `decide`（一次调用给出状态、可用动作与场景指引）、`get_scene_guidance`、`get_raw_game_state`、`get_decision_log`、`get_run_summary` 与 `diff_state`。

游戏内自动游玩已经按配套 skill 的合同在打。若用 **外部** Agent 经 MCP 操作游戏（Cursor / Claude / Codex，或可选的 Python sidecar），请同时加载 [`sts2-mcp-player`](./skills/sts2-mcp-player/SKILL.md)。只接工具、不加载 skill，也能点合法动作；要接近游戏内自动游玩的效果，需要这份配套 skill。

---

## 🧪 源码构建与自动化测试

独立核心测试和 MCP 契约测试可**脱离游戏客户端运行**。Godot 界面、真实游戏动作、多人同步、存档与安装渠道仍需分别实机验证：

GitHub 发布包只带 `scripts/` 下用于启动和检查 Python sidecar 的脚本；下面的构建、预检和实机命令需要完整源码仓库。

### 编译 Mod

> ⚠️ **注意**：编译前必须**先关闭游戏**，否则 DLL 会被进程锁定无法写入。

```powershell
# Windows
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release

# Linux / macOS
./scripts/build-mod.sh --configuration Release
```
编译产物 `STS2AIAgent.dll` 与 `STS2AIAgent.pck` 会自动拷贝至游戏 `mods/` 目录。

### 运行自动化测试

- **C# 核心单元测试**：
  ```powershell
  dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
  ```
  涵盖会话预算守卫、自动恢复退避、战局边界拦截、离线双开隔离、网络端口自动后备、原生存档切换等。
- **Python MCP 契约测试**：
  ```powershell
  cd mcp_server
  uv run python -m unittest discover -s tests -v
  ```
- **一键全量发布前预检**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File ".\scripts\preflight-release.ps1"
  ```

---

## ❓ 常见问题排查 (FAQ)

### Q1: 进入游戏后按 F8 没有任何反应？
1. 工坊用户确认已「带 Mod 启动」。GitHub 用户确认 zip 的 `mod/` 里三个文件已放到游戏 `mods/`。不要同时留工坊和手动拷贝。
2. 确认复制到的是 Steam 游戏实际的安装目录，而不是从 Git 克隆的项目源码目录。
3. 观察游戏主界面右侧屏幕边缘是否有半透明的灰色 **AI** 标签，若有可直接用鼠标点击展开。

### Q2: 自动游玩突然停住了，怎么恢复？
1. 看「游玩 → 多人」页的状态和下一步。配置错误不会无限重试；改完设置、测试通过后点「继续游玩」，不必重启整个游戏。
2. Token 若显示「未知」，表示服务没返回 usage，不是消耗为 0。预算仍可用请求次数限制。
3. 需要排查时，在「游玩 → 多人」的「组队状态」卡底部点「复制诊断」或「保存诊断」。导出不含 API Key、授权头和会话令牌，默认也不含聊天正文。

### Q3: 本地双开副窗口打不开或提示失败？
1. 本地双开通过游戏内部的联机大厅进行对接。
2. 启动器已自动配置 `--force-steam off` 与增量 `clientId`；部分杀毒软件可能会拦截子进程拉起，请加入信任列表。
3. 若端口被占用，Mod 会自动寻找后续可用端口，请以「设置 → MCP」里显示的本机 API 地址为准。

### 更新与联机排障
- 第二窗口会从主窗口实际加载的模组复制文件，兼容 GitHub 的 `mod_id.json` 和工坊的 `STS2AIAgent.json`。未被修改的自动副本可随主窗口更新；手动安装或修改过的副本会保留并显示冲突路径。不要同时保留工坊和手动安装的两份 Mod。
- 模型、Key、预算及「仅战斗外」更改会同步到当前队友；只有收到队友确认才显示生效。当前动作会先停稳，配置不再满足工具验证时保持暂停。
- 本地 AI 联机存档应从 **游玩 → 多人 → 继续联机存档** 恢复。模组识别到原生读档的玩家身份不匹配时会阻止它，避免存档被移成 `.VAL.corrupt`。普通 Steam 存档不受此识别规则影响。
- **复制诊断** 写入剪贴板；**保存诊断** 保存脱敏文本并打开目录。诊断含 DLL 构建 SHA256，便于区分同版本的不同构建，不含 Key 或聊天正文。

---

## 📁 仓库结构

```text
STS2-Agent/
├── STS2AIAgent/          # C# 游戏内 Mod（Godot UI 悬浮窗、决策循环、预算守卫、HTTP 服务）
├── STS2AIAgent.Tests/    # C# 核心单元测试（无游戏依赖）
├── mcp_server/           # FastMCP Server 封装（Python）
├── scripts/              # 构建、部署、启动与全量预检脚本
├── skills/               # 面向 MCP 外部 Agent 的策略 Skill 规范
├── docs/                 # 开发设计文档与 API 接口参考
├── history/              # 归档快照、发布记录与实机证据
└── PRODUCT_PLAN_CURRENT.md # 官方当前状态与证据边界
```

---

## 开源协议

本项目采用 [GNU Affero General Public License v3.0 (AGPL-3.0)](./LICENSE) 协议开源。
