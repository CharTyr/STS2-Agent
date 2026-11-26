# v0.19.6 发布记录（2026-11-26）

> 历史快照。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

- [PR #258](https://github.com/CharTyr/STS2-Agent/pull/258) 合入 dev，[PR #259](https://github.com/CharTyr/STS2-Agent/pull/259) 合入 main。发布标签指向 `a188150de3b2fee2d83ed50c4cd4c977f64b238a`。
- [Issue #257](https://github.com/CharTyr/STS2-Agent/issues/257) 已随发布 PR 关闭。
- [GitHub Release](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.6) 已公开。Windows ZIP 为 998600 字节，GitHub 报告的 SHA-256 为 `D515529887C1C36AA38B7F33C3DAE6C92BF69E2B1FBBD1F0E7F9853D6642B8CB`，与本地 ZIP 一致。
- [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) 的现有条目已公开更新。北京时间 10:45:28，服务端确认 `file_size=2286744` 和内容 ID `682822038819754432`。发布前生成的上传目录逐文件核对了哈希。
- 两渠道从干净的工作树构建，共用 2246144 字节的 DLL，SHA-256 为 `66EBA42E8E27EED83CFB25E58BD5B137F69BDE8906B57B5DEB5AEFD4C22D16EA`。本地构建指纹分别覆盖 GitHub 的 33 个文件和 2920780 字节、工坊的 5 个文件和 2286744 字节。[构建指纹索引](build-fingerprints.md)

## 验证

- 同一份 v0.19.6 发布 DLL 在 Steam 正式版 v0.111.1 和 public beta v0.114.0 的隔离实例通过主菜单、战斗状态、原生 MCP、升级预览和实际出牌。正式版打出铁甲战士打击，公测版验证愚者三标签牌只计一次旋律出牌。
- 本地发布预检和两级 PR 的 GitHub contracts 检查通过。本地 C# 1,180 项通过，两个 Windows 回环监听测试因宿主限制跳过。Python 466 项和 12 项静态检查通过。
- 工坊发布验证以 Steam 上传日志和公开 Web API 为准。游戏加载验证使用隔离实例中的发布产物。
