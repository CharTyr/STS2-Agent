# v0.12.4 Workshop 上传记录（2026-09-15）

> 历史快照：本文件是只更新工坊那一次（v0.12.4，未打 tag）的记录，不代表当前状态；当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

上传时间：2026-09-15 19:37:54 → 19:38:00（本地）。物品：`3796486050`（STS2 AI Agent），更新既有物品，未新建。

工作区：`build/steam-workshop/sts2-ai-agent-v0.12.4/`，由 `scripts/package-steam-workshop.ps1 -PublishedFileId 3796486050` 生成。
来源提交：`4b8e7c5`（PR #133 的合并提交，版本升级 `bd82677` 在其中）。

## 结果（Steam Web API 复核）

| 字段 | 值 | 与本地对照 |
| --- | --- | --- |
| `result` | 1 | 物品存在 |
| `publishedfileid` | 3796486050 | 与 `-i` 参数一致 |
| `title` | STS2 AI Agent | 未改 |
| `visibility` | 0（公开） | 更新后仍为公开，未被回落为私有 |
| `file_size` | 1233413 | 与 `content/` 目录字节和**完全相等** |
| `time_updated` | 1789472280 → 2026-09-15 19:38:00 | 上传后即时刷新 |
| `tags` | Tools & APIs / Utility / QoL | 三个标签都在位 |
| `consumer_app_id` | 2868840 | 正确 |
| 描述正文 | 工坊页面复核含 `Play with Mods` / `AI teammate` / `F8` / `Continue a saved co-op run` / `v0.111.0` | 未被本次上传覆盖 |

上传当日的物品指标（同一次 API 调用）：views 514、subscriptions 74、favorited 14、lifetime_subscriptions 153。

## 内容构成

- `content/STS2AIAgent.dll`（1193984 字节，SHA256 `0134C8BC10A76F15C71CAFFEAC7D818D71744111A4045DDF52AA80A5AA51A5BF`）
- `content/STS2AIAgent.pck`（608 字节）
- `content/STS2AIAgent.json`（版本号 `0.12.4`、`min_game_version 0.111.0`，由打包脚本从 `mod_manifest.json` 断言后写入）
- `content/README.md`（3802 字节）、`content/LICENSE`（34637 字节）
- `steam-workshop.vdf` 的 `changenote` 为新的 v0.12.4 文案，`visibility` 0

## 本次改动（相对工坊上的 v0.12.3）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.4 段。玩家能直接感受到的两条：

1. 邀请 / 继续队友时请求立刻返回 `pending`，慢启动不再像客户端超时；双开进行中这两个动作不再出现在可用动作里，第二发也无法把上一轮的成功当成自己的结果回报。
2. 请求计费准确：每一次真正发出的模型调用都被记账（聊天与主动发言失败、工具循环里的意外异常都不再漏记），游玩页的请求上限与实际花费一致。

## 过程注意事项（复用给下次）

1. **本次连续两次卡在 `Upload starting` 且毫无推进**：19:21 那次 10 分钟、19:31 那次 4 分钟，Steam 侧 `workshop_log.txt` 既不报错也不进入 `k_EItemUpdateStatusPreparingContent`；`%LOCALAPPDATA%/sts2-mod-uploader/win-x64/mod-uploader.log` 只在结束时才写，期间看不到阶段。
2. **解法是重启 Steam 客户端**：`steam.exe -shutdown` → 等 `steam` 进程退出 → 重新启动 → 等 `connection_log.txt` 出现 `RecvMsgClientLogOnResponse() : ... 'OK'`，再跑同一条上传命令，10 秒内走完 UploadingContent → UploadingPreviewFile → CommittingChanges 并成功。
3. 上传命令是 `& ModUploader.exe upload -w <workspace> -i 3796486050`，进程环境带 `HTTP_PROXY` / `HTTPS_PROXY=http://127.0.0.1:10808`。代理可用性可先自检：`curl -x http://127.0.0.1:10808 https://steamcommunity.com/` 本次返回 200，说明卡住与代理无关。
4. 尾部出现的 `k_EItemUpdateStatusInvalid` 是 SDK 状态枚举的终止值，随后仍打印 `Successfully uploaded`，不代表失败；本次与历史成功记录一致。

## 未做

- 未打 GitHub tag、未建 GitHub Release：本次只按要求更新工坊。需要时补 `v0.12.4` tag 与 Release 资产。
- 未在工坊客户端侧做订阅加载冒烟（沿用 2026-09-09 的结论：上传后重启 Steam 可见新内容）。
- 工坊简体中文列表仍是旧版（自 v0.11.0 起），待手工粘贴 `steam-workshop/description.zh-CN.txt`——`ModUploader` 没有语言参数。
