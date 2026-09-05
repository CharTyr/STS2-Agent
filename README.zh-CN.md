# STS2 AI Agent

<div align="center">

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

**《杀戮尖塔 2》（Slay the Spire 2）游戏内 AI 助手与自主队友 Mod**

[English README](./README.md) • [当前产品计划](./PRODUCT_PLAN_CURRENT.md) • [联机交付跟踪](./COOP_DELIVERY.md) • [API 文档](./docs/api.md) • [MCP 工具指南](./mcp_server/README.md)

</div>

---

## 🌟 项目亮点

- 🎮 **游戏内悬浮窗界面**：在游戏内按 **F8** 即可随时唤起，无需切屏或打开浏览器。
- 🤖 **支持任意主流大模型**：完美兼容 OpenAI、DeepSeek、硅基流动 (SiliconFlow)、OpenRouter、Ollama、LM Studio 等，支持按模型配置思考强度（Thinking Intensity）。
- 🃏 **全自动自主游玩 (Auto-Play)**：纯文本模型即可完整打通战斗、选牌、商店、宝箱、路线抉择全流程；支持外挂视觉模型辅助。
- 👥 **本地双开联机组队 (AI Teammate)**：一键在本地拉起副游戏窗口并自动组队，你操控自己的角色，AI 队友操作它的角色，共同爬塔。
- 💬 **队伍实时自然语言交流**：在人类窗口直接向 AI 队友发战术指令（如“集火右怪”、“走商店路线”），AI 队友会回复并根据讨论调整出牌。
- 🛡️ **会话成本硬护栏与异常熔断**：实时统计并限制 Token 消耗与请求数，防范失控扣费；连续 3 次异常自动安全熔断；离开对局自动刹车。
- 🔌 **开发者友好**：内置本地 HTTP API (`:8080`) 与 FastMCP Server (`:8765`)，支持 Cursor、Claude Desktop、Codex 等外部 Agent 客户端一键接入。

---

## 🚀 新手 3 分钟极速上手

