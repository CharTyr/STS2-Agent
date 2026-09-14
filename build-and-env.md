# Build And Environment Workflow

本文档用于统一 STS2 Agent 的构建、部署与运行环境流程，覆盖 Windows 与 macOS/Linux。

## 1. Prerequisites

### Common

- Steam 安装并可运行 Slay the Spire 2
- Python 3.11+
- `uv`

### Windows

- PowerShell 5+ / PowerShell 7+
- .NET SDK（建议与项目当前目标框架匹配）

### macOS / Linux

- Bash
- .NET SDK
- Godot 4.x（推荐优先复用游戏自带运行时打包 PCK）

## 2. Key Environment Variables

- `STS2_GAME_ROOT`: 游戏根目录（可选）
- `STS2_DATA_DIR`: 游戏数据目录 `data_sts2_*`（可选）
- `STS2_MODS_DIR`: 游戏 `mods` 目录（可选）
- `STS2_EXE_PATH`: 游戏可执行文件 `SlayTheSpire2.exe`（可选）
- `STS2_APP_MANIFEST`: Steam 的 `appmanifest_2868840.acf`（可选）
- `STS2_STEAM_EXE`: Steam 客户端 `steam.exe`（可选，只有 `-ViaSteam` 启动才需要）
- `GODOT_BIN`: Godot 可执行文件路径（可选）
- `STS2_API_BASE_URL`: Mod API 地址，默认 `http://127.0.0.1:8080`

说明：

- `STS2AIAgent.csproj` 支持从 `STS2_DATA_DIR` 读取数据目录。
- `scripts/` 下需要游戏的脚本（`build-mod.ps1`、`start-game-session.ps1`、`test-mod-load.ps1`、`test-debug-console-gating.ps1`）按同一顺序解析路径：**命令行参数 → 上面的环境变量 → 探测 → 约定俗成的 Steam 默认路径**，Windows 侧的共享实现在 `scripts/lib-sts2-paths.ps1`。
- Windows 侧的探测会读注册表里的 Steam 安装位置，再顺着 `libraryfolders.vdf` 枚举所有 Steam 库，所以换盘符、第二个库里装的游戏都能自动找到；只有探测全部落空时才回落到约定俗成的默认安装路径。
 - POSIX 侧（`scripts/lib-sts2-paths.sh`，由 `scripts/lib-sts2.sh` 引入）用同一顺序解析，共用 `STS2_GAME_ROOT`、`STS2_EXE_PATH`、`STS2_APP_MANIFEST` 三个变量名（`STS2_STEAM_EXE` 只有 Windows 侧读）。探测覆盖 macOS 与 Linux 的约定 Steam 根目录，并读取每个安装的 `libraryfolders.vdf`，所以第二个库里装的游戏同样能找到；只有全部落空时才回落到约定路径。
 - 这套解析有离线测试：`bash scripts/test-lib-sts2-paths.sh`（也可用 `python scripts/check_verification_gates.py --only sh-syntax`，CI 与 preflight 都会执行）。它不需要游戏、Steam 或联网，把 `$HOME` 指向夹具即可验证解析顺序、vdf 解析、`.app` bundle 布局与第二个库。
- 需要真机才能确认的部分（游戏能否启动、PCK 能否打包、运行中的游戏进程能否被识别）不在该测试的声明范围内，仍按平台验收清单执行。
 - Windows 上请用 `.ps1` 脚本。从 Git Bash 运行 `.sh` 虽然能过语法与解析（离线测试就是在 Git Bash 里跑的），但 `cd`/`pwd` 返回的是 MSYS 形式路径，.NET 读不了，`dotnet build` 一步会失败。

## 3. Build And Deploy Mod

### Windows

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

### macOS / Linux

```bash
./scripts/build-mod.sh --configuration Release
```

常用自定义参数：

```bash
./scripts/build-mod.sh \
  --configuration Release \
  --game-root "/path/to/Slay the Spire 2" \
  --data-dir "/path/to/data_sts2_osx_arm64" \
  --mods-dir "/path/to/mods" \
  --godot-exe "/Applications/Godot.app/Contents/MacOS/Godot"
```

构建成功后会把以下文件复制到目标 `mods` 目录：

- `STS2AIAgent.dll`
- `STS2AIAgent.pck`

## 4. Start MCP Server

### stdio (recommended)

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

macOS/Linux:

```bash
./scripts/start-mcp-stdio.sh
```

### network (optional)

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

macOS/Linux:

```bash
./scripts/start-mcp-network.sh
```

默认地址：

- MCP HTTP: `http://127.0.0.1:8765/mcp`
- Health: `http://127.0.0.1:8765/healthz`

## 5. Verification Checklist

1. 游戏进程已启动。
2. `http://127.0.0.1:8080/health` 返回 `status: ready`。
3. MCP 可导入：

```bash
cd mcp_server
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

4. 可读取状态：

```bash
cd mcp_server
uv run python -c "from sts2_mcp.client import Sts2Client; import json; print(json.dumps(Sts2Client().get_state(), ensure_ascii=False))"
```

5. macOS/Linux 核心回归入口：

```bash
./scripts/test-full-regression.sh
```

非默认安装路径可以显式透传：

```bash
./scripts/test-full-regression.sh \
  --game-root "/path/to/Slay the Spire 2" \
  --exe-path "/path/to/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2" \
  --app-manifest "/path/to/appmanifest_2868840.acf" \
  --app-id 2868840
```

说明：

- 这条 `bash` 回归链路覆盖构建、Mod 装载、debug gating、MCP tool profile、主菜单生命周期、新局生命周期、完整状态不变量，以及双进程多人大厅流。
- 也可以单独运行 `./scripts/test-state-invariants.sh` 和 `./scripts/test-multiplayer-lobby-flow.sh` 做定向验证。
- 如果启动链路需要临时写入 `steam_appid.txt`，`start-game-session.sh` 会在退出时自动恢复；也可以传 `--skip-steam-app-id-file` 禁用这一步。

## 6. Troubleshooting

- 其它启动与环境问题（例如启动时报 `No appID found`）：
  - 见 [docs/troubleshooting.md](./docs/troubleshooting.md)。
- `connection refused`:
  - 游戏未启动，或 Mod 未加载成功。
  - 首次加载时需在游戏内确认 mods warning。
- `MCP server is up but no state`:
  - 检查 `STS2_API_BASE_URL` 是否正确。
- PCK 打包异常：
  - 优先使用游戏自带运行时作为 `GODOT_BIN`。
