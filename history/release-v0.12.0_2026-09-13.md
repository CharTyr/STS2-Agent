# v0.12.0 发布与工坊上传记录（2026-09-13）

## 结论

GitHub Release 与 Steam 工坊内容**都已完成**；工坊的**简体中文列表未更新**（见下方「未做」，这一项从 v0.11.0 起就漏了）。

| 项 | 值 |
| --- | --- |
| 版本 | `0.12.0` |
| 发布分支 / PR | `codex/release-v0.12.0` → #86 |
| 发布提交 | `69602c9`（`Release v0.12.0`） |
| 合并提交（= tag 目标，也是当前 `main`） | `69887a3` |
| CI | `69602c9` 的 push 与 pull_request 两个 Validate run 均 success |
| Tag | `v0.12.0`（annotated，tag 对象 `45a3931` → commit `69887a3`） |
| Release | https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.0 ，资产 `sts2-ai-agent-v0.12.0-windows.zip`（533493 字节，SHA256 `C2B1F3229D6CF8E7D757D571AAF717004DDBCAFD4F8AAE1276A86A54A3AFA685`） |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-13 02:25:02`，manifest `3382317012139913714` |
| 工坊 `file_size` | `1202181` —— 与本地内容字节和**完全相等**（dll 1162752 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 标签 | Tools & APIs / Utility / QoL（未变） |
| 默认（英文）说明 | 2670 字符，含新增的两条 co-op 行 |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`，最后一项用 `uv lock` 重新生成），`check_release_metadata.py` 通过。`docs/api.md` 的 `mod_version` 示例一并更新——`api-facts` 门禁先报错、再修正，符合该门禁的设计意图。

本轮同时更新了工坊源文案：`description.en.txt` / `description.zh-CN.txt` 的「能得到什么」列表各加两条（继续联机存档、队友角色由你选），`content-readme.md` 的对应步骤也补了说明。

## 内容构成

工作区：`build/steam-workshop/sts2-ai-agent-v0.12.0/`（由 `scripts/package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote "…"` 生成）。

| 文件 | 字节 | SHA256（前 16） |
| --- | --- | --- |
| `STS2AIAgent.dll` | 1162752 | `45E5E1BEA832CF7C` |
| `STS2AIAgent.pck` | 608 | `1C46C2DA267402B9` |
| `STS2AIAgent.json` | 382 | `3CF7BD14EAEF89DA` |
| `README.md` | 3802 | `6A18A53359E678A4` |
| `LICENSE` | 34637 | `55515BACC3BD3796` |

## 本次改动（相对工坊上的 v0.11.0）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.0 段。玩家能直接感受到的三条：

1. **没打完的联机局可以接着打**：主菜单出现「继续 AI 队友」，读档并把本地队友拉回来（#83）。
2. **队友角色可以由你选**：`CompanionAutoSelectCharacter=false` 时队友停在选角界面等你，且不计入 5 分钟启动预算（#84）。
3. **状态可信度收口**：奖励浮层正名为 `REWARD`、卡牌查看屏有了自己的名字、越界索引不再静默点错牌、游戏侧等待都有期限。

## 上传过程（可复用）

本次与 v0.11.0 记录的模式一致，但**只有一次失败重试**，且重试瞬间完成：

1. 关掉游戏（本次游戏未运行），优雅重启 Steam（`steam.exe -shutdown` → 等待退出 → 启动）。
2. `Start-Process -WindowStyle Hidden -PassThru` 分离启动 `ModUploader.exe upload -w <workspace> -i 3796486050`，stdout/stderr 重定向到 `%TEMP%`。
3. 第 1 次尝试：02:08:03 开始 → 02:24:44 失败（`Failed to download manifest "steampipe-partner.akamaized.net/…" (timeout)`），与 v0.11.0 那次同一根因：Steam 客户端拉取物品当前 manifest 时在 Akamai 域上超时。
4. 第 2 次尝试：02:24:54 启动 → **02:25:01 上传完成**（`Uploaded new content ( ManifestID 3382317012139913714 )`）→ 02:25:02 `Upload finished for workshop item 3796486050 : OK`。
5. 复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags`，与本地内容字节和完全相等。

## 未做

- **工坊简体中文列表未更新**。抓取 <https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050&l=schinese> 的原文显示，页面上的中文列表是**旧版**：开头为「带一个 AI 一起玩或者AI直接操控游戏游玩。」，且**不含** v0.11.0 就写进仓库的「界面跟随游戏语言」一行、也不含「不加新牌、不改数值、不动你的存档」。说明「上传后粘贴 zh-CN 列表」这一步在 v0.11.0（甚至更早）就没有执行。仓库内的 `steam-workshop/description.zh-CN.txt` 是当前应粘贴的正文。
- 未做订阅加载冒烟：沿用 2026-09-09 的结论（上传后重启 Steam 可见新内容）。
- 本版发布前未做完整实机验收；仍需实机确认的项见 [docs/live-validation-checklist.md](../docs/live-validation-checklist.md)。

## 注记

- 与前几次「直接向 `main` 提交发布提交」不同，本次因为 `main` 的分支保护（必须走 PR），发布提交经 #86 合并，因此 tag 指向**合并提交** `69887a3` 而不是发布提交 `69602c9`；两者树内容相同。
