# v0.19.5 发布记录（2026-11-22）

> 历史快照。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

- [PR #254](https://github.com/CharTyr/STS2-Agent/pull/254) 合入 dev，[PR #255](https://github.com/CharTyr/STS2-Agent/pull/255) 合入 main，发布提交为 `8430f37b33543eace8c40f822e5b0ef99976f831`。
- [GitHub Release](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.5) 已公开。`sts2-ai-agent-v0.19.5-windows.zip` 为 996098 字节，发布资产的 SHA256 为 `7F57D790CB69303B3F7653F936470F170A022E85B47569B64EE333425D1C7400`。
- [Steam 工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) 保持公开。Steam 在 2026-11-22 00:20:36 更新内容，服务端确认 `file_size=2277010` 与本地内容总量相同，`hcontent_file=8772943972341413861`。
- 两个渠道共用 DLL `AE4F31D30A8D125B4E79CDA2479CB1B186FA6077A2E7ADB5CD611DDC1FAF183E`，2236416 字节。GitHub 与工坊的干净工作树指纹分别为 33 文件 / 2908631 字节和 5 文件 / 2277010 字节。见[构建指纹索引](build-fingerprints.md)。
- 版本要求为游戏 v0.113.0 或以上。完整更新见 [CHANGELOG](../CHANGELOG.md)。

## 验证

- 本地发布预检及两级 PR 的 GitHub contracts 检查通过。C# 1,174 通过、2 项 Windows 回环监听测试因环境限制跳过，Python 462 通过，12 项静态闸门通过。
- v0.113.1 隔离实机回归了射击重放、闪电与厄运先后，以及 Dread 轮换选角。最终发布包另通过加载、主菜单与状态一致性验证。
- 未通过 Steam 订阅路径重新安装。独立简体中文工坊页面文案未另行编辑。
