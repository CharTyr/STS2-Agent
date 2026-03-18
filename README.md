

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

# STS2 AI Agent

`STS2 AI Agent` 由两部分组成：

- `STS2AIAgent` Mod：把游戏状态和操作暴露为本地 HTTP API。
- `mcp_server`：把本地 HTTP API 包装成 MCP Server，供支持 MCP 的客户端直接调用。

## 分支说明

当前 `master` 是原版主线分支，对应游戏内 Mod 名称 `STS2 AI Agent`。

- Mod 文件名：`STS2AIAgent.dll` / `STS2AIAgent.pck`
- 默认健康检查地址：`http://127.0.0.1:8080/health`
- 适合单独安装或作为后续合并基线

如果你想安装可与原版并存的实验版桌面托管台，请切到 `codex/in-game-agent-panel-exp` 分支；该分支使用独立的 Mod 标识、目录和端口。


## 你会下载到什么

发布包内通常包含这些目录：

```text
mod/
  STS2AIAgent.dll
  STS2AIAgent.pck
mcp_server/
  pyproject.toml
  uv.lock
  src/sts2_mcp/...
scripts/
  start-mcp-stdio.ps1
  start-mcp-network.ps1
  start-mcp-stdio.sh
  start-mcp-network.sh
README.md
```

如果你只想安装 Mod，只需要 `mod/` 目录里的两个文件。

## 快速开始

详细的编译与环境流程请看：[build-and-env.md](./build-and-env.md)。

### 1. 安装 Mod

1. 下载并解压 release 压缩包。
2. 打开你的游戏目录。
   Steam 默认路径通常是：

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
   ```

3. 如果游戏目录下没有 `mods` 文件夹，就新建一个。
4. 把 `mod/STS2AIAgent.dll` 和 `mod/STS2AIAgent.pck` 复制到游戏目录的 `mods/` 中。

最终结构应当类似：

```text
Slay the Spire 2/
  mods/
    STS2AIAgent.dll
    STS2AIAgent.pck
```

### 2. 启动游戏

先正常启动一次游戏，让 Mod 随游戏一起加载。

如果你想确认 Mod 是否已经生效，可以在浏览器里打开：

```text
http://127.0.0.1:8080/health
```

能看到返回结果，就说明 Mod 已成功启动。

### 3. 启动 MCP

#### 推荐方式：stdio MCP

这是最适合接入桌面 AI 客户端的方式。

先准备环境：

1. 安装 Python 3.11 或更高版本。
2. 安装 `uv`。

安装 `uv` 的常见方式：

```powershell
powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

macOS 下可以直接安装：

```bash
brew install uv
```

然后在 release 解压目录中运行：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

或者在 macOS / Linux 终端中运行：

```bash
./scripts/start-mcp-stdio.sh
```

脚本会自动：

- 进入 `mcp_server/`
- 执行 `uv sync`
- 启动 `sts2-mcp-server`

如果你更喜欢手动启动，也可以执行：

```powershell
cd ".\mcp_server"
uv sync
uv run sts2-mcp-server
```

macOS / Linux 手动启动：

```bash
cd "./mcp_server"
uv sync
uv run sts2-mcp-server
```

#### 可选方式：HTTP MCP

如果你的 MCP 客户端更适合通过网络地址连接，可以启动 HTTP 版本：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

或者在 macOS / Linux 终端中运行：

```bash
./scripts/start-mcp-network.sh
```

默认监听地址：

```text
http://127.0.0.1:8765/mcp
```

常用参数示例：

```bash
./scripts/start-mcp-network.sh --host 127.0.0.1 --port 8765 --path /mcp --api-base-url http://127.0.0.1:8080
```

### 4. macOS 现状说明

