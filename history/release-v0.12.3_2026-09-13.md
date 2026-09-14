# v0.12.3 发布与工坊上传记录（2026-09-13）

> 历史快照：本文件是已经发布出去的构建的证据记录，不代表当前状态；当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。
>
> 本文件记三次发布：下面的「结论」与各小节是 **2026-09-13 的第一次**（tag 当时指向 `b0217b0`）；
> 2026-09-14 有两个小节——「同号重发（第二次上传）」是 **第二次**（tag 重切到 `0f60ec4`），
> 「同号重发（第三次上传，第二次重切）」是 **第三次**，也是当前发布基准：tag 现指向 `c2630a8`，
> 两侧产物与工坊内容以第三次的数为准。

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
- sachi4clover 叠在 #106 之上的后续 PR（游戏内「继续游玩」按钮与选角勾选框）已作为 #111 合并，并随下面的同号重发发布。


## 2026-09-14 同号重发（第二次上传）

版本号仍是 `0.12.3`：这次只换构建，把 #111 的两个界面入口（「继续上次联机对局」与「禁用自动选角」）带给订阅者，不占版本号。

| 项 | 值 |
| --- | --- |
| 发布分支 / PR | `codex/republish-v0.12.3` → #113 |
| 发布提交（= 新 tag 目标） | `0f60ec4`（`Release v0.12.3 (re-cut): republish the same version with the co-op overlay entries`） |
| CI | #113 的 push（34767592154）与 pull_request（34767602272）两个 Validate run 均 success |
| Tag | `v0.12.3` 删除后重建：旧 tag 对象 `f74291a` → `b0217b0`；新 tag 对象 `ad15562` → `0f60ec4`（两者都是 annotated） |
| Release | 同一 URL 重建：资产 `sts2-ai-agent-v0.12.3-windows.zip`（552014 字节，SHA256 `B7684C9F2E8EAF7F43D9009EC5B6E0683033E9BC7088B52AEFDE5AE090DE5ADE`；GitHub 上报的 `sha256:b7684c9f…` 与本地一致），GitHub 标记为 Latest |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-14 00:09:26`，内容 id `hcontent_file = 6028841468497339213` |
| 工坊 `file_size` | `1229829` —— 与本地内容字节和**完全相等**（dll 1190400 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 / 标签 / 英文说明 | `0`（公开）/ Tools & APIs、Utility、QoL（未变）/ 2672 字节（未被上传覆盖） |

内容构成（第二次）：`STS2AIAgent.dll` 1190400、`STS2AIAgent.pck` 608、`STS2AIAgent.json` 382、`README.md` 3802、`LICENSE` 34637。DLL 比第一次的 1187840 大 2560 字节，差额来自 #111 的代码；PCK 与另外三个文件逐字节不变。

### 两条产物的载荷交叉核对（第二次）

工坊内容里的 `STS2AIAgent.dll` 与发布目录里的同名文件大小相同（都是 1190400），`STS2AIAgent.pck` 与 `mod_id.json` 逐字节一致；DLL 的 SHA256 不同（发布 `5E05F6CE59B1` / 工坊 `169A076AE899`，取前 12 位），逐字节比对**只有 72 个字节不同、集中在 5 段**（偏移 136 的 4 字节 PE 时间戳，以及 MVID / 调试目录所在区域）——与第一次相同的成因：同一份源码编译两次，.NET 每次写入新的时间戳与 MVID。

### 说明文档

- CHANGELOG 的 `## Unreleased` 段并入 v0.12.3 段，段首横幅写明「2026-09-14 同号重发」，并明确**两份构建的版本字符串相同、只能靠大小或哈希区分**。
- `steam-workshop/workshop.json` 的 changeNote 改为 `v0.12.3 (re-cut): the F8 window's AI Teammate tab gains Continue the saved co-op run and Disable automatic character pick. …`（保留邀请路线那一句）。
- 五处版本号**一处未动**：`check_release_metadata.py` 只比对它们彼此一致，不比对 tag、不比对上一版，所以同号重发在这套校验里是受支持形状。

### 上传过程

