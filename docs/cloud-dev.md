# 云机开发环境

云主机上通常没有显卡、没有 Steam、也没有完整游戏本体。这份文档把「能在云机上做的事」拆成三层，每层给出**需要什么、怎么装、怎么验证**，以及一套从本机游戏提取最小编译/逆向资料到云机的闭环流程。

结论先说：**云端不需要搬 2.95 GB 的完整游戏。** 编译 Mod 只需要游戏里的 3 个程序集；再加上 `sts2.xml`、`release_info.json`、`sts2.runtimeconfig.json`，整包压缩后也只有约 5.7 MB（原始 23.6 MB），不仅能让云机编译 Mod，还能让云机上的编辑器和 AI Agent 查 API 注释、反编译 `sts2.dll`、甚至用同版 Godot 打出完整的 `.pck`。真正需要完整游戏（2.95 GB）的只有「实机运行一局」和「双版本实机发版验收」。

> **版权边界（必读）**：`pack-game-assemblies.ps1` 产出的压缩包包含《杀戮尖塔 2》本体的闭源程序集，**仅限私下传给你自己拥有正版游戏的个人云机/容器**。默认输出目录 `build/game-assemblies/` 已被 `.gitignore` 忽略；切勿提交到 Git，也不要附进任何公开 Release。

## 1. 三层能力

| 层 | 需要什么 | 云机因此能做什么 | 增量 |
|----|----------|------------------|------|
| **L0 离线** | .NET SDK 9、uv（自带 Python 3.12）、可选 PowerShell 7 | 核心单测、MCP 单测、全部 CI 离线闸门、API schema 语义检查、离线决策基准、路径解析测试、MCP 工具契约 | 工具链约 600 MB |
| **L1 编译与逆向** | L0 + 本机提取的程序集包（`sts2.dll` / `GodotSharp.dll` / `0Harmony.dll` + `sts2.xml` + 元数据）；可选 `ilspycmd` 与同版 Godot 4.x | `dotnet build` 出 `STS2AIAgent.dll`、双版本兼容编译（正式版 + 公测版）、云端 `ilspycmd` 反编译查游戏源码、`build-mod.sh --skip-install` 打出完整 DLL + PCK | +约 5.7 MB（精简包约 5.2 MB） |
| **L2 实机** | L1 + 完整游戏 + 可渲染显示（真实显卡或 Xvfb+软件渲染），或 SSH 隧道回连本机 Windows 游戏 | `mod-load`、`state-invariants` 等真机验证、截图与视觉相关功能 | +2.95 GB，另需 Steam 账号 |

`check-cloud-dev.sh` 按这三层报告机器状态并运行对应检查：

```bash
bash scripts/check-cloud-dev.sh                # 只报告当前具备哪几层
bash scripts/check-cloud-dev.sh --run offline  # 报告 + 跑全部 L0 离线检查（与 CI 对齐）
bash scripts/check-cloud-dev.sh --run build    # 报告 + 编译 Mod（若有双版本基线会两版都编）
bash scripts/check-cloud-dev.sh --run all      # L0 + L1 全部跑一遍
```

缺少某一层只会记为 `missing`，不会让退出码变 1；只有「点名要跑的检查失败」才返回 1。加 `--strict` 则任何一层不完整都返回 1，适合放进镜像构建或专职 CI。

## 2. L0：从零到能跑全部离线测试

```bash
git clone https://github.com/CharTyr/STS2-Agent.git
cd STS2-Agent
bash scripts/setup-cloud-dev.sh
bash scripts/check-cloud-dev.sh --run offline
```

`setup-cloud-dev.sh` 做的事，以及为什么这样做：

- **apt 只装最小基础包**：`git curl ca-certificates xz-utils unzip tar jq python3`。其余工具全部装进 `$HOME`，普通用户权限也能完成。
- **.NET 用 `dotnet-install.sh --channel 9.0` 装到 `~/.dotnet`**，与 `global.json` 的 `9.0.100 + rollForward latestFeature` 一致。**不要用 .NET 10 SDK 编译测试工程**：.NET 10 的 Roslyn 依赖 `System.Runtime 10.0.0.0`，会导致 `net9.0` 测试工程报 `CS1705`。
- **uv 装到 `~/.local/bin`**（设 `UV_UNMANAGED_INSTALL` 避免改写 shell 配置）。`uv sync --locked` 会自带 Python 3.12 运行 MCP 单测，系统 `python3` 只用来跑标准库闸门脚本。
- **PowerShell 7 装到 `~/.local/powershell`**，用于 `ps1-syntax` 闸门和 `scripts/test-mcp-tool-profile.ps1`；未装时记为 `missing`，其余检查照跑。
- **可选开发增强**：
  - `--with-ilspy`：安装全局 `.NET` 工具 `ilspycmd` 到 `~/.dotnet/tools`，拿到 `sts2.dll` 后可直接在云机反编译查看游戏内部类、字段与 Harmony hook 目标（详见 [reverse-engineering.md](reverse-engineering.md)）。
  - `--with-godot`：按已解包 `manifest.json` 里的 `godotVersion`（默认 `4.5.1`，可用 `GODOT_VERSION` 覆盖）下载 Linux Godot 4.x 到 `~/.local/godot` 并链接到 `~/.local/bin/godot4`，使云机可以直接打 `.pck`。
  - `--with-node` / `--with-gh`：按需安装 Node.js 或 GitHub CLI；离线测试本身不依赖它们。
