# v0.16.0 发布记录（2026-09-24）

> 历史快照：本页记录这一次发布当时的过程与证据，不代表之后的状态。当前状态见
> [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，构建识别见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.16.0（五个版本文件一致，`check_release_metadata.py` 通过） |
| 发布 PR | #199（dev → main），CI `contracts` 通过 |
| tag / 源码提交 | `v0.16.0` → `4beaabeb836f50b001759c9b579b426d174360cc`（PR #199 的合并提交），打包时工作区干净 |
| GitHub Release | [v0.16.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.16.0)，资产 `sts2-ai-agent-v0.16.0-windows.zip` 777852 字节，SHA256 `43A026B56DD77D9B51969E8720D96CEEC0374CADFE5C25744B70E8EAD172E75F` |
| Steam 工坊 | 物品 3796486050，公开（`visibility` 0），`file_size` 1645031 = 本地内容字节和，`time_updated` 2026-09-24 02:32:06，内容 id `1458911249290867048` |
| DLL SHA256 | `7935096C0E95D8B0195EA45BB3720FFE867C4AAB0CDB47B72AF3CFC8EF1F70FB`（GitHub 包与工坊包相同） |

## 内容

见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.16.0 段：思考内容实时显示、双层模式的宏观目标与提示对齐、
外部规划器可设 `goal` 且局部更新不再重置策略、覆盖层右边缘被裁的根因修复、截图不再含覆盖层，
以及随本次合入 main 的 2026-09-23 严重可用性修复。

## 验证

- 离线：C# 860、Python 423、全部验证闸门、`preflight-release.ps1` 通过；新增的源码契约另在 CRLF 检出上复跑，同为 860 通过。
- 三份相互独立的只读审查（流式思考 / 双层策略 / 截图与布局），确认属实的问题全部先写失败测试再修复。
- 实机（隔离档，全程不调用模型）：截图不含覆盖层；两个页签的 `[STS2AIAgent.Wide]` 均为 0，包括删除首个模型之后；正式 Steam 档 185 个文件前后一致。详见 [2026-09-23 验证报告](../docs/live-validation-2026-09-23-results.md)。
- **未实机复验**：思考流式显示、双层模式实际打法质量（需要真实模型 / Jev 调用）、AI 队友。

## 工坊上传过程

按 [steam-workshop/README.md](../steam-workshop/README.md) 的已知做法：上传前把系统代理原值写到文件，
临时关闭 `ProxyEnable`，重启 Steam 等到登录，再用 `ModUploader.exe upload -w <workspace> -i 3796486050`。

1. 02:30:54 关闭系统代理；02:31:58 Steam 重启并登录。
2. 02:31:58 第一次上传启动，02:32:06 `workshop_log.txt` 记 `Upload finished for workshop item 3796486050 : OK`（成功行数由 26 增至 27）——约 8 秒，与 v0.14.0 记录一致。
3. 02:32:08 系统代理按原值恢复（`ProxyEnable=1`、`127.0.0.1:10808`），随后重启 Steam 回到用户原先的代理环境。
4. Steam Web API（经代理）复核：`file_size` 1645031 与本地 `content/` 字节和相等，`visibility` 0。

**恢复代理后 Steam 重连慢**：重启后 4 分钟内连接日志没有 `Logged On`，CM 的 WebSocket 连接在代理开启时反复失败
（`StartAutoReconnect() ... attempt 6`），而经代理的连通性测试正常。这与 v0.14.0 记录的现象相同，
是本机代理环境下 Steam 自己的重连行为，与上传无关。对照连接日志：上传前那次（00:24:33）登录走的是 **UDP**
（Steam 按约 11% 的概率选 UDP），这次重启后一直选 WebSocket 并失败。处理方式与 v0.14.0 相同：02:42 再次在代理
关闭时重启 Steam，02:43:24 `Logged On` 后**立即**把系统代理恢复原值且不再重启 Steam；此后 Steam 保持在线，
系统代理 `ProxyEnable=1`、`ProxyServer`、`ProxyOverride` 与上传前逐字段一致。

**下次上传的建议**：上传后恢复代理即可，**不要**在代理开启状态下再重启 Steam——那一步在本机大概率让 Steam
卡在 WebSocket 重连里。

## 未完成 / 需手工

- 工坊**简体中文列表**仍需在网页端手工粘贴 `steam-workshop/description.zh-CN.txt`（`ModUploader` 没有语言参数）。
- 订阅加载验收（从工坊目录实际加载 0.16.0）未做：需要用户用「带 Mod 启动」重启游戏确认。
