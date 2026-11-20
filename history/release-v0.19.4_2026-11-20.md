# v0.19.4 发布记录（2026-11-20）

> 历史快照：仅记录本次发布证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| PR / 提交 | [#250](https://github.com/CharTyr/STS2-Agent/pull/250) → dev；[#251](https://github.com/CharTyr/STS2-Agent/pull/251) → main；tag `v0.19.4` → `ada36ab361ddac7bd3e8b74422597af21fa082e0` |
| GitHub Release | [v0.19.4](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.4)；`sts2-ai-agent-v0.19.4-windows.zip`，994799 字节；SHA256 `1EC10052D0955DDFF1DFCD4C60FD98140A1E72087404DE5ADE061833DF66690B` |
| Steam 工坊 | [3796486050](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050)，公开；2026-11-20 13:05:54 `Upload finished … : OK`；Web API `file_size=2274963` 与本地内容字节和相等，`hcontent_file=7917707886145540801` |
| 共用 DLL | SHA256 `E7F37C7061ED12923EFB3C6E90B31C83DD624440D36B5B754A6AB894CF73289B`（2234368 字节） |
| 指纹 | `build/release/sts2-ai-agent-v0.19.4-windows-fingerprint.json`（33 文件 / 2907767 字节） |

## 改动

- Windows `HttpListener` 报错误 6（无效句柄）时，同一端口的 REST、原生 MCP 与 SSE 改由回环 socket 传输提供；路由、Host/Origin 校验、队友令牌与端口策略不变。
- 两种传输都起不来时，主窗口保留悬浮窗与单人功能，MCP 与 AI 队友明确不可用。

## 实机验证（游戏 v0.113.1，隔离副本，无模型 Key）

- 传输切换日志与 `/health`、`/state`、`/mcp/control`、MCP `initialize`/`tools/list`（16 个工具）。
- 双开进入同一局；队友座位由外部客户端经原生 MCP 打完一整场战斗（2 次 `play_card` + 1 次 `end_turn`，战斗结束进入下一屏），队友 `play_phase=paused`、`session_requests=0`，主窗未被代打。
- 固定端口被占用：`Ready without a local API` + `Overlay Installed`，该端口无 API。

## 未验证

- Host/Origin 与队友令牌在 socket 上的拒绝用例未逐条实机重放。
- DSH 客户端本身未接入；只验证了原生 MCP 协议层。
- 工坊与 GitHub 产物布局未用新包重新实测（实机使用同源码的本地 DLL）。