1. 把上一次的产物改名留档：`build/steam-workshop/sts2-ai-agent-v0.12.3` → `…-upload-2026-09-13`；`build/release/sts2-ai-agent-v0.12.3-windows` 与同名 zip → 同名加 `-upload-2026-09-13`。两个打包脚本都用 `Get-UniquePath`，不清旧目录会打出 `-2` 后缀。
2. `package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote <新 changeNote>`：内容字节和 1229829。
3. 游戏未运行、Steam 在运行；`Start-Process -WindowStyle Hidden` 分离启动 `ModUploader.exe upload -w <workspace> -i 3796486050`，进程环境带 `HTTP_PROXY` / `HTTPS_PROXY`（`127.0.0.1:10808`）。00:09:19 启动，一次成功（`Successfully uploaded 'STS2 AI Agent' to the workshop with id 3796486050`）。
4. 工坊复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags` / `description`，`file_size` 与本地内容字节和完全相等。
5. GitHub 侧：`gh release delete v0.12.3 --cleanup-tag --yes` 删掉旧 Release 与旧 tag → `git tag -a v0.12.3 0f60ec4` → 推送 → `package-release.ps1`（0 警告 0 错误，目录与 zip 产物检查均通过）→ `gh release create v0.12.3 --latest`。

### 未做

- **工坊简体中文列表仍未更新**（自 v0.11.0 起）。`ModUploader upload` 没有语言参数，只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`。
- **#111 的两个界面入口没有实机点击验收**：按钮时机、读档接回队友、子进程读到的勾选值，都待一次真实联机存档的实机；离线证据是 head `b42bc5a` 上的 384 PASS / 0 FAIL、八道 gate 全绿与三次破坏性验证。
- 外部接管路线仍未在 Steam 双开路径复跑（与第一次相同的遗留缺口）。

### 注记

- **这是本仓库第一次同号重发**。代价是「版本号 → 构建」不再一一对应：`0.12.3` 指两份构建，只能靠大小或哈希区分，所以 CHANGELOG 与状态页都显式写了这件事，而不是留给读者自己发现。
- 删除并重建 tag / Release 不会触发任何工作流：`.github/workflows/validate.yml` 只监听 `pull_request` 与 `main` / `dev` / `codex/**` 的 push，没有 tag 或 release 事件。


## 2026-09-14 同号重发（第三次上传，第二次重切）

版本号仍是 `0.12.3`：这次换构建，是把当天实机验收的产物与随之而来的收口工作带给订阅者——**一处玩家可见修复**（Continue 按钮待机不刷新）加上一批诊断、发布工具与离线契约测试。

| 项 | 值 |
| --- | --- |
| 发布分支 / PR | `codex/republish-v0.12.3-2` → #123 |
| 发布提交（= 新 tag 目标） | `c2630a8`（`Release v0.12.3 (second re-cut): carry the Continue fix and the diagnostics work`） |
| CI | #123 的 push（34774014272）与 pull_request（34774022312）两个 Validate run 均 success |
| Tag | `v0.12.3` 删除后重建：上一个 tag 对象 `ad15562` → `0f60ec4`；新 tag 对象 `4eb2806` → `c2630a8`（仍然是 annotated；远端 `refs/tags/v0.12.3` 复核为 `4eb2806` → `c2630a8`） |
| Release | 同一 URL 重建：资产 `sts2-ai-agent-v0.12.3-windows.zip`（555061 字节，SHA256 `E882B653B48B15EF278CFD9E20CC443FDCA3E5D69A51E362A40B934E29E167DF`；GitHub 上报的 `sha256:e882b653…` 与本地一致），GitHub 标记为 Latest |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-14 02:17:15`，内容 id `hcontent_file = 6689158800196895712` |
| 工坊 `file_size` | `1232901` —— 与本地内容字节和**完全相等**（dll 1193472 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 / 标签 / 英文说明 | `0`（公开）/ 未变 / 未被上传覆盖 |

内容构成（第三次）：`STS2AIAgent.dll` 1193472（比第二次的 1190400 大 3072 字节，差额来自 #116 与 #120 / #121 的代码）、`STS2AIAgent.pck` 608、`STS2AIAgent.json` 382、`README.md` 3802、`LICENSE` 34637；后四项与前两次逐字节相同。

### 载荷交叉核对（第三次）

这一轮的发布目录与工坊内容里的 `STS2AIAgent.dll` 与 `STS2AIAgent.pck` **SHA256 完全相等**（`49F45EA96A01…` / `281945DC0424…`），`mod_id.json` 与工坊的 `STS2AIAgent.json` 也都是 302 字节——两者是 `package-release.ps1` 与 `package-steam-workshop.ps1` 各自构建出来的，同一次会话里编译两次却得到同一份 DLL，这点与前两次（两次编译之间必然有时间戳 / MVID 差异）不同，记在这里免得被当成异常。

### 说明文档

- CHANGELOG 的 v0.12.3 段首改为 `## v0.12.3 - 2026-09-13 (republished twice on 2026-09-14)`，新增第二段横幅说明这次重切的内容与「三份构建都叫 0.12.3、只能靠大小或哈希区分」，并列出前两次的 `file_size` / 资产字节数 / SHA256 前缀。
- 同一段里补了两块：`### Fixed` 顶部加 Continue 按钮那条（含实机亮度数字），新增 `### Diagnostics, tooling and tests (second re-cut)` 记录 #117 / #120 / #121 / #122 与发布工具的两条。
- `steam-workshop/workshop.json` 的 changeNote 改为以 Continue 按钮修复打头，并保留上一版的两条界面入口说明。
- 五处版本号**一处未动**（与上一次重发同理）。

