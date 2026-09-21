# v0.14.0 发布记录（2026-09-21）

> 历史快照：本文件记录 v0.14.0 的 GitHub Release、Steam 工坊上传与构建指纹。
> 当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

发布提交：`5f9ea72`（PR #174 的 `dev → main` 合并）。Tag `v0.14.0` 指向它。
功能实现经 PR #173 合入 `dev`（35 个提交，CI `contracts` 通过）。

## GitHub Release

| 字段 | 值 |
| --- | --- |
| tag | `v0.14.0` → `5f9ea72691eaad3299cd618884061a98780c6798` |
| 资产 | `sts2-ai-agent-v0.14.0-windows.zip` |
| 字节数 | 637550 |
| SHA256 | `37F95EED73C636ECEDB160683B28D06B7DFAA3DFD14432A500F935DC1E6E2A70` |
| Release | <https://github.com/CharTyr/STS2-Agent/releases/tag/v0.14.0> |

## 内容

v0.14 的主题是**会解释的决策 + 更深的双人协作**：

- **决策解释链**：动作可带理由，`AgentTurnResult.Reasoning` 对非思考型模型不再为空。理由进入
  overlay 的「决策日志」页、`GET /decisions`、两面 MCP 的 `get_decision_log`，以及 SSE 新事件
  `decision_made`。决策条目带 `run_id`，可按局汇总花费；模型未回报用量时显示「未知」而不是 0。
- **策略层**：新增 route / 休息点 / 商店 / 药水时机 / 战斗优先级 / 双人分工规则，按屏注入
  （`skills/sts2-mcp-player/references/strategy.md` + `PlaybookSections`）。新 MCP 工具
  `get_scene_guidance` 一次调用返回当前屏策略，事件屏附带逐选项风险分级。
- **知识库**：卡表补 Owner 与可读 Effect；事件表补逐选项 handler / cost / risk（77 → 357 行）。
- **协作**：`POST /companion/message` 支持可选结构化 `intent`（`focus_fire` / `target_announce` /
  `potion_ownership`），格式错误返回 400 而不是静默丢弃；host 面板显示队友角色的实时血量 /
  格挡 / 能量 / 手牌数。
- **界面重做**：新增设计系统（间距与字号刻度、分区卡片、按钮主次层级、当前页签高亮药丸）与
  四套配色主题（暮色 / 午夜 / 羊皮纸 / 高对比），设置里可选、保存即生效；每套主题受离线对比度
  下限约束。
- **机读 API**：`docs/openapi.json`（OpenAPI 3.1，12 路径 / 76 模式）由 `scripts/api_schema.py`
  生成，第 12 道发布闸门逐字节比对。
- **新 MCP 工具**：`get_run_summary`、`diff_state`（两面同加）。
- **模型兼容**：`max_tokens` / `max_completion_tokens` 按服务端错误重试一次；上下文超长的 400
  不误判。
- **事件流**（原计划单独发版的 v0.13.1，已并入本版）：SSE 轮询按需驱动、慢订阅者显式断开、
  关闭不复活事件状态，新增 `inject_event_churn` 调试动作。
- **验证工具**：`run_sts2_validation.py patch-check` 一条命令跑完补丁回归；
  `scripts/test-isolated-settings.ps1` 钉住隔离档 seeding。
- **代码健康**：`GameStateService.cs` 5,037 → 1,336 行（按屏拆 8 个 partial，218 条声明级
  SHA-256 证明逐字节搬迁）。

## Steam 工坊（物品 3796486050）

上传流程见 [steam-workshop/README.md](../steam-workshop/README.md)。本机无 SteamCMD 与
ModUploader，改用官方 [megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader)
v0.2.0 的 `ModUploader-win-x64.zip`，下载后按 Release 公布的
`sha256:2b55c19c…6578` 逐字节校验通过再执行。

上传状态与证据写在下方「工坊上传过程」一节；成功前不得把本页当作工坊已更新。

## 构建指纹

来源提交 `5f9ea72691eaad3299cd618884061a98780c6798`（tag `v0.14.0`）。

> **`source_tree_dirty: true` 的确切含义**：仓库根有两个 **0 字节**的未跟踪文件
> `cstest.log` 与 `v.log`（创建于 2026-09-20 11:45，仓库内无任何脚本引用，属于更早一次误重定向
> 的残留）。它们不参与编译或打包，`git diff` 为空——**没有任何已跟踪文件被修改**。
> 构建脚本用 `git status --porcelain` 判定「脏」，未跟踪文件也算，所以指纹如实记录了 true。
> 这两个文件不属于本次发布，按要求未做处置。

