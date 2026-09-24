# v0.16.1 发布记录（2026-09-24）

> 历史快照：本页记录这一次发布当时的过程与证据，不代表之后的状态。当前状态见
> [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，构建识别见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.16.1（五个版本文件一致，`check_release_metadata.py` 通过） |
| 发布 PR | #200（dev → main），CI `contracts` 通过 |
| tag / 源码提交 | `v0.16.1` → `a64e052710fa08a74b54418a78af6bd17d54b2fc`（`dev` 上的 Release 提交），打包时工作区干净 |
| GitHub Release | [v0.16.1](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.16.1)，资产 `sts2-ai-agent-v0.16.1-windows.zip` 779153 字节，SHA256 `835E22E871DD4A217766F5E02043312D2B144E9D81CEB00B75B32B49D37C7551` |
| Steam 工坊 | 物品 3796486050，公开（`visibility` 0），`file_size` 1646567 = 本地内容字节和，`time_updated` 2026-09-24 11:54:48，内容 id `1146374597091505946` |
| DLL SHA256 | `CD9707AA463005977D885D1D5D6E206BA28B1CF82A69F67132F34019472E1553`（工坊包） |

## 内容

一处集中修复：单人模式下或自动游玩进行中，`invite_ai_teammate` / `continue_ai_teammate` 不再无声地一直挂在
`pending`。这类玩家可修复的前置拒绝现在记录为 `Rejected`，并带可读原因（切换到多人模式 / 先暂停自动游玩），
调用返回 409 指向原因；真正并发在途的尝试仍按 `pending` 不变。详见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.16.1 段。

## 验证

- 实机复现原故障（两次邀请 8 分钟无任何反馈：`dual_launch_outcome` 一直为 Idle、`dual_status` 不变化、无双开进程），
  修复后同一隔离档复验：邀请返回 **409 `invite_failed`**，错误信息指向双人模式切换；`/health` 显示
  `dual_launch_outcome=Rejected`、原因出现在 `dual_status`。
- 离线：C# 866、Python 423、全部验证闸门、`preflight-release.ps1`、CI 通过。
- 正式 Steam 档 185 个文件前后哈希一致。
- **未实机**：Steam 开启环境（好友/大厅联机）的完整队友拉起与退出——Steam-off 环境禁止多人大厅，百度下视为环境受限场景；
  本版只验证到"前置拒绝有明确原因"这一段。

## 工坊上传过程

按 [steam-workshop/README.md](../steam-workshop/README.md) 与上次的修正：

1. 11:53:44 代理原值已存（`build/live-2026-09-24/proxy-saved.json`），临时关闭 `ProxyEnable`，重启 Steam，11:54:38 登录。
2. 基线成功行 27；11:54:38 第一次上传，11:54:49 `workshop_log.txt` 记 `Upload finished for workshop item 3796486050 : OK`（→28）。
3. 11:54:58 系统代理恢复（`ProxyEnable=1`、`127.0.0.1:10808`），**没有再重启 Steam**——按 v0.16.0 的教训，代理开启时 Steam
   容易卡在 WebSocket 重连里。Steam 保持代理关闭时建立的连接继续在线。
4. Steam Web API（经代理）复核：`file_size` 1646567 与本地 `content/` 字节和相等，`visibility` 0。

## 未完成 / 需手工

- 工坊**简体中文列表**仍需在网页端手工粘贴 `steam-workshop/description.zh-CN.txt`（`ModUploader` 没有语言参数）。
- 订阅加载验收（从工坊目录实际加载 0.16.1）未做。