### 第 1 步：安装 Mod
1. 前往 [GitHub Releases](https://github.com/CharTyr/STS2-Agent/releases) 下载最新的发布包（zip）。
2. 解压后将以下三个文件复制到游戏的 `mods/` 文件夹中（若没有 `mods` 文件夹可新建）：
   ```text
   STS2AIAgent.dll
   STS2AIAgent.pck
   mod_id.json
   ```
   > 💡 **Steam 默认路径参考**：`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`

### 第 2 步：进入游戏并呼出界面
1. 正常启动《杀戮尖塔 2》。
2. 在任意游戏界面按下 **`F8`**（可在设置中修改），或点击屏幕右侧边缘灰色的 **`AI`** 标签，即可打开 Agent 控制悬浮窗。

### 第 3 步：配置大模型（以 DeepSeek / 硅基流动 / 本地模型为例）
1. 在悬浮窗顶部切换到 **「设置」** 页面。
2. 点击 **「添加端点」**：
   - **名称**：如 `SiliconFlow` 或 `DeepSeek`
   - **Base URL**：例如 `https://api.siliconflow.cn/v1` 或 `https://api.deepseek.com/v1`
   - **API Key**：填入你的模型 API 密钥（使用本地 Ollama/LM Studio 可留空或填任意值）
3. 在下方选择你刚刚添加的端点，并在「对话模型」与「游玩模型」中选定对应模型（例如 `deepseek-chat` 或 `deepseek-ai/DeepSeek-V3`）。
4. （可选）为模型设置思考强度（**Off / Low / Medium / High**）。
5. *新手安心保障*：默认开启了「会话预算守卫」，在设置页面可看到 `Max Tokens` 与 `Max Requests`，防止产生意料外的模型调用费用。

### 第 4 步：开始体验！

#### 体验方式 A：单人自动代打
- 开始一局正常的单人游戏。
- 悬浮窗切换到 **「游玩」** 页面，点击 **「开始自动游玩」**（或点「单步执行」查看一步步决策）。
- AI 会读取当前卡牌与场面，自动打出最优卡牌、选择奖励、规划路线。

#### 体验方式 B：与 AI 队友双人联机（最推荐玩法！）
- 回到游戏**主菜单**。
- 悬浮窗切换到 **「AI 队友」** 页面，点击 **「邀请 AI 队友」**。
- 系统会自动拉起第二个游戏实例并加入本地联机大厅！你控制主角色，AI 队友控制副角色。
- 点击 **「队伍交流」** 标签，可以直接用文字和队友商量路线与集火策略。

---

## 🎮 核心玩法详解

### 1. 自动游玩 (Auto-Play)
- **纯文本模型全流程**：底层采用高压缩率且信息完备的 compact 状态（包含手牌、能量、怪物意图、遗物、血量等），无需具备视觉能力即可通关。
- **可选视觉增强**：若配置了支持视觉的模型或外挂视觉模型，系统在需要时会自动截屏作为补充上下文辅助决策。
- **实时看板**：游玩页面实时显示当前会话消耗的 Prompt Tokens、Completion Tokens、总 Token 数以及请求次数。

### 2. 双人本地联机 (Co-op Companion)
- **零干扰隔离启动**：离线模式下自动继承 `--force-steam off` 并分配增量 `clientId`；启动器自动为副实例派生并首次克隆独立的 `settings.companion.json` 配置文件。彻底杜绝两个实例共用存档或并发写入配置导致的踩踏与冲突。
- **队伍交流机制 (Team Conversation)**：
  - 在人类窗口直接向 AI 队友发起讨论（如：“这回合我全力防御，你来输出”、“下层我们走问号房”）。
  - AI 队友使用游玩模型回复，并将近期的交流内容注入后续动作决策。
  - 聊天为只读安全设计：绝不会擅自替人类玩家出牌，也不会替暂停状态的队友代打。

### 3. AI 实时参谋 (Interactive Chat)
- 切换到 **「对话」** 页面，可随时向模型请教游戏理解。
- 勾选「附带当前状态」，大模型即可感知你当前的完整卡组、剩余生命与战局状态，提供针对性的构筑建议与出牌策略。

---

## 🛡️ 可靠性与安全护栏（玩得安心）

| 安全机制 | 功能说明 | 对玩家的保障 |
|---|---|---|
| **会话预算硬护栏 (`SessionBudgetGuard`)** | 统一统计非流式与流式 SSE 使用量；支持设置 Token 与请求上限 | 超额时即刻硬性停止，并在 UI 上标红提示，彻底消除账单超支焦虑 |
| **连续异常自动熔断 (`AutoPlayRecovery`)** | 连续 3 次空动作或决策异常时自动停止，并采用 2s / 4s 指数退避重试 | 遇到游戏未知卡死或模型输出不可行操作时自动刹车，不浪费费用 |
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
                           ▼
┌───────────────────────────────────────────────────────────┐
│              mcp_server (Python FastMCP)                  │
│  - stdio / HTTP (:8765) 适配器                             │
│  - Tool Profiles: guided / layered / full                 │
│  - 离线/内置游戏元数据检索 (Cards/Relics/Monsters/Events)   │
└──────────────────────────┬────────────────────────────────┘
                           │ MCP Protocol
                           ▼
        外部智能体 (Cursor / Claude Desktop / Codex)
```

### 开发者 HTTP API

Mod 默认在本地启动 HTTP 服务（默认端口 `8080`，遇冲突自动动态选择）：

- `GET /health`：服务健康检查，返回 `api_port`、`instance_role` 与进程 PID。
- `GET /state`：获取完整原始游戏状态 JSON。
- `GET /actions/available`：获取当前所有合法动作清单与参数 Schema。
- `GET /events/stream`：订阅游戏状态转换的 SSE 长连接流。
- `POST /action`：执行具体游戏动作（例如 `play_card`、`choose_map_node`、`proceed` 等）。

### FastMCP 协议集成

若希望使用外部 AI 客户端（如 Cursor、Claude Desktop 等）直接接管游玩，可通过内置 FastMCP 服务连接：

1. **环境准备**：安装 Python 3.11+ 与 [uv](https://astral.sh/uv)。
2. **启动服务**：
   - 方式一：在游戏内悬浮窗 **「接入」** 页面点击一键启动。
   - 方式二：通过脚本启动：
     ```powershell
     # Windows (HTTP 模式，默认 http://127.0.0.1:8765/mcp)
     powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
     ```
3. **外部客户端配置**（以 `claude_desktop_config.json` 为例）：
   ```json
   {
     "mcpServers": {
       "sts2-ai-agent": {
         "url": "http://127.0.0.1:8765/mcp"
       }
     }
   }
   ```
4. **Tool Profile 说明**：
   - `guided`（默认推荐）：提供精简的自主游玩接口 (`health_check`, `get_game_state`, `get_available_actions`, `act`, `get_game_data_*`, `wait_until_actionable`)。
   - `layered`：面向主/副 Agent 分层协同，补充提供 Handoff 与战斗观察记录工具。
   - `full`：全量暴露所有独立的 Legacy per-action 工具，适合细粒度测试。

---

## 🧪 源码构建与自动化测试

本项目拥有完善的端到端自动化单测套件，**所有核心逻辑均可脱离游戏客户端运行并验证**：

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

- **C# 核心单元测试（121 项全部通过）**：
  ```powershell
  dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
  ```
  涵盖会话预算守卫、自动恢复退避、战局边界拦截、离线双开隔离、网络端口自动后备、原生存档切换等。
- **Python MCP 契约测试（48 项全部通过）**：
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
1. 确认已将 `STS2AIAgent.dll`、`STS2AIAgent.pck` 和 `mod_id.json` 三个文件放进游戏的 `mods/` 文件夹。
2. 确认复制到的是 Steam 游戏实际的安装目录，而不是从 Git 克隆的项目源码目录。
3. 观察游戏主界面右侧屏幕边缘是否有半透明的灰色 **AI** 标签，若有可直接用鼠标点击展开。

### Q2: 自动游玩突然停住了，怎么恢复？
1. **检查是否达到预算上限**：悬浮窗中会显示明确的红字提示（如 `Session budget exceeded`）。若确认安全，可在「设置」中将 `Max Tokens` 或 `Max Requests` 调大，或重置统计后继续。
2. **检查网络与 API Key**：若 API 额度不足或网络断开，恢复控制器在连续 3 次失败后会自动停止保护。
3. **检查游戏状态**：是否退回了主菜单或联机大厅？战局边界保护会在离开战局时自动刹车。

### Q3: 本地双开副窗口打不开或提示失败？
1. 本地双开通过游戏内部的联机大厅进行对接。
2. 启动器已自动配置 `--force-steam off` 与增量 `clientId`；部分杀毒软件可能会拦截子进程拉起，请加入信任列表。
3. 若端口被占用，Mod 会自动寻找后续可用端口，请以悬浮窗显示的地址为准。

### Q4: 推荐使用什么大模型？
- **云端商业模型**：首选 DeepSeek-V3 / DeepSeek-R1、OpenAI GPT-4o / o3-mini、Claude 3.5 Sonnet。
- **本地私有化模型**：使用 Ollama / LM Studio 部署 7B~14B 以上参数量、具备良好 JSON 工具调用能力的模型（如 Qwen2.5-7B/14B、Llama-3-8B 等）。

---

## 📁 仓库结构

```text
STS2-Agent/
├── STS2AIAgent/          # C# 游戏内 Mod（Godot UI 悬浮窗、决策循环、预算守卫、HTTP 服务）
├── STS2AIAgent.Tests/    # C# 核心单元测试（121 项，无游戏依赖）
├── mcp_server/           # FastMCP Server 封装（Python）及离线游戏元数据
├── scripts/              # 构建、部署、启动与全量预检脚本
├── skills/               # 面向 MCP 外部 Agent 的策略 Skill 规范
├── docs/                 # 开发设计文档与 API 接口参考
├── PRODUCT_PLAN_CURRENT.md # 官方当前产品成熟度计划与基线评估
└── COOP_DELIVERY.md      # AI 队友与联机双开交付全流程跟踪
```

---

## 开源协议

本项目采用 [GNU Affero General Public License v3.0 (AGPL-3.0)](./LICENSE) 协议开源。
