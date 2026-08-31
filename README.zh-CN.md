# STS2 AI Agent

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

English README: [README.md](./README.md)

`STS2 AI Agent` 是《Slay the Spire 2》的游戏内 AI Agent Mod。安装后即可在可视化窗口里配置模型端点、对话、按模型调整思考强度、让模型自动游玩。视觉是可选项：有视觉的模型可以看画面，没有视觉的模型也能靠 compact 状态和工具打完全部内容，也可以另配一个视觉模型当外挂。也可以从窗口拉起本地第二实例，和模型一起联机。

本地 HTTP API 与 MCP Server 仍然保留，给开发者和外部客户端用。

- `STS2AIAgent`：游戏内窗口 + 本地 HTTP API（默认 `http://127.0.0.1:8080`）
- `mcp_server`：可选的 MCP 封装

MCP 工具说明见 [mcp_server/README.md](./mcp_server/README.md)。游戏内自动游玩沿用 [skills/sts2-mcp-player/SKILL.md](./skills/sts2-mcp-player/SKILL.md) 的状态优先规则。

## 快速开始（玩家）

### 1. 安装 Mod

下载并解压 release 后，把下面这些文件复制到游戏目录 `mods/` 下：

```text
STS2AIAgent.dll
STS2AIAgent.pck
mod_id.json
```

Steam 默认游戏目录通常是：

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
```

最终目录结构应当类似：

```text
Slay the Spire 2/
  mods/
    STS2AIAgent.dll
    STS2AIAgent.pck
    mod_id.json
```

### 2. 打开游戏内 Agent 窗口

正常启动游戏。按 **F8**（可改）或点屏幕右侧的 **AI** 标签。

窗口里可以：

1. **设置**：添加多个 OpenAI 兼容端点（OpenAI / DeepSeek / 硅基流动 / OpenRouter / Ollama / LM Studio 等）和模型，选择主对话模型；可选游玩模型、外挂视觉模型。思考强度在每个模型上单独设置（Off / Low / Medium / High）。
2. **对话**：和模型聊天。可勾选「附带当前状态」或「附带截图」。
3. **游玩**：开始/暂停自动游玩，或单步。走 compact 状态和工具，与 MCP 相同；不需要视觉也能打完全部流程。
4. **双开**：启动第二个本地游戏进程，本机开大厅，同伴实例由模型自动加入并游玩。
5. **接入**：一键启动 HTTP MCP，复制 API / MCP 地址，给 Cursor、Claude、Codex 或自写客户端用。

配置保存在 `%AppData%/STS2AIAgent/settings.json`，两个本地实例共用。

视觉是可选的额外上下文。纯文本模型可以直接游玩：compact `agent_view` + `get_game_state` / `get_available_actions` / `get_game_data_*` / `wait_until_actionable` / `act`。如果给游玩模型勾了「视觉」，或另外配了外挂视觉模型，截图才会作为辅助信息附上。

本地双开目前走游戏 debug 的 `multiplayer test` 大厅。Steam 可能阻止第二进程。启动器不会杀掉当前游戏。

### 3. 可选：确认 HTTP API

玩家不必打开浏览器。开发者仍可访问：

```text
http://127.0.0.1:8080/health
```

`/health` 现在会返回 `api_port` 和 `instance_role`。8080 被占用时会自动改绑 8081、8082……（除非设置了 `STS2_API_PORT`）。

## 可选：MCP Server（开发者）

游戏内 Agent 不依赖 MCP。外部客户端可在 overlay **接入** 页一键启动 HTTP MCP，也可以继续用下面的脚本。

1. 安装 `Python 3.11+`
2. 安装 `uv`

Windows 安装 `uv`：

```powershell
powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

macOS：

```bash
brew install uv
```

然后启动 `stdio` MCP。

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

macOS / Linux：

```bash
./scripts/start-mcp-stdio.sh
```

如果客户端更适合 HTTP：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

默认 MCP 地址：`http://127.0.0.1:8765/mcp`

## 这个项目现在能做什么

- 游戏内窗口：多端点/多模型、对话、按模型思考强度、自动游玩、可选截图视觉、本地双开联机、一键启动 MCP 给外部 Agent
- 读取游戏状态（compact `agent_view` 现含多人大厅摘要）
- 获取当前可执行动作
- 执行战斗、奖励、商店、地图、事件、休息点、宝箱、尖塔选择、Bundle 选择等操作
- 可选 MCP（`stdio` 或 HTTP）给外部 Agent
- 通过 Mod API 提供卡牌、遗物、敌人、药水、事件等实时元数据
- 提供逐卡牌/逐伤害/AI 决策事实、明确战斗胜负和遭遇类型、完整多人库存及正式 JSON Schema

事件与 Schema 格式见 [Protocol v2 事实事件](./docs/fact-events-v2.md)，更细的 MCP 工具说明在 [mcp_server/README.md](./mcp_server/README.md)。

## 常见问题

### 装了 Mod 但看不到窗口

按 **F8**，或点右侧 **AI** 标签。确认三个文件都在 Steam 游戏目录的 `mods/` 下。

### `http://127.0.0.1:8080/health` 打不开

优先检查：

1. 游戏是否真的已经启动
2. 三个 Mod 文件是否都在游戏 `mods/` 目录
3. 8080 是否已被另一实例占用，试 `http://127.0.0.1:8081/health`
4. 你放的是 Steam 游戏目录，不是仓库目录

### MCP 能启动，但读不到游戏状态

这通常表示 `mcp_server` 启动了，但游戏里的 Mod 没连上。请确认实际 `api_port` 上的 `/health`。

### 要不要开 debug 动作

正常使用不需要。窗口内双开会在内部打开 debug 多人大厅，不必把 `run_console_command` 暴露给 MCP。

## 从源码构建

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

macOS / Linux：

```bash
./scripts/build-mod.sh --configuration Release
```

不依赖游戏的核心单测：

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

更完整的环境变量、路径探测和验证流程见 [build-and-env.md](./build-and-env.md)。

## 仓库结构

- `STS2AIAgent/`：游戏 Mod 源码（窗口、LLM 客户端、Agent 循环、HTTP API）
- `STS2AIAgent.Tests/`：设置、LLM JSON、Agent 循环单测
- `mcp_server/`：可选 MCP Server 源码
- `scripts/`：启动、构建、验证脚本
- `docs/`：补充文档
- `skills/`：给 MCP 客户端用的配套 Skill

## License

This project is licensed under the GNU Affero General Public License v3.0 only (AGPL-3.0-only).
