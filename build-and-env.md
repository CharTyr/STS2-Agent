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
- `GODOT_BIN`: Godot 可执行文件路径（可选）
- `STS2_API_BASE_URL`: Mod API 地址，默认 `http://127.0.0.1:8080`

说明：

- `STS2AIAgent.csproj` 支持从 `STS2_DATA_DIR` 读取数据目录。
- 未设置变量时，脚本会自动探测常见安装路径。

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

## 6. Troubleshooting

- `connection refused`:
  - 游戏未启动，或 Mod 未加载成功。
  - 首次加载时需在游戏内确认 mods warning。
- `MCP server is up but no state`:
  - 检查 `STS2_API_BASE_URL` 是否正确。
- PCK 打包异常：
  - 优先使用游戏自带运行时作为 `GODOT_BIN`。
