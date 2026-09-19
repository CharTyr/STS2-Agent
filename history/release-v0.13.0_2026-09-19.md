# v0.13.0 发布记录（2026-09-19）

> 历史快照：本文件记录 v0.13.0 的 GitHub Release、Steam 工坊上传与最终发布二进制冒烟；
> 当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

发布提交：`78b085f`（PR #161 的 `dev → main` 合并）。tag `v0.13.0` 指向它；
候选提交 `3ee12e7` 与发布提交的源码树完全相同。

## GitHub Release

| 字段 | 值 |
| --- | --- |
| tag | `v0.13.0` → `78b085f0300b4abe189be234c17ed0a3b0518309` |
| 资产 | `sts2-ai-agent-v0.13.0-windows.zip` |
| 字节数 | 576033 |
| SHA256 | `8CFC0F451EE04D30C4F1708A5BF8553B5860FD1B61F99F249FEAAF87BAB18733` |
| Release | <https://github.com/CharTyr/STS2-Agent/releases/tag/v0.13.0> |

## Steam 工坊（物品 3796486050）

上传期间 SteamPipe 曾分别在上传 manifest、下载旧 manifest 时超时。确认本机代理
`http://127.0.0.1:10808` 可用、重启 Steam 并重试后，Steam 日志于 2026-09-19 11:32:26 记录
`Upload finished for workshop item 3796486050 : OK`。

Steam Web API 通过同一代理复核：

| 字段 | 值 | 与本地对照 |
| --- | --- | --- |
| `result` | 1 | 物品存在 |
| `visibility` | 0（公开） | 未因更新回落为私有 |
| `file_size` | 1252357 | 与 `content/` 字节和完全相等 |
| `time_updated` | 2026-09-19 11:32:25 +08:00 | 上传后即时刷新 |
| `hcontent_file` | `3840768014407368680` | 本次内容 manifest |
| `consumer_app_id` | 2868840 | 正确 |
| `tags` | Tools & APIs / Utility / QoL | 三个都在位 |

当日物品指标：订阅 107、累计订阅 239、浏览 782、收藏 15。

## 构建指纹

来源提交 `78b085f0300b4abe189be234c17ed0a3b0518309`，工作树干净。

工坊 content（5 个文件，合计 1252357 字节）：

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| `STS2AIAgent.dll` | 1212928 | `6B9D90C8C13998B2A5EEAA22D7B9D7543A466C5C6688214F73393A489A7FBE42` |
| `STS2AIAgent.pck` | 608 | `8BDDFCE8091E3112CF94F25E4B916E136718D64F79FF064CEB3A093540FDF7C0` |
| `STS2AIAgent.json` | 382 | `097919D60A3EF455897C4FA115A3694A294341A07BFE490F9CA1DB45A3DBE086` |
| `README.md` | 3802 | `6A18A53359E678A43F9A436D6F7C6F6B0AFE0C529C3AB40A27958D8E5DC3F0D6` |
| `LICENSE` | 34637 | `55515BACC3BD3796AF8B3C2864D2BBE1F7B922FB41B827C5C67138F18BB5F008` |

GitHub 产物目录指纹：22 个文件、1730338 字节，来源提交与干净状态相同。

## 验收证据

发布候选 `3ee12e7`（与 main 发布树相同）完成全量验收：C# **453 PASS / 0 FAIL**、
`mcp_server` **224 项 OK**、**十一道闸门全绿**、`preflight-release.ps1` exit 0。
隔离实机走通单人 embark、战斗、奖励、保存继续与死亡后的 `GAME_OVER`；联机邀请成功，
两名玩家均完成出牌与结束回合。

重新打包后 DLL 为 `6B9D…FBE42`，与候选验收部署的 `7A54…` 字节不同。源码树相同，差异来自
.NET 构建产物而非源码变化；为避免拿“树相同”替代“发布字节已跑过”，发布前又把 GitHub 与工坊
共同使用的 `6B9D…FBE42` DLL 部署到隔离实例做短冒烟：

- `/health` 报 `0.13.0`、`status: ready`，兼容性 27 项检查、0 缺失；
- 新建对局进入战斗，成功出牌（敌人 46 → 40）、结束回合，下一回合仍可操作；
- `/data/monsters` 返回 107 个条目，抽查 `AEONGLASS` 有 3 个招式；
- `save_and_quit` 后 `continue_run` 恢复同一 run id `QCYDW4JH5U20`，重新进入可操作战斗；
- 请求日志无 500、无 agent ERROR、无反射成员错误；玩家真实存档的逐文件哈希未变化。

## 已知边界

- 隔离主机使用 `clientId != 1` 时，`continue_ai_teammate` 会在读档前返回 409；拒绝时存档不变。
  该路径与 v0.12.5 去空白后完全相同，且 2026-09-13 已记录，因此不是 v0.13.0 回归。
- 这种必然拒绝的状态下，`continue_ai_teammate` 仍会出现在可用动作列表中，属于待修的动作面问题。
- 正常接回队友的成功路径需要以 `clientId 1` 启动，会触及真实存档目录，本轮没有执行。
- 工坊简体中文列表仍需在网页端手工更新；`ModUploader` 没有语言参数。
