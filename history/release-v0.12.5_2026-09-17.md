# v0.12.5 发布记录（2026-09-17）

> 历史快照：本文件记录 v0.12.5 的 GitHub Release 与 Steam 工坊上传；当前状态见
> [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

发布提交：`f361bdb`（PR #145 的 `dev → main` 合并）。tag `v0.12.5` 指向它。
**一次发布，未重切**——前四次同号重发（v0.12.3 ×2、v0.12.4 ×2）的成因是 tag 之后来的修复
无处可记；本轮这两条修复也都是 tag 之后发现的，但等在 `CHANGELOG.md` 的 `## Unreleased` 段里，
直到这次发版才定版。

## 本版内容

两条玩家 / agent 能感知的运行时修复，**都是驱动真实游戏发现的，不是读代码读出来的**，
且都在打过补丁的构建上做了实机复验。

1. **死亡后 `/state.screen` 恢复报 `GAME_OVER`**（PR #142）。死亡后战斗房仍然 active，
   `ResolveNonModalScreen` 的战斗分支抢在 `NGameOverScreen` 之前命中，整个结算阶段都报 `COMBAT`。
   实机基线里 8 个「唯一可用动作是 `continue_game_over`」的样本**全部**报 `COMBAT`，
   2346 个样本里 `GAME_OVER` 一次没出现过。
2. **失败的游戏动作说出游戏给的原因**（PR #143）。12 条路径共用一句无信息量的
   "the game task faulted"，分不清「游戏正当拒绝」和「mod 坏了」。

其余是契约与工具（PR #141）：`docs/api.md` 首次完整描述 `/state`、`api-facts` 闸门扩容、
打包产出构建指纹、体量棘轮。

## GitHub Release

| 字段 | 值 |
| --- | --- |
| tag | `v0.12.5` → `f361bdb`（PR #145 的 dev→main 合并） |
| 资产 | `sts2-ai-agent-v0.12.5-windows.zip` |
| 字节数 | 562212 |
| SHA256 | `0E4A15184A679D95C5064247B2DB0FEFA25E3B68D743D9110A4F59DE0318CFED` |
| 发布说明 | `build/release/notes-v0.12.5.md`（gitignore） |

## Steam 工坊（物品 3796486050）

上传一次通过，未卡住——这是三次里的第一次（前两次分别卡在 `Upload starting` 与
`PreparingContent`，都靠重启 Steam 客户端解决）。上传命令：
`ModUploader.exe upload -w build/steam-workshop/sts2-ai-agent-v0.12.5 -i 3796486050`，
进程环境带 `HTTP_PROXY` / `HTTPS_PROXY=http://127.0.0.1:10808`（上传前 `curl -x` 自检返回 200）。

Steam Web API 复核（`GetPublishedFileDetails`）：

| 字段 | 值 | 与本地对照 |
| --- | --- | --- |
| `result` | 1 | 物品存在 |
| `visibility` | 0（公开） | 未因更新回落为私有 |
| `file_size` | 1238533 | 与 `content/` 字节和**完全相等** |
| `time_updated` | 2026-09-17 09:36:37 | 上传后即时刷新 |
| `hcontent_file` | `2962740913650521121` | 本次内容 id |
| `consumer_app_id` | 2868840 | 正确 |
| `tags` | Tools & APIs / Utility / QoL | 三个都在位 |

当日物品指标（同一次 API 调用）：订阅 90、累计订阅 194、浏览 657、收藏 14。

## 构建指纹（首次由打包脚本自动产出）

以往这些数字是上传之后手工 `Get-FileHash` 抓的；自 PR #141 起，
`scripts/lib-build-fingerprint.ps1` 在打包那一刻就写进产物旁的 `build-fingerprint.json`，
`file_size` 的对账也直接拿它比。`source_tree_dirty: false` 顺带证明产物确实来自该提交，
而不是从脏工作树构建的。

来源提交 `f361bdb6b2031a434b4f26d04ed764ec6332b27b`，工作树干净。

工坊 content（5 个文件，合计 1238533 字节）：

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| `STS2AIAgent.dll` | 1199104 | `1624BBF5CBAB50D415FAFCC59C354A6101616899F2FC00E9C8A76CAFF84CD4A7` |
| `STS2AIAgent.pck` | 608 | `CD6403306ACA1A3AF5FDC918C0FDD9F9EF78339FCAEFE5BE4032CECC8440BC76` |
| `STS2AIAgent.json` | 382 | `CF8F434BA424EFC5A8B18C31F9EE2C097F5B1A98667FF1A45FA91584181E343C` |
| `README.md` | 3802 | `6A18A53359E678A43F9A436D6F7C6F6B0AFE0C529C3AB40A27958D8E5DC3F0D6` |
| `LICENSE` | 34637 | `55515BACC3BD3796AF8B3C2864D2BBE1F7B922FB41B827C5C67138F18BB5F008` |

GitHub 产物目录指纹另存于 `build/release/sts2-ai-agent-v0.12.5-windows-fingerprint.json`
（20 个文件、1695285 字节，gitignore）。

## 验收证据

实机（隔离离线主机，`--windowed --force-steam off --clientId 2026091701`，API 18080，单实例）：

- **动作面基线**：2346 对 `/state` 与 `/actions/available` 背靠背采样、12 块屏、22 种动作集变体。
  非战斗屏 61 个样本零分歧；战斗屏 23 处分歧全部是时序假象（只涉及受门禁管的
  `end_turn` / `play_card` / `use_potion`，方向跟随「哪个端点后读」而非「哪个表面」）。
- **`GAME_OVER` 修复复验**：两次独立死亡，10 个 `game_over` 非空样本**全部**报 `GAME_OVER`，
  零个 `COMBAT`；`continue_game_over` → `return_to_main_menu` 全链路仍然走通，
  `save_verified` 到达 `true`。12 块基线屏重采、25 条逐屏对照，**没有任何一块屏换名字**。
- **诊断修复复验**：重复 `room Treasure` 现在返回
  `Console command failed: the game task faulted: InvalidOperationException: Attempted to start
  new relic picking session while one was already occurring.`——异常无害且自解释。
  成功路径不受影响（成功的控制台命令仍返回 `completed`）。

玩家真实 Steam 存档在两轮实机前后各哈希一次：184 个文件、摘要一致、逐文件 diff 为空。

离线（发布提交上）：C# **415 PASS / 0 FAIL**、`mcp_server` **224 项 OK**、
`check_verification_gates.py` **九道闸门全绿**、闸门自测 **29 条**、
`check_release_metadata.py` 五处版本号一致、`preflight-release.ps1` exit 0、mod 构建 0 警告。

## 未做

- **工坊简体中文列表仍是旧版**（自 v0.11.0 起缺多条列表项）。`ModUploader` 没有语言参数，
  只能在工坊网页端手工粘贴 `steam-workshop/description.zh-CN.txt`（仓库里的文案是新的）。
- 未做订阅端加载冒烟（沿用 2026-09-09 的结论：上传后重启 Steam 可见新内容）。