- `mcp_server` 可以在 macOS 上直接运行，只需要 `Python 3.11+` 和 `uv`。
- `STS2AIAgent` Mod 现在也提供了一个 macOS / Linux 可用的 `bash` 构建脚本：`./scripts/build-mod.sh`。
- macOS / Linux 现在也提供了一组对齐 Windows 的 `bash` 验证脚本，包括 `start-game-session.sh`、`test-mod-load.sh`、`test-debug-console-gating.sh`、`test-mcp-tool-profile.sh`、`test-state-invariants.sh`、`test-multiplayer-lobby-flow.sh` 和 `test-full-regression.sh`。
- `test-full-regression.sh` 现在会串起状态不变量检查和多人大厅流，覆盖单机主流程与双进程联机场景。
- 这些验证入口支持显式透传 `--exe-path`、`--game-root`、`--app-manifest` 和 `--app-id`，方便在非默认 Steam 安装路径下运行。
- `start-game-session.sh` 在需要时会临时写入 `steam_appid.txt` 来启动游戏，并在脚本退出时自动恢复；如果不希望脚本管理该文件，可以传 `--skip-steam-app-id-file`。
- 如果你已经有可用的 Mod 文件（`STS2AIAgent.dll` 和 `STS2AIAgent.pck`），macOS 侧最需要的是把游戏本地 API 跑起来，然后用这里的 `mcp_server` 连接 `http://127.0.0.1:8080`。

### 5. 从源码构建 Mod

#### Windows

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

#### macOS / Linux

先准备：

1. 安装 `dotnet` SDK。
2. 安装 Godot 4.x，并确保命令行可用，或者知道它的可执行文件路径。

macOS 常见安装方式：

```bash
brew install dotnet
```

然后运行：

```bash
./scripts/build-mod.sh --configuration Release
```

`build-mod.sh` 会自动尝试：

- 解析仓库根目录
- 探测 Steam 安装目录下的 `Slay the Spire 2`
- 优先使用游戏自带运行时打包 `.pck`（避免 Godot 版本高于游戏导致包不兼容）
- 探测游戏数据目录 `data_sts2_*`
- 探测 Godot 可执行文件
- 构建 DLL、打包 `.pck`、并复制到游戏的 `mods/` 目录

如果你的本机目录不在默认位置，可以显式传参：

```bash
./scripts/build-mod.sh \
  --configuration Release \
  --game-root "/path/to/Slay the Spire 2" \
  --data-dir "/path/to/data_sts2_osx_arm64" \
  --mods-dir "/path/to/mods" \
  --godot-exe "/Applications/Godot.app/Contents/MacOS/Godot"
```

也可以使用环境变量：

```bash
export STS2_GAME_ROOT="/path/to/Slay the Spire 2"
export STS2_DATA_DIR="/path/to/data_sts2_osx_arm64"
export STS2_MODS_DIR="/path/to/mods"
export GODOT_BIN="/Applications/Godot.app/Contents/MacOS/Godot"
./scripts/build-mod.sh --configuration Release
```

如果你更喜欢用 `local.props`，也可以从 [local.props.example](./STS2AIAgent/local.props.example) 复制一份到 `STS2AIAgent/local.props`，填入你的 `Sts2DataDir`。现在项目也支持直接读取环境变量 `STS2_DATA_DIR`。

## MCP 客户端如何接

如果你的客户端支持 `stdio` MCP，一般只需要把启动命令指向：

```text
uv run sts2-mcp-server
```

工作目录设置为 release 包中的 `mcp_server/` 即可。

如果你的客户端支持 HTTP MCP，地址填：

```text
http://127.0.0.1:8765/mcp
```