- 最后写出 `~/.sts2-cloud.env`（包含 `~/.dotnet`、`~/.dotnet/tools`、`~/.local/bin` 与 `STS2_*` 变量），并在 `~/.bashrc` 挂一段带 `sts2-cloud-dev` 标记的 `source`（幂等，重跑不重复追加）。

`check-cloud-dev.sh --run offline` 覆盖的 10 项离线检查与 `.github/workflows/validate.yml` 的非 Windows 契约保持对齐：

1. `STS2AIAgent.Tests` 核心单测（`dotnet run`）
2. `mcp_server` 单元测试（`uv run --locked python -m unittest discover -s tests`）
3. 静态验证闸门（`scripts/check_verification_gates.py`）
4. 模型预算代理离线自测（`scripts/sts2-model-budget-proxy-selftest.py`）
5. 五处版本号一致性检查（`scripts/check_release_metadata.py`）
6. 生成的 OpenAPI Schema 语义测试（`scripts/test-api-schema.py`）
7. 离线决策基准测试（`scripts/decision_benchmark.py` + `scripts/test-decision-benchmark.py`）
8. 发布源码打包契约检查（`scripts/check_release_package.py --source-root .`）
9. POSIX 路径解析离线测试（`scripts/test-lib-sts2-paths.sh`）
10. MCP tool profile 契约测试（`scripts/test-mcp-tool-profile.ps1`，需 `pwsh`）

## 3. L1：从本机游戏提取最小包，在云机编译、查 API 与打 PCK

### 3.1 在本机（有游戏）打包

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1 -Force
```

脚本通过共享的 `scripts/lib-sts2-paths.ps1` 自动探测 Steam 游戏目录（也支持 `-GameRoot` / `-DataDir` / `-Channel`）。默认会把以下文件打进 `build/game-assemblies/sts2-assemblies-<version>-<platform>.zip`（以 `v0.114.0` 实测，压缩后 5.7 MB，原始 23.6 MB）：

- **编译必需（3 个）**：`sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`
- **参考与元信息（默认附带，传 `-Minimal` 可跳过）**：
  - `sts2.xml`：游戏 API 的 XML 注释文档，便于云机 IDE 和 AI Agent 查类型与方法签名
  - `release_info.json`：游戏精确版本号、commit、分支与 `main_assembly_hash`
  - `sts2.runtimeconfig.json`：游戏内置 .NET 运行时版本
- **`manifest.json`（schema v2）**：逐文件记录 SHA-256，并自动从 DLL 读取精确的 `godotVersion`（如 `4.5.1`）与 `harmonyVersion`（如 `2.4.2.0`）。

> 本机同时保存了 Steam 正式版（如 `v0.111.1`）和公测版（如 `v0.114.0`）两份游戏目录时，可以分别打两份包，用于云机双版本兼容编译：
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1 -GameRoot "D:\sts2-stable" -Channel stable -Force
> powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1 -Channel beta -Force
> ```

### 3.2 传到云机并校验解包

```bash
scp build/game-assemblies/sts2-assemblies-*.zip cloud:/tmp/

# 云机上：解包主版本（默认写入 STS2_DATA_DIR=~/.sts2/game-assemblies）
bash scripts/unpack-game-assemblies.sh /tmp/sts2-assemblies-v0.114.0-*.zip --force

# 可选：若还带了最低支持版本（如正式版 v0.111.1），注册为次基线（写入 STS2_MIN_DATA_DIR）
bash scripts/unpack-game-assemblies.sh /tmp/sts2-assemblies-v0.111.1-stable-*.zip --as-min --force

# 验证编译（若配置了 STS2_MIN_DATA_DIR，会自动对两个版本各编译一次）
bash scripts/check-cloud-dev.sh --run build

# 离线检查编译后的 STS2AIAgent.dll ABI、ReflectedGameMembers 私有成员与跨分支能力绑定
bash scripts/check-game-abi.sh --data-dir "$STS2_DATA_DIR"
# Windows 本机对应命令：
# pwsh -File scripts/check-game-abi.ps1
```

`unpack-game-assemblies.sh` 会先核对每个文件相对 `manifest.json` 的 SHA-256 再解压，避免下载截断后变成莫名其妙的编译报错。参数说明：

