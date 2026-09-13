# v0.12.3 Workshop 上传记录（2026-09-13）

## 结论

工坊物品已更新到 v0.12.3，**未打 GitHub tag、未建 GitHub Release**（沿用 [v0.10.7 的先例](workshop-upload-v0.10.7_2026-09-12.md)：只发工坊、版本号照升，GitHub 侧留待后续版本一并发布）。

| 项 | 值 |
| --- | --- |
| 版本 | `0.12.3` |
| 发布分支 / PR | `codex/workshop-v0.12.3` → #108 |
| 发布提交（合并后 = `main`） | `b0217b0`（`Release v0.12.3 for the Steam Workshop`） |
| CI | `5f3df09` 的 push（34762753954）与 pull_request（34762757419）两个 Validate run 均 success |
| Tag / Release | **无**（本轮只发工坊） |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-13 22:31:22`，内容 id `hcontent_file = 5284257893537057643` |
| 工坊 `file_size` | `1227269` —— 与本地内容字节和**完全相等**（dll 1187840 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 标签 | Tools & APIs / Utility / QoL（未变） |
| 默认（英文）说明 | 2672 字节，未被上传覆盖 |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`，最后一项用 `uv lock` 重新生成），`scripts/preflight-release.ps1` 全部步骤通过（含 `check_release_metadata.py` 与 `docs/api.md` 的 `mod_version`）。

## 为什么必须升版本号

工坊上的构建就是版本号所指向的那份。PR #106 改了 mod 代码（`AgentOverlayHost.cs`、`Loc.Strings.Ui.cs`），如果仍以 `0.12.2` 重传，工坊内容就会与同名的 `v0.12.2` GitHub 产物不一致，而 `/health` 只能报一个版本号。升一个补丁号，"我手上是哪一版" 才有答案。

## 内容构成

工作区：`build/steam-workshop/sts2-ai-agent-v0.12.3/`（由 `scripts/package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote "…"` 生成）。

| 文件 | 字节 | SHA256（前 16） |
| --- | --- | --- |
| `STS2AIAgent.dll` | 1187840 | `52D5D46B6D136C17` |
| `STS2AIAgent.pck` | 608 | `281945DC0424E807` |
| `STS2AIAgent.json` | 382 | `E0C7BD104DAFFEFC` |
| `README.md` | 3802 | `6A18A53359E678A4` |
| `LICENSE` | 34637 | `55515BACC3BD3796` |

DLL 比 v0.12.2 的 1186304 大 1536 字节，差额来自 #106 的代码。

## 本次改动（相对工坊上的 v0.12.2）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.3 段。只有一条：#85 的路线拆分此前只到了 `POST /action`，游戏内 F8 窗口的「邀请 AI 队友」按钮仍然调只走自动游玩的重载——模型未验证时点它会被旧门禁拒掉、tab 一直要求先做连接测试，而同一个请求走 API 却能拉起队友等待外部接管。对一个「本来就是给外部 agent 用」的功能来说，玩家真正会点的那一个按钮恰好是唯一走不通的入口。

现在按钮与 API 一样按 `FirstRunSetup.Evaluate(settings).ReadyToInvite` 选路，tab 里那两行说明也随路线切换，并且用源码契约测试 `CoopRoute.OverlayInviteRoute` 钉住（把旧写法还原回去，该测试立刻转红）。作者 sachi4clover，见 PR #106。

## 上传过程

1. 打包工作区：`content/` 字节和 1227269。
2. 游戏未运行，Steam 自 16:50 那次重启后一直运行。
3. 上传**一次成功**：`k_EItemUpdateStatusUploadingContent` 1188830 字节 → `UploadingPreviewFile` → `Successfully uploaded`。命令前置了 `HTTP_PROXY` / `HTTPS_PROXY`（本机 `127.0.0.1:10808`），这是 v0.10.7 记录里推荐的配方；本轮没有复现 v0.12.2 遇到的 `No Connection`。
4. 复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags` / `description`，`file_size` 与本地内容字节和完全相等。

## 未做

- **未打 tag、未建 GitHub Release**：本轮只按要求更新工坊。sachi4clover 还有一个叠在 #106 之上的后续 PR（游戏内「继续游玩」按钮与选角勾选框），把 #106 与那个 PR 攒在一起做一次 GitHub 发布是更自然的时点；需要时也可以随时单独回填 `v0.12.3` tag（发布提交 `b0217b0`）。
- **工坊简体中文列表仍未更新**（自 v0.11.0 起）。`ModUploader upload` 只有 `-w` / `-i`，没有语言参数；中文列表只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`。
- 未做订阅加载冒烟：沿用 2026-09-09 的结论（上传后重启 Steam 可见新内容）。
- 未再做实机验收：#106 的实机证据随身带来（未配置模型时真实点击拉起队友、`/health` 报 `companion.auto_play: false`），v0.12.3 的代码与该次验证逐字节相同，只有版本号变化。

