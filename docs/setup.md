# STS2 AI Agent Mod - 环境与运行准备

## 目的

记录当前开发机上已确认的环境事实、缺失依赖和建议安装步骤，避免后续实现阶段基于错误前提推进。

更新时间：2026-03-10

---

## 已确认的本机事实

### 工作区

- 项目根目录：`<repo-root>`
- 当前仓库已初始化 Git，但尚未有代码骨架

### 游戏安装

- 游戏目录：`C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2`
- 程序集目录：`C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64`
- 主程序集：`sts2.dll`
- `sts2.dll` 大小：`8,870,912` bytes
- 游戏版本：`v0.98.2`
- 发布提交：`f4eeecc6`
- 发布日期：`2026-03-06T15:52:37-08:00`

### 运行时与依赖

- `sts2.runtimeconfig.json` 指向 `net9.0`
- 游戏自带 `Microsoft.NETCore.App 9.0.7`
- 游戏目录内已确认存在：
  - `0Harmony.dll` `2.4.2.0`
  - `GodotSharp.dll` `4.5.1.0`
  - `System.Net.HttpListener.dll`

### 当前开发机工具状态

| 工具 | 状态 | 说明 |
|------|------|------|
| `.NET Runtime` | 已安装 | 主机上存在 `6.0.16` 和 `8.0.14` 运行时 |
| `.NET SDK` | 已安装 | `9.0.311` |
| `python` | 已安装 | 解释器位于 `<python-install>/python.exe` |
| `py` | 已安装 | `py -3.11 --version` 返回 `Python 3.11.9` |
| `uv` | 已安装 | `0.10.9` |
| `ilspycmd` | 已安装 | `9.1.0.7988` |
| `godot` CLI | 已安装 | `Godot 4.5.1 Mono`，当前通过显式路径调用 |

### Mod 目录现状

- 游戏根目录下当前未发现现成的 `mods/` 目录
- 通过正式反编译已确认 ModManager 会扫描 `Path.Combine(directoryName, "mods")`
- 也就是说最终目录就是：`C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods`
- 若目录不存在，可在后续安装最小 Mod 时手动创建

---

## 当前阻塞项

以下项目仍会阻塞完整联调阶段：

1. 把 `/state` 和 `/action` 的最小实现补进 Mod
2. 在真实游戏运行中验证更多路由，而不只是 `/health`
3. 为后续 `.pck` 资源扩展建立稳定的 Godot 打包流程

---

## 建议安装顺序

1. 安装 `.NET 9 SDK`
2. 安装 `Python 3.11+`
3. 安装 `uv`
4. 安装 `ilspycmd`
5. 安装 `Godot 4.5.1 Mono` 编辑器

建议完成后执行以下检查：

```powershell
dotnet --list-sdks
py -3.11 --version
uv --version
ilspycmd --version
godot --version
```

---

## 建议的目录约定

### 项目目录

- `STS2AIAgent/`：C# Mod 项目
- `mcp_server/`：Python MCP Server
- `docs/`：协议、逆向和运行文档
- `extraction/`：反编译产物和临时分析结果

### 逆向输出目录

推荐将反编译结果输出到：

`<repo-root>/extraction/decompiled`

---

## 首次验证清单

### 环境验证

- `dotnet --list-sdks` 能看到 `9.0.311`
- `py -3.11 --version` 能返回 `Python 3.11.9`
- `uv --version` 能返回 `0.10.9`
- `ilspycmd --version` 能返回 `9.1.0.7988`
- `"<godot-console-exe>" --version` 能返回 `4.5.1`

### 游戏事实验证

- `release_info.json` 仍为 `v0.98.2`
- `sts2.runtimeconfig.json` 仍声明 `net9.0`
- `0Harmony.dll` 和 `GodotSharp.dll` 仍存在

### Mod 安装验证

- 已确认 `mods/` 最终目录为游戏根目录下的 `mods/`
- `.dll` 与 `.pck` 需要同目录、同名并列放置
- 可将一个最小 Mod 放入目录并被游戏识别
- 若日志出现 `user has not yet seen the mods warning`，需要在 `settings.save` 中启用：
  - `"mod_settings": { "mods_enabled": true }`

### 当前可用构建命令

```powershell
dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj"
```

### 当前可用自动化脚本

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/build-mod.ps1"
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-mod-load.ps1"
```

- 运行 `build-mod.ps1` 前，请先通过 `-GodotExe` 传入 Godot 控制台可执行文件，或设置 `GODOT_BIN` 环境变量
- `build-mod.ps1` 会构建 DLL、打包最小 `.pck`，并安装到游戏 `mods/` 目录
- `test-mod-load.ps1` 会短启动游戏并轮询 `http://127.0.0.1:8080/health`

### 已完成验证

- 已生成并安装：
  - `C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/STS2AIAgent.dll`
  - `C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/STS2AIAgent.pck`
- 已通过短启动验证 `/health` 返回 200
- 已确认游戏日志中存在 Mod 扫描、初始化和 HTTP 服务启动记录

---

## 下一步

1. 实现 `GET /state` 的最小字段
2. 实现一个最小 `POST /action`
3. 为 Mod 增加更细日志和错误返回
4. 开始准备 MCP 端最小客户端联调