- `--as-min`：解包到 `~/.sts2/game-assemblies-min` 并写入 `STS2_MIN_DATA_DIR`，不覆盖主目录。
- `--slot <name>`：解包到 `~/.sts2/game-assemblies-<name>`，适合多版本并存。
- `--decompile`：若云机装了 `ilspycmd`，解包后直接把 `sts2.dll` 反编译到 `extraction/decompiled/<version>/`（该目录已在 `.gitignore` 中），云机上就能直接 `rg` 搜索游戏源码。

### 3.3 云端按需反编译（免传海量源码）

有了 6 MB 的程序集包，完全不需要从本机上传几百 MB 反编译目录。云机上装一次 `ilspycmd` 即可按需查看任意游戏类，或一次性反编译到本地 ignored 目录：

```bash
# 安装 ilspycmd（仅一次）
bash scripts/setup-cloud-dev.sh --no-apt --no-dotnet --no-uv --no-pwsh --no-sync --with-ilspy
. ~/.sts2-cloud.env

# 查看单个游戏类（秒级）
ilspycmd -t MegaCrit.Sts2.Core.Models.CardModel -r "$STS2_DATA_DIR" "$STS2_DATA_DIR/sts2.dll"

# 或整包反编译到 gitignored 目录供全文搜索
bash scripts/unpack-game-assemblies.sh --force --decompile
```

### 3.4 云端打出完整 Mod（DLL + PCK）并回传本机实测

`scripts/build-mod.sh` 用 `dotnet build` 编译 DLL，并用 Godot headless 运行 `tools/pck_builder/build_pck.gd` 把 `mod_manifest.json` 打包成 `.pck`。云机装上与 `manifest.json` 同版本的 Godot 后，就可以直接产出完整可安装的 Mod 三件套（`STS2AIAgent.dll`、`STS2AIAgent.pck`、`mod_id.json`）：

```bash
# 云机：自动按已解包 manifest.json 里的 godotVersion（如 4.5.1）安装 Linux Godot
bash scripts/setup-cloud-dev.sh --no-apt --no-dotnet --no-uv --no-pwsh --no-sync --with-godot
. ~/.sts2-cloud.env

# 云机：编译并暂存到 build/mods/STS2AIAgent/（无需游戏目录）
bash scripts/build-mod.sh --configuration Release --data-dir "$STS2_DATA_DIR" --skip-install
```

云机编好后，可以直接拉回本机 Windows 游戏目录里实机验证（先关闭游戏，避免 DLL 占用）：

```powershell
# 本机 Windows（把云机暂存的 3 个产物拷进游戏 mods 目录）
scp cloud:~/STS2-Agent/build/mods/STS2AIAgent/* "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\"
```

> 使用 `dotnet build` 时参数请写 `-p:Sts2DataDir=...` 而不是 `/p:...`：在 Git Bash / MSYS 下 `/p:` 会被路径改写导致 MSBuild 报 `MSB1008`。

### 3.5 WSL / Dev Container 捷径

- **WSL**：不必打包，直接把 `STS2_DATA_DIR` 指向 Windows 盘上的游戏目录即可（`export STS2_DATA_DIR="/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64"`）。POSIX 路径解析器刻意不猜测 `/mnt/c`，保持 `mcp_server/tests/test_posix_script_portability.py` 的单点布局约束。
- **Dev Container（`.devcontainer/`）**：只要本机已经执行过 `scripts/pack-game-assemblies.ps1`（产物在工作区 `build/game-assemblies/` 内），容器创建时的 `postCreateCommand` 会自动校验并解包进容器的 `/root/.sts2/game-assemblies`，打开容器即可编译与跑全套离线检查。

## 4. L2：实机验证与发版边界

### 4.1 推荐路线：云机开发 + SSH 隧道连本机游戏

对「改 MCP、调状态/决策逻辑、看实机响应」来说，把游戏留在本机 Windows，通过 SSH 反向隧道把本机 Mod HTTP API 映射到云机，成本最低也最稳定：

```bash
# 本机 Windows 把本地 8080 转发到云机 8080
ssh -R 8080:127.0.0.1:8080 you@cloud

# 云机上即可直接连本机运行中的游戏
export STS2_API_BASE_URL=http://127.0.0.1:8080
python3 scripts/run_sts2_validation.py state-summary
bash scripts/start-mcp-stdio.sh
```

### 4.2 云机直接安装游戏本体（可选，成本高）

如果确实要在 Linux 云机上直接跑游戏，先用 `steamcmd` 核对账号下是否有原生 Linux depot：

```bash
steamcmd +login <你的账号> +app_info_update 1 +app_info_print 2868840 +quit | grep -E '"oslist"|"depot"'
```

