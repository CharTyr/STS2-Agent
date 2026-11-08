# v0.18.0 发布记录（2026-11-09）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.18.0（五个版本文件一致） |
| 发布 PR | [#237](https://github.com/CharTyr/STS2-Agent/pull/237)（release/v0.18.0 → dev，版本号与 CHANGELOG）、[#238](https://github.com/CharTyr/STS2-Agent/pull/238)（dev → main） |
| tag / 源码提交 | `v0.18.0` → `8ee29276079fd3fb7fbcccc69a4fd17925d7e23d`（PR #238 合并提交） |
| GitHub Release | [v0.18.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.18.0)，资产 `sts2-ai-agent-v0.18.0-windows.zip`，961863 字节，SHA256 `B218E6AD46AEA319A9DD554AD7AADCB86A9EA23F2F2D1F5EE9B115F5F1589B54` |
| Steam 工坊 | 物品 `3796486050`；`workshop_log.txt` 于 2026-11-09 02:07:05 记 `Upload finished for workshop item 3796486050 : OK`，并先于 02:06:05/02:07:04 两次记 `No content change detected for item 3796486050`——内容字节（2,193,555，5 个文件）与本地 `content/` 一致；预览图 `image.png` 一并上传。上传期间按本机惯例将系统代理 `ProxyEnable` 临时置 0，完成后已恢复为 1。 |
| DLL SHA256 | `1ECCC7B928E4BF84EE7F74C82C93D4F11CA311DCB5AF5B0A4880A83A8D96EF82`（GitHub 包；工坊包 `8BF2872D0E9F4D6A14E15D17E3C91D541B5DD9979E75086530564A384842F509`，为合并提交后重打包，版本号同为 0.18.0） |
| 构建指纹 | GitHub 包：33 个文件、2815836 字节；工坊 content：5 个文件、2193555 字节；两者 `source_tree_dirty=true`——脏项是打包脚本自身的 `--log-file` 修复与未入库的 `.trellis/tasks/`，均不在发布内容内 |

## 内容

体验大改（bold-overhaul）：自动游玩接管游戏原生加速并在暂停/停止时恢复；无需取舍的小步骤零请求自动处理；整回合规划一次请求给出多步出牌、逐帧复核、Boss 召唤/变招即停；HUD 横幅/出牌描边/目标连线/理由气泡，F7 暂停、F8 面板；局末成绩单与最多 50 局的战绩摘要。MCP 两面载荷瘦身与同会话去重（#233）；静态运行前缀缓存分离、Boss 回合规划分歧检测、最小决策间隔（#234）；流式与非流式工具参数都被截断时按正文 JSON 出牌的服务商级降级。详见 [CHANGELOG.md](../CHANGELOG.md) 的 0.18.0 段。

## 验证与限制

- 离线：preflight（构建、Python 编译、MCP 导入、C# 测试、12 项静态门禁、预算代理自测、release 元数据核对）在打包机上通过；Release 构建 0 警告 0 错误。
- 打包机（DSH 沙箱）有 9 个测试因环境限制失败（临时文件写入、环回监听、本机 HTTP 等需要真实文件/套接字的用例），与 2026-10-27 记录的「其余环境失败」同类，已复核与本次变更无关。
- **实机边界（CHANGELOG 同述）**：#233、#234 在 2026-10-29 隔离实机之后合入，仅经离线测试；牌框描边/目标连线、F7 热键、联机队友与整局胜率未实机验证；「工具参数截断改正文出牌」仅在模拟截断响应上测过，未在出问题的网关上实测。按维护者决定随本版发布。
- 打包时发现 `scripts/build-mod.ps1` 未给 Godot 传 `--log-file`，沙箱内默认 `user://logs/` 不可写致 headless 崩溃；已在打包机上修复（提交见 dev 后续提交）。

## 工坊上传

1. 从 `e1a61ab`（dev 领先 main 的 docs 合并提交）用 `package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public -ChangeNote <v0.18.0 note>` 打包到 `build/steam-workshop/sts2-ai-agent-v0.18.0/`（content 2,193,555 字节，5 个文件）。工坊 DLL 与 GitHub 包的哈希不同：此包在 `8ee2927` 之后重新构建，含 docs 合并提交，版本号同为 0.18.0。
2. 前两次 `ModUploader upload -w <workspace> -i 3796486050` 在系统代理 `127.0.0.1:10808` 开启时先后卡在 `k_EItemUpdateStatusPreparingContent` 与 `k_EItemUpdateStatusPreparingConfig` 数分钟，与 README 记录的本机故障一致（登录正常、SteamPipe 传输被代理拖死）。
3. 保存 `ProxyEnable`/`ProxyServer` 到 `build/proxy-backup-v018.json`，将 `ProxyEnable` 置 0，重启 Steam 待其载入（webhelper 带 `steamid=76561198420578597`），重跑同一命令：约 60 秒内经 `PreparingContent → UploadingContent → CommittingChanges` 完成，`workshop_log.txt` 记 `Upload finished for workshop item 3796486050 : OK`（02:07:05）。随后在 `finally` 语义下恢复 `ProxyEnable=1`。
4. `No content change detected` 是因为前一次被中断的尝试已把内容传完，本次只补提交了元数据与预览；本地 `content/` 字节总和与上传侧一致。

## 仍需手工

- 工坊**简体中文列表**仍需在网页端粘贴 [description.zh-CN.txt](../steam-workshop/description.zh-CN.txt)；ModUploader 无语言参数。
- 补做上表实机边界中列出的未验证项。
