# v0.12.2 发布与工坊上传记录（2026-09-13）

## 结论

GitHub Release 与 Steam 工坊内容**都已完成**；工坊的**简体中文列表仍未更新**（见「未做」），与 v0.12.1 同。

| 项 | 值 |
| --- | --- |
| 版本 | `0.12.2` |
| 发布分支 / PR | `codex/release-v0.12.2` → #104 |
| 发布提交 | `40d1464`（`Release v0.12.2`） |
| 合并提交（= tag 目标，也是发布时的 `main`） | `72b2a81` |
| CI | `40d1464` 的 push（34748166159）与 pull_request（34748168653）两个 Validate run 均 success |
| Tag | `v0.12.2`（annotated → commit `72b2a81`） |
| Release | https://github.com/CharTyr/STS2-Agent/releases/tag/v0.12.2 ，资产 `sts2-ai-agent-v0.12.2-windows.zip`（549033 字节，SHA256 `35C1F0FC0ED3C664C0F74A73B5759486E4CA2BE92295CC47062FCC65552B5D9D`） |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-13 16:51:04`，内容 id `hcontent_file = 8439947284938535648` |
| 工坊 `file_size` | `1225733` —— 与本地内容字节和**完全相等**（dll 1186304 + pck 608 + json 382 + README 3802 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 标签 | Tools & APIs / Utility / QoL（未变） |
| 默认（英文）说明 | 2672 字节，未被上传覆盖 |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`，最后一项用 `uv lock` 重新生成），`scripts/preflight-release.ps1` 全部步骤通过（含 `check_release_metadata.py` 与 `docs/api.md` 的 `mod_version`）。

## 内容构成

工作区：`build/steam-workshop/sts2-ai-agent-v0.12.2/`（由 `scripts/package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote "…"` 生成）。

| 文件 | 字节 | SHA256（前 16） |
| --- | --- | --- |
| `STS2AIAgent.dll` | 1186304 | `1046081212022442` |
| `STS2AIAgent.pck` | 608 | `5E1C66CF4A139CB2` |
| `STS2AIAgent.json` | 382 | `04C676AFC3AB4385` |
| `README.md` | 3802 | `6A18A53359E678A4` |
| `LICENSE` | 34637 | `55515BACC3BD3796` |

## 本次改动（相对工坊上的 v0.12.1）

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.12.2 段。这一版是「联机接力」：

1. **邀请 AI 队友不再要求已验证的游玩模型**（#85）——模型没验证时队友照常拉起、照常进图，然后停在原地等外部接管，且这条路线根本不调用模型（`STS2_AGENT_AUTOPLAY=0`）。
2. **主窗口新增 `POST /teammate/control`**（#85），外部 agent 有了一条受支持的开始 / 暂停入口，不需要队友会话令牌；`GET /health` 的 `companion` 区块让调用方自己找到队友的 API 端口。
3. **队友实例自己的 `POST /session/control` 补齐同一道模型门禁**（#99）——此前它是唯一一个能绕过验证启动进程内循环的入口；暂停刻意不受门禁。
4. **`combat.enemies[].base_max_hp`**（#101）把联机缩放前的基础血量与缩放后的实况血量分开，元数据终于和实况对得上。
5. **`get_relevant_game_data` 的 `item_ids` 可省略**（#101），按当前屏幕从实况派生；分页教学弹窗在非末页给出自解释文案而不是看起来卡住的 `pending`（#101）。

## 打包时发现并修掉的缺陷

第一次打包时 `package-release.ps1` 被产物检查拦下：`README.md links to missing packaged file 'docs/api.md'`。

- **根因**：打包时 `Rewrite-PackagedReadmeLinks` 用一张固定的字符串替换表把 README 里的相对链接改写成 GitHub 绝对链接，表里的键是 `"(./docs/api.md)"` 这种形态；#97 在 README 里新加的是**不带 `./`** 的 `(docs/api.md)`，没命中替换表，于是产物里留下了一个指向未打包文件的链接。
- **修复**：两处 README 的那条链接改回带 `./` 的形态（与文件里其它链接一致），不再往替换表里加一条会造成语义重复的规则。
- **防复发**：新增第八道离线 gate `packaged-links`。它从 `package-release.ps1` 解析出改写表、从 `check_release_package.py` **import** 那两个清单与链接规则（不复制粘贴，避免清单漂移后 gate 说谎），在源文档上重放与打包相同的改写，再检查剩下的本地链接是否都在产物里。破坏性验证：把 README 的链接改回裸形态 → gate 以退出码 1 报出 `docs/api.md`；逐字节还原后恢复通过（SHA256 与还原前一致）。自测脚本 `test-verification-gates.ps1` 做了最小更新（fixture 补上新 gate 需要的输入、6b 用例先把打包脚本挪开）。

## 上传过程（含一次故障）

1. 打包工作区：`content/` 字节和 1225733。
2. 游戏未运行，Steam 在运行。
3. **第一次上传连续失败 6 次**，`k_EItemUpdateStatusInvalid` / `k_EResultFail`。Steam 自己的日志给出原因：`Upload workshop item 3796486050 failed (Failed to initialize build on server (No Connection) )` —— Steam 客户端到 UGC 后端的会话失效，与本地产物无关（12:41 上传 v0.12.1 时同一流程正常，`steamcommunity.com` 直连也返回 200）。
4. **处理**：`steam.exe -shutdown` 等进程完全退出后重新启动（`AutoLoginUser = chartyr`、`RememberPassword = 1`，自动登录回 `U:1:460312869`），再跑同一条上传命令即成功：`k_EItemUpdateStatusUploadingContent` 1187294 字节 → `UploadingPreviewFile` → `Successfully uploaded`。
   → 结论：**工坊上传卡在 `No Connection` 时，重启 Steam 客户端是有效的处置**，不必怀疑产物。
5. 复核：Steam Web API `GetPublishedFileDetails` 核对 `file_size` / `time_updated` / `visibility` / `tags` / `description`，`file_size` 与本地内容字节和完全相等。

## 未做

- **工坊简体中文列表仍未更新**（自 v0.11.0 起未做，本轮再次确认）。`ModUploader upload` 只有 `-w` / `-i` 两个选项，没有语言参数；工坊的英文说明是 VDF 里的单个 `description` 字段，中文列表只能在工坊网页端编辑。仓库内的 `steam-workshop/description.zh-CN.txt` 是当前应粘贴的正文。
- 未做订阅加载冒烟：沿用 2026-09-09 的结论（上传后重启 Steam 可见新内容）。
- **未再做一遍完整实机验收**：v0.12.2 里的代码与实机跑过的那份**逐字节相同**（实机证据来自 #85 / #99 / #101 三次隔离验收，见 [docs/live-validation-checklist.md](../docs/live-validation-checklist.md)），标签之后只有文档与打包脚本的改动。仍需实机确认的项以该清单为准；其中两条明确的缺口是「外部接管路线未在 Steam 双开路径复跑」与「`FAKE_MERCHANT` 的回落只有离线单测」。

## 注记

- 发布提交经 PR 合并，因此 tag 指向**合并提交** `72b2a81` 而不是发布提交 `40d1464`；两者树内容相同。
- 打包用的源码在 tag 之上多了 README 链接修复与新增 gate（`scripts/` 与两个 README），**不涉及 mod 代码**，因此发布的 DLL/PCK 与 tag 上的 mod 源码等价。
- 本轮没有新增标签或改可见性，工坊物品与 v0.12.1 相比只有内容、changenote 与预览图上传路径变化。
