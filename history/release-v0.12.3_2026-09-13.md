# v0.12.3 发布与工坊上传记录（2026-09-13）

## 结论

**先发的 Steam 工坊，GitHub Release 随后跟上**，两边同代码。这一版是本仓库第一次把「工坊先行」的 v0.10.7 形式补成完整发布：版本号在 #108 就升好了，工坊 22:31 上传并复核，GitHub tag 与 Release 在 22:39 补上。

| 项 | 值 |
| --- | --- |
| 版本 | `0.12.3` |
| 发布分支 / PR | `codex/workshop-v0.12.3` → #108 |
| 发布提交（= tag 目标） | `b0217b0`（`Release v0.12.3 for the Steam Workshop`） |
| CI | #108 的 push（34762753954）与 pull_request（34762757419）两个 Validate run 均 success |
| Tag | `v0.12.3`（annotated → commit `b0217b0`），2026-09-13 推送 |
| Release | https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.3 ，资产 `sts2-ai-agent-v0.12.3-windows.zip`（549933 字节，SHA256 `B9DC1A070BE8A78ECFE1EC5ED37C6E606E4AA8CC2F8F383060EB3C953424B1B2`），GitHub 标记为 Latest |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-13 22:31:22`，内容 id `hcontent_file = 5284257893537057643` |
| 工坊 `file_size` | `1227269` —— 与本地内容字节和**完全相等**（dll 1187840 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 标签 | Tools & APIs / Utility / QoL（未变） |
| 默认（英文）说明 | 2672 字节，未被上传覆盖 |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`，最后一项用 `uv lock` 重新生成），`scripts/preflight-release.ps1` 全部步骤通过（含 `check_release_metadata.py` 与 `docs/api.md` 的 `mod_version`）；`scripts/package-release.ps1` 的产物检查对发布目录与 zip 均通过。

## 为什么升版本号

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

## 两条产物的 mod 载荷交叉核对

发布目录里的 DLL 与工坊上传的 DLL **大小相同（1187840）、PCK 字节完全一致**，但 DLL 的 SHA256 不同（发布 `40EDE117DD0D9E56` / 工坊 `52D5D46B6D136C17`）。逐字节比对：**整份文件只有 70 个字节不同，集中在 7 段连续区间**，位置正是 PE 头的时间戳（偏移 136）、CLR 模块 MVID 与调试目录（PDB 路径）。

也就是说这是同一份源码编译两次的正常差异（.NET 构建不带 deterministic 开关时每次都写入新的 MVID 与时间戳），不是代码不同。下一版若要两边哈希完全相等，需要给 `STS2AIAgent.csproj` 打开确定性构建（`<Deterministic>true</Deterministic>`，.NET SDK 默认已开，此处差异说明实际未生效或输出路径参与了哈希），本轮未改。

## 本次改动（相对工坊上的 v0.12.2）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.3 段。

1. **F8 窗口的邀请按钮与 API 同路**（PR #106，作者 sachi4clover）：#85 的路线拆分此前只到了 `POST /action`，游戏内「邀请 AI 队友」按钮仍然调只走自动游玩的重载——模型未验证时点它会被旧门禁拒掉、tab 一直要求先做连接测试，而同一个请求走 API 却能拉起队友等待外部接管。现在按钮按 `FirstRunSetup.Evaluate(settings).ReadyToInvite` 选路，tab 里那两行说明随路线切换，并用源码契约测试 `CoopRoute.OverlayInviteRoute` 钉住（把旧写法还原回去，该测试立刻转红）。
2. **打包链接 gate**（PR #105）：新增第八道离线 gate `packaged-links`，把「README 里新写的相对链接没人改写」挡在打包之前，而不是在切发布时才炸。

## 上传过程

1. 打包工作区：`content/` 字节和 1227269。
2. 游戏未运行，Steam 自 16:50 那次重启后一直运行。
3. 工坊上传**一次成功**：`k_EItemUpdateStatusUploadingContent` 1188830 字节 → `UploadingPreviewFile` → `Successfully uploaded`。命令前置了 `HTTP_PROXY` / `HTTPS_PROXY`（本机 `127.0.0.1:10808`），这是 v0.10.7 记录里推荐的配方；本轮没有复现 v0.12.2 遇到的 `No Connection`。
4. 工坊复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags` / `description`，`file_size` 与本地内容字节和完全相等。
5. GitHub 侧：`git tag -a v0.12.3 b0217b0` → 推送 → `package-release.ps1`（0 警告 0 错误，目录与 zip 产物检查均通过）→ `gh release create v0.12.3`。Release 资产 digest `sha256:b9dc1a07…` 与本地 zip 一致，GitHub 标记为 Latest。

## 未做

- **工坊简体中文列表仍未更新**（自 v0.11.0 起）。`ModUploader upload` 只有 `-w` / `-i`，没有语言参数；中文列表只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`。
- 未做订阅加载冒烟：沿用 2026-09-09 的结论（上传后重启 Steam 可见新内容）。
- 未单独再做完整实机验收：#106 的实机证据随身带来（未配置模型时真实点击拉起队友、`/health` 报 `companion.auto_play: false`），tag 的代码与那次验证逐字节相同，只有版本号变化。
- 未在 Steam 双开路径复跑外部接管路线（与 v0.12.2 相同的遗留缺口）。

## 注记

- 本轮与 v0.12.0 / v0.12.1 / v0.12.2 不同：先发工坊、后补 tag 与 Release，因此 tag 指向的是**发布提交本身** `b0217b0`（未被后续合并改写），而不是某个合并提交的等价物。
- 标签后目前只有文档与 journal（#109 的工坊上传记录、本轮记录），不构成新版本。
- sachi4clover 还有一个叠在 #106 之上的后续 PR（游戏内「继续游玩」按钮与选角勾选框）尚未合并，将是下一版的内容。