- 原生 Linux 版：`lib-sts2-paths.sh` 已带 `data_sts2_*` 通配兜底，有桌面或 Xvfb（`DISPLAY` 存在）时可按 `start-game-session.sh` 启动并跑 `run_sts2_validation.py`。
- Windows 版 + Proton：需 2.95 GB 传输、Proton 层与软件渲染（Xvfb + lavapipe），帧率与截图保真度都很低，本仓库未验证该路线。

### 4.3 正式发版仍需本地 Windows

`scripts/package-release.ps1` 与 `scripts/package-steam-workshop.ps1` 在打包前会调用 `scripts/test-release-compatibility.ps1`，用同一份发布 DLL 在**最低支持版本**和**目标版本**两份真实游戏目录上各自拉起隔离实例并做 smoke 验证（详见 [release-readiness.md](release-readiness.md)）。因此：

- **云机适合**：日常编码、全部离线单测与闸门、单/双版本编译检查、反编译调研、`build-mod.sh` 预打包测试。
- **本地 Windows 负责**：实机交互验收、双版本兼容性打包发布、Steam 创意工坊上传。

## 5. 能力边界速查

| 能力 | L0 | L1 | L2 | 备注 |
|------|:--:|:--:|:--:|------|
| `STS2AIAgent.Tests` 核心单测 | ✓ | ✓ | ✓ | 不引用游戏程序集，也不需要 Godot |
| `mcp_server` 单测与全部 CI 离线闸门 | ✓ | ✓ | ✓ | 10 项离线检查，`pwsh` 缺席时仅跳过 MCP profile 脚本 |
| `dotnet build` 出 Mod DLL（支持双版本） | ✗ | ✓ | ✓ | `STS2_DATA_DIR` + 可选 `STS2_MIN_DATA_DIR` |
| `ilspycmd` 查/反编译 `sts2.dll` | ✗ | ✓* | ✓* | *加装 `--with-ilspy` |
| `build-mod.sh --skip-install` 出 DLL + PCK | ✗ | ✓* | ✓* | *加装 `--with-godot`（自动对齐游戏 Godot 版本） |
| `run_sts2_validation.py` 实机验证 | ✗ | ✗ | ✓ | 需要游戏在线，或通过 SSH 隧道连本机游戏 |
| 正式双版本发版与创意工坊上传 | ✗ | ✗ | ✗ | 需在本地 Windows 有游戏的机器上执行 |

## 6. 常见问题

- **`dotnet` 报找不到匹配的 SDK**：机器只有 10.x 而没有 9.0.x。按 `dotnet-install.sh --channel 9.0` 并存安装即可，无需卸载 10。
- **`pwsh` 启动立即退出**：缺 ICU 运行库。Debian/Ubuntu 上执行 `sudo apt-get install -y libicu-dev`（或对应 `libicu*`）。
- **`uv sync` 卡在下载 Python**：可设 `UV_PYTHON_INSTALL_MIRROR`，或先手工 `uv python install 3.12`。
- **`check-cloud-dev.sh` 提示 `data directory missing`**：运行 `unpack-game-assemblies.sh`，然后新开 shell 或执行 `. ~/.sts2-cloud.env`。
- **云机全新 clone 下没有 `AGENTS.md`**：正常现象，`AGENTS.md` 被 `.gitignore` 忽略，不属于检出完整度条件；架构与文件索引见 [project-map.md](project-map.md)。
- **在 Windows 上测试 `.sh` 脚本**：语法检查和 `test-lib-sts2-paths.sh` 可在 Git Bash 运行，但真实 Windows 编译部署请用 `.ps1`（原因见 [build-and-env.md](build-and-env.md)）。

## 7. 相关文件

- `scripts/setup-cloud-dev.sh` — L0 一键引导（可选 `--with-ilspy`、`--with-godot`、`--with-node`、`--with-gh`）
- `scripts/check-cloud-dev.sh` — 三层能力报告 + CI 对齐的离线/双版本编译检查
- `scripts/pack-game-assemblies.ps1` — 有游戏的 Windows 机器上提取最小程序集与元数据压缩包
- `scripts/unpack-game-assemblies.sh` — 云机校验解包（支持 `--as-min`、`--slot`、`--decompile`）
- `scripts/check-game-abi.ps1` / `scripts/check-game-abi.sh` — 离线 ECMA-335 ABI、反射成员与跨分支能力审计包装脚本
- `.devcontainer/` — 固化 L0 工具链的容器配置（自动解包工作区 `build/game-assemblies/`）
- [build-and-env.md](build-and-env.md) — 构建、部署与环境变量总表
- [project-map.md](project-map.md) — 仓库架构与代码地图
- [reverse-engineering.md](reverse-engineering.md) — `sts2.dll` 反编译与运行时笔记
- [release-readiness.md](release-readiness.md) — 正式发版与双版本验收流程
