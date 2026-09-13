# v0.12.1 发布与工坊上传记录（2026-09-13）

## 结论

GitHub Release 与 Steam 工坊内容**都已完成**；工坊的**简体中文列表仍然是旧版**（见「未做」）。

| 项 | 值 |
| --- | --- |
| 版本 | `0.12.1` |
| 发布分支 / PR | `codex/release-v0.12.1` → #95 |
| 发布提交 | `f0f3b2a`（`Release v0.12.1`） |
| 合并提交（= tag 目标，也是当前 `main`） | `640c343` |
| CI | `f0f3b2a` 的 push（34738358684）与 pull_request（34738367623）两个 Validate run 均 success；合并提交 `640c343` 的 Validate run（34738442845）success |
| Tag | `v0.12.1`（annotated → commit `640c343`） |
| Release | https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.1 ，资产 `sts2-ai-agent-v0.12.1-windows.zip`（539550 字节，SHA256 `26C6FE8445BA730B193F7697A26EB09BCD52A7532A09F4D422E14F8769BC16C2`） |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-13 12:41:42`，内容 id `hcontent_file = 3644578850671780412` |
| 工坊 `file_size` | `1213444` —— 与本地内容字节和**完全相等**（dll 1174016 + pck 608 + json 381 + README 3802 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 标签 | Tools & APIs / Utility / QoL（未变） |
| 默认（英文）说明 | 2672 字节，未被上传覆盖 |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`，最后一项用 `uv lock` 重新生成），`scripts/preflight-release.ps1` 全部步骤通过（含 `check_release_metadata.py` 与 `docs/api.md` 的 `mod_version`）。

## 内容构成

工作区：`build/steam-workshop/sts2-ai-agent-v0.12.1/`（由 `scripts/package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote "…"` 生成）。

| 文件 | 字节 | SHA256（前 16） |
| --- | --- | --- |
| `STS2AIAgent.dll` | 1174016 | `A69807ED67403316` |
| `STS2AIAgent.pck` | 608 | `DB8EA18A1A1E2297` |
| `STS2AIAgent.json` | 381 | `41B06E7212162C5F` |
| `README.md` | 3802 | `6A18A53359E678A4` |
| `LICENSE` | 34637 | `55515BACC3BD3796` |

## 本次改动（相对工坊上的 v0.12.0）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.1 段。这一版是暂停边界：

1. **暂停菜单不再被当成「开着的 capstone」**——它的选项列表里此前包含「放弃」。
2. **从暂停菜单进入的设置 / 百科大全 / 卡牌总览 / 遗物收集 / 药水研究所 / 角色数据 / 历史记录各自报出真实屏幕名**，不再报下面的房间，也不再带出 `Hitbox` 之类的控件名当选项；`choose_capstone_option` 在这些屏上都是 409。
3. **这些页面重新有了返回动作**（`close_main_menu_submenu` 退一级），而**暂停那一页永远不可关**。
4. **写错的 `--clientId` 不再毁掉联机存档**（读档前比对 `players[].net_id`）。

## 上传过程（可复用）

1. 打包工作区（12:39:02 生成，`content/` 字节和 1213444）。
2. 游戏与 ModUploader 都不在运行，Steam 在运行。
3. `Start-Process -WindowStyle Hidden -PassThru` 分离启动 `ModUploader.exe upload -w <workspace> -i 3796486050`，stdout/stderr 重定向到 `%TEMP%`；进程环境带 `HTTP_PROXY` / `HTTPS_PROXY`（本机 127.0.0.1:10808，系统代理也已指向它，Steam 域名在旁路列表里）。
4. **一次成功，没有重试**：12:41:34 启动 → 状态依次走到 `PreparingContent` → `UploadingContent`（1175005 字节，是 dll + pck + json）→ `CommittingChanges` → 12:41:42 `Successfully uploaded`。前两次上传遇到的 manifest 下载超时没有复现。
5. 复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags` / `description`，`file_size` 与本地内容字节和完全相等。

## 未做

- **工坊简体中文列表仍未更新**。2026-09-13 重新抓取 <https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050&l=schinese> 的正文（2303 字符）确认：页面上仍是旧版文案，**不含** v0.11.0 就写进仓库的「界面跟随游戏语言」「不加新牌、不改数值、不动你的存档」，**也不含** v0.12.0 的两条联机行（「继续上次没打完的联机局」「也可以不让队友自己选角」）。仓库内的 `steam-workshop/description.zh-CN.txt` 是当前应粘贴的正文。
  - 本次尝试过自动补：`ModUploader upload` 只有 `-w` / `-i` 两个选项，没有语言参数（`upload --help` 输出如此），工坊的英文说明是 VDF 里的单个 `description` 字段，中文列表只能在工坊网页端编辑；本机的 Computer Use 运行时报告没有可用的浏览器（`cua.getState()` → `browsers: []`，并带一个 `nodeRepl.fetch request failed`），因此没能在本轮完成。
- 未做订阅加载冒烟：沿用 2026-09-09 的结论（上传后重启 Steam 可见新内容）。
- 本版没有单独再做一遍完整实机验收：v0.12.1 的内容与 `04748f6` / `e7268a2` 同一份代码，实机证据就是 #93 的两轮隔离验收（`build/validation-2026-09-13/verify-capstone-pages.log`，FAILURES: 0，记录在 [docs/live-validation-checklist.md](../docs/live-validation-checklist.md)）。仍需实机确认的项以该清单为准。

## 注记

- 与 v0.12.0 相同，发布提交经 PR 合并，因此 tag 指向**合并提交** `640c343` 而不是发布提交 `f0f3b2a`；两者树内容相同。
- 本轮没有新增标签或改可见性，工坊物品与 v0.12.0 相比只有内容与 changenote 变化。

