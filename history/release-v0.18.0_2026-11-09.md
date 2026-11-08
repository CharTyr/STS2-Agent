# v0.18.0 发布记录（2026-11-09）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.18.0（五个版本文件一致） |
| 发布 PR | [#237](https://github.com/CharTyr/STS2-Agent/pull/237)（release/v0.18.0 → dev，版本号与 CHANGELOG）、[#238](https://github.com/CharTyr/STS2-Agent/pull/238)（dev → main） |
| tag / 源码提交 | `v0.18.0` → `8ee29276079fd3fb7fbcccc69a4fd17925d7e23d`（PR #238 合并提交） |
| GitHub Release | [v0.18.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.18.0)，资产 `sts2-ai-agent-v0.18.0-windows.zip`，961863 字节，SHA256 `B218E6AD46AEA319A9DD554AD7AADCB86A9EA23F2F2D1F5EE9B115F5F1589B54` |
| Steam 工坊 | **本次未上传**：上传需先在本机临时关闭系统代理并重启 Steam，属人工步骤，留待维护者执行（物品 3796486050） |
| DLL SHA256 | `1ECCC7B928E4BF84EE7F74C82C93D4F11CA311DCB5AF5B0A4880A83A8D96EF82` |
| 构建指纹 | GitHub 包：33 个文件、2815836 字节；`source_tree_dirty=true`——脏项是打包脚本自身的 `--log-file` 修复与未入库的 `.trellis/tasks/`，均不在发布内容内 |

## 内容

体验大改（bold-overhaul）：自动游玩接管游戏原生加速并在暂停/停止时恢复；无需取舍的小步骤零请求自动处理；整回合规划一次请求给出多步出牌、逐帧复核、Boss 召唤/变招即停；HUD 横幅/出牌描边/目标连线/理由气泡，F7 暂停、F8 面板；局末成绩单与最多 50 局的战绩摘要。MCP 两面载荷瘦身与同会话去重（#233）；静态运行前缀缓存分离、Boss 回合规划分歧检测、最小决策间隔（#234）；流式与非流式工具参数都被截断时按正文 JSON 出牌的服务商级降级。详见 [CHANGELOG.md](../CHANGELOG.md) 的 0.18.0 段。

## 验证与限制

- 离线：preflight（构建、Python 编译、MCP 导入、C# 测试、12 项静态门禁、预算代理自测、release 元数据核对）在打包机上通过；Release 构建 0 警告 0 错误。
- 打包机（DSH 沙箱）有 9 个测试因环境限制失败（临时文件写入、环回监听、本机 HTTP 等需要真实文件/套接字的用例），与 2026-10-27 记录的「其余环境失败」同类，已复核与本次变更无关。
- **实机边界（CHANGELOG 同述）**：#233、#234 在 2026-10-29 隔离实机之后合入，仅经离线测试；牌框描边/目标连线、F7 热键、联机队友与整局胜率未实机验证；「工具参数截断改正文出牌」仅在模拟截断响应上测过，未在出问题的网关上实测。按维护者决定随本版发布。
- 打包时发现 `scripts/build-mod.ps1` 未给 Godot 传 `--log-file`，沙箱内默认 `user://logs/` 不可写致 headless 崩溃；已在打包机上修复（提交见 dev 后续提交）。

## 仍需手工

- Steam 工坊上传与网页端简体中文列表粘贴（物品 3796486050）。
- 补做上表实机边界中列出的未验证项。
