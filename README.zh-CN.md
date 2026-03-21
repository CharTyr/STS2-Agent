# STS2 AI Agent

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

English README: [README.md](./README.md)

`STS2 AI Agent` 是一个给《Slay the Spire 2》用的游戏 Mod + MCP Server 组合：

- `STS2AIAgent`：把游戏状态和操作暴露为本地 HTTP API
- `mcp_server`：把这套本地 API 包装成 MCP Server，方便接入支持 MCP 的 AI 客户端

## `v0.5.2` 新增

这次发布把新的 MCP 能力一起带上来了：

- `guided` / `layered` / `full` 三种 tool profile
- 游戏数据查询工具：`get_game_data_item`、`get_game_data_items`、`get_relevant_game_data`
- 原始状态读取：`get_raw_game_state`
- 主 / 副 Agent 交接：`create_planner_handoff`、`create_combat_handoff`、`complete_combat_handoff`
- 运行时知识：`append_combat_knowledge`、`append_event_knowledge`、`complete_event_handoff`
- 发布包内补齐了 `mcp_server/data` 和 `docs/game-knowledge`

更细的工具说明在 [mcp_server/README.md](./mcp_server/README.md)，如果你要搭配 agent 工作流，优先看 [skills/sts2-mcp-player/SKILL.md](./skills/sts2-mcp-player/SKILL.md)。

## 快速开始

### 1. 安装 Mod

下载并解压 release 后，把下面这些文件复制到你的游戏目录 `mods/` 下：

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

### 2. 启动游戏并确认 Mod 生效

先正常启动一次游戏，让 Mod 随游戏一起加载。

然后在浏览器打开：

```text
http://127.0.0.1:8080/health
```

只要能看到返回结果，就说明 Mod 已经跑起来了。

### 3. 启动 MCP Server

先准备运行环境：

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

然后直接启动 `stdio` MCP。

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

macOS / Linux：

```bash
./scripts/start-mcp-stdio.sh
```

这就是默认推荐用法。大多数桌面 AI 客户端接 MCP，都优先用 `stdio`。

### 4. 连接你的 MCP 客户端

如果客户端支持命令式启动，把工作目录指向 `mcp_server/`，启动命令填：

```text
uv run sts2-mcp-server
```

如果你的客户端更适合连 HTTP，再启动网络版：

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

macOS / Linux：

```bash
./scripts/start-mcp-network.sh
```

默认 MCP 地址：

```text
http://127.0.0.1:8765/mcp
```

## 这个项目现在能做什么

当前 `main` 分支提供的是一套可直接游玩的基础能力，并且在 `v0.5.2` 增加了新的 MCP 数据与分层交接能力：

- 读取游戏状态
- 获取当前可执行动作
- 执行战斗、奖励、商店、地图、事件等常见操作
- 通过 SSE 事件减少高频轮询
- 以 `stdio` 或 HTTP 方式暴露 MCP
- 提供卡牌、遗物、敌人、药水、事件等打包元数据查询
- 支持 planner / combat 分层 handoff 流程

更细的工具说明在 [mcp_server/README.md](./mcp_server/README.md)。

## 常见问题

### `http://127.0.0.1:8080/health` 打不开

优先检查这几件事：

1. 游戏是否真的已经启动
2. `STS2AIAgent.dll`、`STS2AIAgent.pck` 和 `mod_id.json` 是否都放进了游戏目录的 `mods/`
3. 文件名有没有被系统自动改名或重复
4. 你放的是 Steam 游戏目录，不是仓库目录

### MCP 能启动，但读不到游戏状态

这通常表示 `mcp_server` 启动了，但游戏里的 Mod 没连上。先确认：

1. 游戏正在运行
2. `http://127.0.0.1:8080/health` 可访问
3. MCP 仍然在连默认地址 `http://127.0.0.1:8080`

### 要不要开 debug 动作

正常使用不需要。

像 `run_console_command` 这种开发期调试工具默认关闭，发布和日常使用都建议保持关闭。

## 从源码构建

如果你不是单纯使用 release，而是要自己构建：

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

macOS / Linux：

```bash
./scripts/build-mod.sh --configuration Release
```

更完整的环境变量、路径探测和验证流程见 [build-and-env.md](./build-and-env.md)。

## 仓库结构

- `STS2AIAgent/`：游戏 Mod 源码
- `mcp_server/`：MCP Server 源码
- `scripts/`：启动、构建、验证脚本
- `docs/`：补充文档
- `skills/`：配套 Skill

## License

This project is licensed under the GNU Affero General Public License v3.0 only (AGPL-3.0-only).