## 2026.3.13 改动
**改动思路：**增加分层结构，一个主agent用于规划路线，它能获知全局地图（即当前节点的全部可达路线）并根据实际情况规划路线，以及在事件和对局结束的奖励中做出选择，另一个副agent专门负责战斗，战斗开始时唤起，并传递主Agent的留言、遗物、药水、敌人背景知识等战斗开始时状态，战斗结束后清空上下文，如此能够节省token开销；同时，战斗agent能够在知识库中记录某个敌人的出招顺序（单个敌人通常出招固定，多个敌人也遵循一定的规则）、特性（例如死亡时效果等），以及对待某个敌人的处理思路。这就意味着要维护一个很简单的知识库，暂定以分层目录的形式维护，如果是多个怪物的战斗，则排序后命名为怪物名id/*数量+怪物名id/*数量的形式，根据战斗开始时状态而定，战斗agent自动和对应md建立联系，允许在战斗时和战斗结束后写入出招顺序/处理思路等；主agent同样可以维护一个知识库，对于不同的问号房事件记录不同选项的结果，遇到对应房间自动传入上下文

本轮改动重点是给上层 agent 提供“主 Agent 负责路线 / 房间决策，战斗 Agent 负责局内操作”的分层接法，而不是继续扩底层动作。

已完成：

- MCP 新增 `layered` profile，保持 `guided` 紧凑的同时，额外暴露分层编排工具。
- 新增 planner / combat handoff 工具：
  - `create_planner_handoff`
  - `create_combat_handoff`
  - `complete_combat_handoff`
  - `complete_event_handoff`
- 新增运行时知识库支持：
  - 战斗知识按 `enemy_id_xcount` 聚合落盘
  - 事件知识按 `event_id` 落盘
  - 支持战斗中写观察，也支持战斗结束后按 `combat_key` 回写总结
- `GET /state` 的 `run` payload 新增 `floor` 字段，方便知识归档和上层决策压缩。
- MCP profile 校验脚本已同步覆盖 `layered`。

当前设计约束：

- 运行时知识库默认写入仓库下的 `agent_knowledge/`
- 组合怪文件名采用 Windows 可用格式，例如 `cultist_x2+slime_large_x1.md`
- 当前还没有 chapter / act 字段时，知识先归档到 `global/`

详细工具和交接说明见：

- `mcp_server/README.md`

## 测试状态

### 已完成测试

这些测试是在当前 Linux CLI 环境中完成的：

- Python 侧语法检查通过：
  - `sts2_mcp/client.py`
  - `sts2_mcp/server.py`
  - `sts2_mcp/network_server.py`
  - `sts2_mcp/knowledge.py`
  - `sts2_mcp/handoff.py`
- 在本地虚拟环境中安装 `fastmcp` 后，确认 `guided` profile 仍只暴露：
  - `health_check`
  - `get_game_state`
  - `get_available_actions`
  - `act`
- 确认 `layered` profile 额外暴露：
  - `get_planner_context`
  - `create_planner_handoff`
  - `get_combat_context`
  - `create_combat_handoff`
  - `complete_combat_handoff`
  - `append_combat_knowledge`
  - `append_event_knowledge`
  - `complete_event_handoff`
- 用模拟 state 跑通了：
  - planner handoff 生成
  - combat handoff 生成
  - combat result 回写知识库
  - event result 回写知识库
- 确认运行时知识文件会正确创建，例如：
  - `combat/global/groups/cultist_x2.md`
  - `events/global/cleric.md`

### 未完成测试

这些测试我当前无法在本环境完成，仍需要原作者或有游戏环境的人实机验证：

- `STS2AIAgent` C# Mod 编译
  - 当前环境没有 `dotnet`
- 游戏内实机验证 `/state` 新增的 `run.floor`
- 实机验证 `layered` profile 通过真实 MCP 客户端调用
- 主 Agent -> 战斗 Agent -> 主 Agent 的完整 live handoff 流程
- 战斗结束后用真实 `combat_key` 回写知识库，再由下一次 planner handoff 读取压缩总结
- 事件结束后 `complete_event_handoff` 与真实事件链路对齐
- Windows 游戏目录下运行时知识库路径、文件命名和权限检查

### 建议原作者优先验证

如果原作者准备接手测试，建议优先按这个顺序验证：

1. `scripts/test-mcp-tool-profile.ps1`
2. `/health`、`/state`、`/actions/available`
3. `run.floor` 是否稳定出现在 live state
4. `layered` profile 下的 `create_combat_handoff`
5. 一场真实战斗后的 `complete_combat_handoff`
6. 一个真实事件后的 `complete_event_handoff`

## 常见问题

### 看不到 `http://127.0.0.1:8080/health`

优先检查：

1. 游戏是否已经启动。
2. `STS2AIAgent.dll` 和 `STS2AIAgent.pck` 是否都放进了 `mods/`。
3. 文件名是否被系统自动改成了带 `(1)` 的副本。
4. 游戏目录是否放错了，例如放进了仓库目录而不是 Steam 游戏目录。

### MCP 能启动，但读不到游戏状态

这通常表示 MCP 正常，但游戏里的 Mod 没有连上。请先确认：

1. 游戏正在运行。
2. `http://127.0.0.1:8080/health` 可访问。
3. MCP 使用的接口地址仍然是默认值 `http://127.0.0.1:8080`。

### 要不要开启 debug 动作

正式使用不需要。

`run_console_command` 这类调试工具默认关闭，发布建议保持关闭。


## 相关目录

- `STS2AIAgent/`：游戏 Mod 源码
- `mcp_server/`：MCP Server 源码
- `scripts/`：构建、验证和启动脚本

## License

This project is licensed under the GNU Affero General Public License v3.0 only (AGPL-3.0-only).

If you modify this project and distribute it, or run it as a network service, you must provide the complete corresponding source code under the same license.
