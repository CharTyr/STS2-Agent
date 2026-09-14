# STS2 AI Agent Mod - 常见问题

## Steam 初始化失败：`No appID found`

### 现象

启动 `SlayTheSpire2.exe` 时弹出 Steam 错误：

```text
k_ESteamAPIInitResult_FailedGeneric:
No appID found. Either launch the game from Steam, or put the file
steam_appid.txt containing the correct appID in your game folder.
```

### 原因

- 直接运行游戏 EXE 时，Steam API 可能拿不到当前游戏的 AppID。
- `Slay the Spire 2` 的本机 Steam AppID 已确认是 `2868840`。

### 解决方式

任选一种：

1. 从 Steam 客户端启动游戏。
2. 在游戏根目录创建 `steam_appid.txt`，内容写入：

```text
2868840
```

游戏根目录：

```text
C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2
```

### 项目内脚本行为

- `scripts/test-mod-load.ps1` 解析 AppID 时按「`-AppManifestPath` 参数 → `STS2_APP_MANIFEST` 环境变量 → 探测本机 Steam 库 → 默认安装路径」的顺序找 `appmanifest_2868840.acf`，所以游戏装在别的盘也能直接用。
- 如果游戏目录缺少 `steam_appid.txt`，脚本会自动创建，避免短启动测试直接失败。
- 如果日志里出现 `user has not yet seen the mods warning`，说明这是 Steam 存档路径上的首次 Mod 加载确认；脚本会给出提示，这时再运行一次即可。

### 备注

- 这个问题和当前 Mod 的 `/health` 路由无关，属于启动方式问题。
- 如果仍然失败，先确认 Steam 客户端正在运行，并检查 `steam_appid.txt` 是否位于游戏根目录而不是 `data_sts2_windows_x86_64` 子目录。