GitHub Release（27 个文件，合计 1901019 字节）中的 mod 载荷：

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| `mod/STS2AIAgent.dll` | 1320448 | `F64F8C2AEBFFAC319FF3ADF2976C7C2323D6896E0B73EEB8980654DCECFFFDA3` |
| `mod/STS2AIAgent.pck` | 608 | `AA0F4EBB22A6AF3B53AACBEDE3969BEDF7AA21688613038CE915E774C4407BC4` |
| `mod/mod_id.json` | 382 | `9E553D79492D45E5E3D27C2DB291E8841FC8F8D69D40C8C0B7C9DF61BE35C2C6` |

工坊 content（5 个文件，合计 1360359 字节，`file_size` 应等于这个和）：

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| `STS2AIAgent.dll` | 1320448 | `F64F8C2AEBFFAC319FF3ADF2976C7C2323D6896E0B73EEB8980654DCECFFFDA3` |
| `STS2AIAgent.pck` | 608 | `AA0F4EBB22A6AF3B53AACBEDE3969BEDF7AA21688613038CE915E774C4407BC4` |
| `STS2AIAgent.json` | 382 | `9E553D79492D45E5E3D27C2DB291E8841FC8F8D69D40C8C0B7C9DF61BE35C2C6` |
| `README.md` | 4284 | `64FA56B2B9EFA0DD084B9FCDEE5A841FB668447E3FCD314D24A852603CB20C6F` |
| `LICENSE` | 34637 | `55515BACC3BD3796AF8B3C2864D2BBE1F7B922FB41B827C5C67138F18BB5F008` |

两处 DLL 哈希相同，说明 Release 与工坊出自同一次构建。

## 工坊上传过程（含根因）

2026-09-21 的上传一开始反复失败，最后定位到的根因**不是上传器，也不是 Steam 离线**，而是
**本机系统代理挡在 Steam 的内容分发（SteamPipe）路径上**。过程：

1. 首次尝试停在 `k_EItemUpdateStatusPreparingConfig` 不动——`steam-workshop/README.md` 记录过这个
   症状（当时把「重启 Steam 客户端」当作处置）。
2. 重启 Steam 后重试，症状前移到 `k_EItemUpdateStatusPreparingContent`，几分钟后无进展；再下一次
   直接返回 `k_EItemUpdateStatusInvalid` / `k_EResultFail`。
3. 查 `connection_log.txt` 发现当时 Steam 客户端**处于离线**（所有 `*.steamserver.net` 连接管理器
   连接超时、`StartAutoReconnect() attempt 5/6`），只有经代理 `127.0.0.1:10808` 的普通 HTTP
   连通性测试是 OK 的。上传需要一条活的 Steam 连接，这一步必然失败。等 Steam 于 09:56:30
   重新 `Logged On` 后，`workshop_log.txt` 能记到 `Upload starting ...`，但 **manifest 阶段仍然停滞**。
4. 连续 6 次尝试（每次 6 分钟）全部停在 `PreparingContent`，从未走到 `UploadingContent`。
5. **换方法后根因确认**：把系统代理临时关掉（`ProxyEnable=0`），重启 Steam —— Steam 照样
   `Logged On`（说明它的连接管理器本来就直连，不依赖代理），然后用同一个上传器再跑一次：
   **8 秒完成**，`workshop_log.txt` 记 `[2026-09-21 10:44:21] Upload finished for workshop item
   3796486050 : OK`。随后系统代理已按原值恢复（`ProxyEnable=1`、`127.0.0.1:10808`）。

**结论：代理是本机上传工坊的已知障碍。** 它不影响 Steam 登录、不影响 Steam Web API 查询，
只让 SteamPipe 的内容传输挂死。下次上传前先关代理、重启 Steam，比反复重试有效得多——
这条已写进 [steam-workshop/README.md](../steam-workshop/README.md)，替代原先「重启就好了」的说法。

## 复核（Steam Web API，经代理查询）

| 字段 | 值 | 与本地对照 |
| --- | --- | --- |
| `result` | 1 | 物品存在 |
| `visibility` | 0（公开） | 未因更新回落到私密 |
| `file_size` | 1360359 | **与 `content/` 字节和完全相等** |
| `time_updated` | 2026-09-21 10:44:20 | 上传后即时刷新 |
| `hcontent_file` | `5672199774606704499` | 本次内容 manifest |
| `title` | STS2 AI Agent | 未变 |
| `description` | 2859 字符，含 v0.14 的新条目（`See what it played and why`、`teammate's live status`） | 英文 listing 已随本次更新 |

## 结论

- **GitHub**：`v0.14.0` tag、Release 与资产均已发布并复核（见上）。
- **Steam 工坊**：物品 3796486050 已更新到 v0.14.0，`file_size` 与本地 content 字节和相等，
  可见性保持公开。
- **仍未做（需要人工在 Steam 网页端操作）**：把
  [steam-workshop/description.zh-CN.txt](../steam-workshop/description.zh-CN.txt) 粘贴进物品的
  简体中文 listing。ModUploader 只写入默认（英文）描述，Steam 没有为「其它语言描述」提供 API，
  所以这一步无法脚本化。
