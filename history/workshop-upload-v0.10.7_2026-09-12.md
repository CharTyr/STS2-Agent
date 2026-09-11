# v0.10.7 Workshop 上传记录（2026-09-12）

上传时间：2026-09-12 01:11（本地）。物品：`3796486050`（STS2 AI Agent），更新既有物品，未新建。

## 结果（Steam Web API 复核）

| 字段 | 值 | 与本地对照 |
| --- | --- | --- |
| `publishedfileid` | 3796486050 | 与工作区 `mod_id.txt` 一致 |
| `title` | STS2 AI Agent | 未改 |
| `visibility` | 0（公开） | 更新后仍为公开，未被回落为私有 |
| `file_size` | 1088228 | 与 `content/` 目录字节和**完全相等**（1049088+608+381+3514+34637） |
| `time_updated` | 1789146663 → 2026-09-12 01:11:03 | 上传后即时刷新 |
| `tags` | Tools & APIs / Utility / QoL | 三个标签都在位 |
| `description` | 2418 字节，含 `v0.111.0`、`Play with Mods`、`AI teammate`、`F8` | 未被上传覆盖 |
| `consumer_app_id` | 2868840 | 正确 |

## 内容构成

工作区：`build/steam-workshop/sts2-ai-agent-v0.10.7/`（由 `scripts/package-steam-workshop.ps1` 生成，`-PublishedFileId 3796486050`）。

- `content/STS2AIAgent.dll`（1049088 字节，SHA256 `E389B9EB602ED8B3136A27F5D5E16E35C97ABA84BE92DA9589DA6B5182093B4D`）
- `content/STS2AIAgent.pck`（608 字节）
- `content/STS2AIAgent.json`（版本号 `0.10.7`、`min_game_version 0.111.0`，由打包脚本从 mod_manifest 断言后写入）
- `content/README.md`（玩家用双语说明）、`content/LICENSE`

## 本次改动（相对工坊上的 v0.10.6）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.10.7 段。玩家能直接感受到的三条：

1. **离开对局会停止自动游玩**（此前循环内重置边界导致该判定永不触发，玩家退回主菜单后 AI 会继续在菜单里操作）。
2. **单人开自动游玩时不再提示「可以邀请 AI 队友」**，且运行中会显示「队友正在行动」而不是一直显示「正在请求模型」。
3. **主动发言不会在说了 6 句后永久沉默**（额度改为每次开始自动游玩重新发放）。

## 过程注意事项（复用给下次）

1. **重启 Steam 后第一次上传仍会失败**：本次重启后立即上传，卡在 `k_EItemUpdateStatusPreparingContent` 约 60 秒后报 `k_EResultFail`。开启本机代理（127.0.0.1:10808）后重跑同一条命令，一次成功（约 25 秒走完 PreparingContent → UploadingContent → UploadingPreviewFile → CommittingChanges）。
2. **上传必须在分离进程里跑**：`ModUploader.exe` 与 Steam 握手期间对父进程生命周期敏感，用前台 `&` 调用时被轮询打断会直接失败（本次 21:19 那次即失败于此）。改用 `Start-Process -WindowStyle Hidden` 后台启动后再轮询日志文件，稳定完成。
3. 日志在 `%LOCALAPPDATA%/sts2-mod-uploader/win-x64/mod-uploader.log`，每次上传前会清空，可直接 `-Tail` 观察阶段推进。

## 未做

- 未打 GitHub tag、未建 GitHub Release：本次只按要求更新工坊。需要时补 `v0.10.7` tag（发布提交 `f9330ba`）与 Release 资产。
- 未在工坊客户端侧做订阅加载冒烟（沿用 2026-09-09 的结论：上传后重启 Steam 可见新内容）。