### 上传过程

1. 把上一次的产物改名留档：`build/release/sts2-ai-agent-v0.12.3-windows`（目录与 zip）与 `build/steam-workshop/sts2-ai-agent-v0.12.3` → 同名加 `-upload-2026-09-14a`。
2. `scripts/preflight-release.ps1` 先跑一遍：exit 0，C# 386 PASS / 0 FAIL，MCP 单测通过，八道 gate 与 gate 自测全绿，发布元数据一致。
3. `package-release.ps1 -Configuration Release`：0 警告 0 错误，目录与 zip 产物检查均通过。
4. `package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote <新 changeNote>`：内容字节和 1232901。
5. 游戏未运行；`Start-Process -WindowStyle Hidden` 分离启动 `ModUploader.exe upload -w <workspace> -i 3796486050`，进程环境带 `HTTP_PROXY` / `HTTPS_PROXY`（`127.0.0.1:10808`）。02:16:5x 启动，一次成功（`Successfully uploaded 'STS2 AI Agent' to the workshop with id 3796486050`）。
6. 工坊复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility`，`file_size` 与本地内容字节和完全相等。
7. GitHub 侧：`gh release delete v0.12.3 --cleanup-tag --yes` → `git tag -a v0.12.3 c2630a8` → 推送 → `gh release create v0.12.3 --title v0.12.3 --notes-file build/release/notes-v0.12.3-recut-2.md --latest <zip>`。发布说明是这一版新写的 `notes-v0.12.3-recut-2.md`（中文，含三份构建的区分表）。

### 未做

- **工坊简体中文列表仍未更新**（自 v0.11.0 起），仍然只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`。
- 外部接管路线仍未在 Steam 双开路径复跑（与前两次相同的遗留缺口）。
- 本版**没有单独跑一轮实机**：唯一的实机素材是 2026-09-14 对 #111 两个界面入口的点击验收（那次发现的 Continue 缺陷就是本版修掉的那一处），其余为离线证据。

### 注记

- 这是本仓库**第三次发布同一个版本号**（首发 + 两次重切）。同号重发的代价在第二次已经写明：`0.12.3` 现在指三份构建，CHANGELOG、状态页与 GitHub 发布说明都各自列了区分用的数字。
- 这次没有删掉旧产物，而是改名留档，三份构建在本地都能找到：`*-windows`（本次）、`*-windows-upload-2026-09-13`（首发）、`*-windows-upload-2026-09-14a`（第二次）。
- 与第二次相同：删除并重建 tag / Release 不触发任何工作流（`validate.yml` 只监听 `pull_request` 与 `main` / `dev` / `codex/**` 的 push）。


