

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

# STS2 AI Agent

`STS2 AI Agent` 由两部分组成：

- `STS2AIAgent` Mod：把游戏状态和操作暴露为本地 HTTP API。
- `mcp_server`：把本地 HTTP API 包装成 MCP Server，供支持 MCP 的客户端直接调用。


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
- Mod 的完整实机验证脚本目前仍主要是 PowerShell + Windows 路径；macOS 侧先解决“可构建、可安装”这条链路。
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

如果你更喜欢用 `local.props`，也可以从 [local.props.example](/Users/mac46/CodeWorkSpace/STS2-Agent/STS2AIAgent/local.props.example) 复制一份到 `STS2AIAgent/local.props`，填入你的 `Sts2DataDir`。现在项目也支持直接读取环境变量 `STS2_DATA_DIR`。

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
