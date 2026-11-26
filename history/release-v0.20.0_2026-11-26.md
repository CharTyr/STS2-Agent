# v0.20.0 发布记录（2026-11-26）

> 历史快照。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

- [PR #262](https://github.com/CharTyr/STS2-Agent/pull/262)、[PR #263](https://github.com/CharTyr/STS2-Agent/pull/263) 与 [PR #264](https://github.com/CharTyr/STS2-Agent/pull/264) 经 `dev` 合入 `main`。发布标签 `v0.20.0` 指向 `21d2d849c052e38a042caacd81196a734ba05346`。
- [GitHub Release](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.20.0) 已公开。Windows ZIP 为 1032252 字节，GitHub 报告的 SHA-256 为 `8052263B18ADB924432E200335A65C0974E0E63EA5B4A140B1A7DEF1810A1B5C`，与本地 ZIP 一致。
- [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) 的现有条目已公开更新。北京时间 19:47:08，服务端确认 `visibility=0`、`file_size=2345624` 和内容 ID `9155003340227920676`，与本地 `content/` 字节和一致。
- 两渠道从干净的工作树构建，共用 2305024 字节的 DLL，SHA-256 为 `BA5AC7DF2A6C590946788CB3F7174364502272E961EB5987D5DE3295555D574A`。本地构建指纹分别覆盖 GitHub 的 33 个文件和 2983123 字节、工坊的 5 个文件和 2345624 字节。[构建指纹索引](build-fingerprints.md)

## 验证

- 同一份 v0.20.0 发布 DLL 在 Steam 正式版 `v0.111.1` 和 public beta `v0.114.0` 的隔离沙箱中通过双版本发布兼容门禁（主菜单、战斗状态、原生 MCP、卡牌升级预览与实际出牌）。
- 有窗口隔离实机内置自动游玩探针（`scripts/test-in-game-autoplay.ps1`）在 `v0.114.0` 上分别通过零成本本地 Mock OpenAI SSE 与真实在线模型（`deepseek-v4.1-flash`）验收：主菜单→选角→首次引导→地图→第一层战斗出牌全程仅 4 次请求、选角加载期 0 次浪费请求、`active_faults=0`、Godot Overlay/HUD 0 报错、真实 Steam 存档与配置文件 0 改动。
- 24 例跨屏决策基准（`scripts/run_decision_benchmark_eval.py`）使用 `deepseek-v4.1-flash` 实测 24/24 全答，综合得分 91.35%（84.5 / 92.5，22/24 满分，战斗算术/多资源/冰球/流沙/Blitz 商店用例全满分）。
- 本地发布前静态检查与三轮 PR 的 GitHub CI（`contracts`、`linux-offline`）全部通过：C# 核心单测 1,195 项、Python MCP 单元测试 466 项、决策基准单测 19 项、离线 ABI 审计、12 项静态验证闸门均全绿。
- 工坊发布验证以 Steam `workshop_log.txt` 上传完成日志与公开 Steam Web API 复核为准。
