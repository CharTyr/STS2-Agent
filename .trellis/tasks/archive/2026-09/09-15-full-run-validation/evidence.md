# v0.12.4 完整实机验证（2026-09-15）

主机：全新隔离档 `--clientId 2026091504`，API 18080；队友 18081；游戏 v0.111.0；mod **0.12.4**
（DLL SHA256 `0134C8BC10A76F15C71CAFFEAC7D818D71744111A4045DDF52AA80A5AA51A5BF`，与工坊上传包逐字节相同）。
第三方 mod（DamageMeter / SpeedX / RemoveMultiplayerPlayerLimit / 储君娘化）在测试期间移出，结束后还原。

## 验证到的东西

1. **全新 clientId 首次启动的种子**：`%APPDATA%/SlayTheSpire2/default/2026091504/settings.save` 由种子逻辑克隆 Steam 模板生成——
   4538 字节、`mods_enabled=true`、10 个 mod 条目、`language` 保持 `zhs`；游戏随即加载 mod，`/health` 报 `mod_version=0.12.4`。
2. **完整一局**：邀请队友（200 `pending` → 68 秒 `Succeeded`，队友 PID 58976 / 18081 / `auto_play=false`）→ 双方选人 → embark →
   地图投票 → 三场战斗 → 奖励结算 → 回到地图 → 事件界面。
3. **战斗内不变量**（本轮新覆盖的项）：在 COMBAT 快照上跑了 8 轮 `state-invariants`，`checked_actions` 为 0/1/2，
   主机与队友两侧 `failure_count` 全为 0；奖励界面与回到地图后的检查同样为 0。
4. 战斗推进：敌人分别从 11 / (57,19) / (10,27,23) 清空；双方血量整轮由 80/80 变为 79/64（第一次奖励时）。

## 发现并修掉的缺陷：首启种子在「模板已就绪」时反而退化

**现象**：全新 clientId `2026091502`（以及后来手造的 `2026091503`）启动后 mod 根本没加载，`/health` 拒绝连接；
落盘的 settings.save 是 3297 字节、`mods_enabled` 丢失、只剩一条 mod 记录——游戏显然没有接受那份种子文件。

**根因**：PowerShell 侧的就绪判定与修复都用固定 ±200 字符窗口围绕 `"id": "STS2AIAgent"`。Steam 模板的 mod 列表里，
AI 队友条目后面紧跟着 `DamageMeter`，其 `is_enabled` 是 `false`，正好落进窗口——于是：

1. 一个**完全可用**的模板克隆被判成「未就绪」（`ready_before=False`，实测）；
2. 修复流程被唤起，而它的翻转正则又用同一个窗口，把**下一个 mod** 的 `is_enabled: false` 改成了 `true`；
3. 修复后的文本仍然判不通过 → 写出 788 字节的精简兜底；
4. 游戏不认这份精简文件，启动时重写成默认值并丢掉 `mods_enabled` → mod 不加载。

**修复**：新增 `Get-IsolatedAgentEntryBody`（按大括号配对定位 AI 队友自己的那个对象），就绪判定与 `is_enabled` 翻转都只在
该对象内进行；`scripts/start-game-session.ps1`。修复后同一模板 `ready_before=True`（无需修复），全新 clientId 拿到完整克隆。
回归测试：`mcp_server/tests/test_posix_script_portability.py` 新增 2 条（入口函数存在并被两处调用；不再有固定字符窗口判定 `is_enabled`）。

## 已修的缺陷：单帧状态自相矛盾（0.12.4 同号重发修复）

自动化采样在 146 个战斗快照中抓到 1 次（`ready` 且手上有可出牌的样本里 36 次中 1 次）：
同一份 `/state` 响应内，`combat.action_readiness` 说 `can_use_combat_actions=true`（`reason=ready`、`snapshot_stable=true`、
`hand_mode=Play`），手牌两张都 `playable=true` / `can_play_result=true`，但 `available_actions` 只有
`discard_potion` / `use_potion`——没有 `play_card`，也没有 `end_turn`。
完整快照存于 `%TEMP%/sts2_mismatch_dump.json`（当场捕获，非推断）。

判读（2026-09-15 更正）：早前把 dump 顶层的 `"energy": null` 读成「`combat.energy` 为 null、战斗载荷只装了半份」是**错误**的：
`CombatPayload` 本来就没有顶层 `energy` 字段（能量在 `combat.player.energy`），那个 `null` 是采集脚本自己写进 dump 的探针字段，
它不构成任何证据。真正的根因是**同一份响应内对战斗门禁求值了多次**：`BuildAvailableActionNames`（end_turn → play_card → … → 药水）
与随后 `BuildCombatPayload` 里的 readiness 各自独立调用 `CanUseCombatActions`，而其中的稳定采样器带 200ms 窗口、签名变化即重置。
响应跨过那个窗口时，前半段判「未稳定」而不给 `play_card`/`end_turn`，后半段的药水与 readiness 判「已稳定」——同一份响应自我矛盾。
影响：轮询方可能看到一帧没有出牌入口（下一次轮询即恢复）；`state-invariants` 若正好采到那一帧会报
`missing action 'play_card'`——本轮所有不变量调用都没踩中，两侧 0 失败。

修复（0.12.4 同号重发）：`BuildStatePayload` 现在只求值一次 `CombatActionGate`，把同一份结果传给
`BuildAvailableActionNames`、`BuildCombatPayload`、`BuildRunPayload`；`combat.action_readiness` 变成这份门禁的纯投影，
只读的 `IsCombatActionSnapshotCurrentlyStable` 被删除（它正是能在同一响应内「改口」的那一半），门禁同时补上了此前只在
readiness 里检查的 `modal_open` 与 `combat_paused` 两个锁。契约测试见 `STS2AIAgent.Tests/GameStateCombatGateContractTests.cs`。

## 未覆盖

- 事件界面之后的地图内容（商店 / 营火 / 宝箱 / boss）与完整通关；本次只到第一个事件。
- 工坊订阅侧加载（订阅 → 重启 Steam → 确认拉到 0.12.4）未做。
- `state-invariants` 在事件 / 商店 / 营火界面的断言未单独采样。
