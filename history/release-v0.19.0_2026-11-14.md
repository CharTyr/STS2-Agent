# v0.19.0 发布记录（2026-11-14）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.19.0（五个版本文件一致） |
| 发布 PR | [#239](https://github.com/CharTyr/STS2-Agent/pull/239)（dev → main） |
| tag / 源码提交 | `v0.19.0` → `73955b7`（PR #239 合并提交）；Release 提交 `ffe8f49` |
| GitHub Release | [v0.19.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.0)，资产 `sts2-ai-agent-v0.19.0-windows.zip`，971610 字节，SHA256 `887D9AEFDC11C9042924FB6EDB1175A2C5664D820275726BB6105188A3466F79` |
| Steam 工坊 | 物品 `3796486050`；`workshop_log.txt` 于 2026-11-14 01:24:28 记 `Upload finished for workshop item 3796486050 : OK`（`time_updated` 1794590663 = 本地 01:24:23；`file_size` 2219155 = 本地 content 字节和，5 个文件；`hcontent_file` ManifestID `8677832952189501151`）。上传期间按本机惯例将系统代理 `ProxyEnable` 临时置 0，完成后已恢复为 1。 |
| DLL SHA256 | `C1213B26D8436584CF87AC66E771584D5890E17F376479E398784C7D252F9C41`（GitHub 包与工坊 content 同一份，自 Release 提交 `ffe8f49` 干净构建） |
| 构建指纹 | GitHub 包：33 个文件、2844471 字节；工坊 content：5 个文件、2219155 字节；两者 `source_tree_dirty=false` |

## 内容

适配游戏 v0.113.0「Dread & Blitz」：`open_character_select` 增 `mode`（standard/blitz/dread/custom）统一四种开局屏；run 载荷感知 GameMode（`run.game_mode` / `dominion_id` / `is_custom_game` / `is_rotating_character`）；lobby 玩家暴露 `max_blitz_ascension` / `is_rotating_dread`；Blitz 三选一商店（`shop.is_blitz`、`blitz_choices_remaining`、`blitz_entries`、`buy_blitz_entry`）；`run.timeline.slots[]`（EpochSlotData）；Fool 暴露 `arrow_slots` 与战斗 arrows/melody 字段。详见 [CHANGELOG.md](../CHANGELOG.md) 的 0.19.0 段。

## 验证与限制

- 实机（v0.113.0 隔离档 clientId 2026111401、端口 18080、debug 开）：四种 run mode 开局、Blitz 三选一购买流、标准商店回归（MerchantSynchronizer.PurchaseLocalEntry）、timeline 59 槽、/data/characters `arrow_slots` FOOL=1、GAME_OVER drain、`mode=bogus` 400、锁定档 409——全过。完整逐项证据在打包机 `build/live-2026-11-14/report.md`（未入库）。
- 实机发现并修复两处 v0.113.0 回归：Dread/custom lobby `/state` 500（轮转槽 `NCharacterSelectButton` 带 `Character == null`，已过滤）；`open_character_select mode=custom` 409（节点名 `%CustomRunButton`）。
- 离线：C# 288 PASS / 0 FAIL；Python 456 PASS；api-doc、api-facts、api-schema、arch-facts、doc-links、docs-tracked、packaged-links、ps1/sh-syntax 等 12 项闸门全过。
- 打包机（DSH 沙箱）的环境限制与历史一致：工作区内的二进制跑不起 `HttpListener`/`Directory.CreateDirectory`（TEMP 落在仓库外不可写），测试进程移到仓库外临时目录后全部通过；Python 侧四个「无 checkout」夹具同理，干净 worktree 全过。判为环境限制，非产品回归。
- **实机边界（同 CHANGELOG）**：模型相关项（服务商额度/认证）、3–4 人联机、工坊简体中文列表（需网页端手工粘贴 `description.zh-CN.txt`）未验收。

## 工坊上传

1. 从 `ffe8f49`（dev 的 Release 提交）用 `package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote <v0.19.0 note>` 打包到 `build/steam-workshop/sts2-ai-agent-v0.19.0/`（content 2,219,155 字节，5 个文件）。
2. 保存 `ProxyEnable`/`ProxyServer` 到 `build/steam-workshop/saved-proxy.json`，将 `ProxyEnable` 置 0，重启 Steam 待 `Logged On`，重跑 `ModUploader.exe upload -w <workspace> -i 3796486050`：约 11 秒经 `UploadingContent`（2179550 字节）完成，`workshop_log.txt` 记 `Upload finished for workshop item 3796486050 : OK`（01:24:28）。上传完成后恢复 `ProxyEnable=1`。
3. 记录上传前/后的 `Upload finished … : OK` 行数（4 → 5），确认是本上传新增的 OK，不是历史行。

## 仍需手工

- 工坊**简体中文列表**仍需在网页端粘贴 [description.zh-CN.txt](../steam-workshop/description.zh-CN.txt)；ModUploader 无语言参数。
- 补做上表实机边界中列出的未验证项。
