# v0.19.3 发布记录（2026-11-19）

> 历史快照：仅记录本次发布证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| PR / 提交 | [#246](https://github.com/CharTyr/STS2-Agent/pull/246) → dev；[#247](https://github.com/CharTyr/STS2-Agent/pull/247) → main；tag `v0.19.3` → `f7ca4c4267574ee4e4762170a495add43b6a708e` |
| GitHub Release | [v0.19.3](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.3)；`sts2-ai-agent-v0.19.3-windows.zip`，980700 字节；SHA256 `EE32BDDB17829DD0D4CB986546107FECBA5EFB1B30DF0F13541E0C1457B7C716` |
| Steam 工坊 | 未上传（本次无工坊更新） |
| 共用 DLL | SHA256 `C4C446B72BABE87276760AFF2BB99849DEB015C9F1E90403A5DF47FD3F3DCD5F`，来自干净 `416ff4d` |
| 指纹 | `build/release/sts2-ai-agent-v0.19.3-windows-fingerprint.json`（33 文件 / 2874089 字节） |

## 改动

- 游戏 v0.113.1 把 `FastHost(GameMode)` 改成 `FastHost(GameMode, bool isCustom)`；旧代码对多参数签名传空参数，本机双开每次都以 `Parameter count mismatch` 死在建房之前。现在按完整参数类型精确选择签名（新签名传 `Standard, false`，否则回退旧单参数版），选中的形状不重试。
- 「外部 Agent」模式只为队友进程本次会话开启原生 MCP，主窗口多人页显示并可复制队友的 `/mcp` 地址；外部客户端用 `decide`/`act` 驱动队友，不需要 Mod 模型验证，也不需要点「继续游玩」。

## 验证

- 离线：对安装的 v0.113.1 程序集 Release 编译 0 警告 / 0 错误；FastHost 回归修复前以 `Parameter count mismatch` 变红、修复后 5/5 通过（含旧签名、拒绝未知重载、派发后不重试）；C# 全量 1166 通过 / 0 失败 / 2 跳过；12 项静态门通过；发布元数据一致。
- 实机：**未完成**。隔离副本启动 v0.113.1 后，游戏进程内的 Mod HTTP 监听无法启动（`HttpListenerException 6: bad handle`，`LoopbackListener.Bind → HttpServer.Start`），同一环境里普通进程创建 `HttpListener` 正常，因此拿不到 `/health` 与 API，双开建房与队友 MCP 出牌均无实机证据。待补清单见任务 `11-19-coop-fast-host-mcp-companion/verification.md`（仓库内 `.trellis/tasks/`）。
- 发布按用户明确指示进行，尽管实机验收未完成；本记录如实标注该缺口，不把离线结果当作实机通过。

## 边界

- 未调用任何付费模型，未修改玩家真实存档与正式安装。
- 未上传创意工坊；未创建新的工坊物品。
- 打包在独立 worktree（`416ff4d`，working tree clean）中进行；主工作区里另一并行会话的未提交改动未被带入产物。
