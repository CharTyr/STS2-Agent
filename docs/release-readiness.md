# 发布验收清单

- 更新时间：`2026-03-11`
- 目标：让这个 STS2 MCP Mod 达到“可正式发布、可完整使用、可稳定验证”的状态

发布前必须同时满足静态门槛、安装门槛、MCP 可用性门槛，以及完整实机链路门槛。

## 1. 静态门槛

以下命令必须全部通过：

```powershell
dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj" -c Release
python -m py_compile "<repo-root>/mcp_server/src/sts2_mcp/client.py" "<repo-root>/mcp_server/src/sts2_mcp/server.py"
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

也可以直接运行：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/preflight-release.ps1"
```

通过标准：

- Mod C# 项目可在 `Release` 配置下编译
- MCP Python 源码可编译
- MCP server 包可导入并创建 `FastMCP` 实例
- 发布相关文档齐全且与代码一致

## 2. 安装门槛

### Mod 安装

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/build-mod.ps1" -Configuration Release
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-mod-load.ps1" -DeepCheck
```

通过标准：

- 运行 `build-mod.ps1` 前，已通过 `-GodotExe` 或 `GODOT_BIN` 提供 Godot 控制台可执行文件
- `STS2AIAgent.dll` 已复制到游戏 `mods/` 目录
- `STS2AIAgent.pck` 已复制到游戏 `mods/` 目录
- 启动游戏后 `/health`、`/state`、`/actions/available` 都能成功返回

### MCP 启动

```powershell
cd "<repo-root>/mcp_server"
uv sync
uv run sts2-mcp-server
```

通过标准：

- MCP server 可正常启动
- MCP 客户端可调用 `health_check`
- Mod 未启动时返回的错误可理解，不是崩溃

## 3. Debug 门槛

调试控制台必须是“开发时可选开启，发布默认关闭”。

验证命令：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1"
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1" -EnableDebugActions
```

通过标准：

- 默认情况下 `run_console_command` 返回 `invalid_action`
- 显式启用 `STS2_ENABLE_DEBUG_ACTIONS=1` 后，`run_console_command` 可用
- MCP server 仅在启用该环境变量时注册 `run_console_command`

## 4. 实机链路门槛

### A. 主菜单与开局链路

必须覆盖：

1. `MAIN_MENU -> open_character_select`
2. 有存档时：`continue_run` 和 `abandon_run -> confirm_modal`
3. 如有时间线门控：`open_timeline -> choose_timeline_epoch -> confirm_timeline_overlay -> close_main_menu_submenu`
4. `select_character`
5. `embark`
6. 如出现 `MODAL` / FTUE：`confirm_modal` 或 `dismiss_modal`
7. 成功进入 `MAP` 或 `EVENT`

通过标准：

- `character_select.selected_character_id` 与实际选择一致
- `available_actions` 在不同主菜单阶段暴露正确
- 时间线门控出现时可完整闭环
- Modal 出现时普通动作会被正确拦截

### B. 地图与普通战斗链路

必须覆盖：

1. `MAP -> choose_map_node`
2. 进入 `COMBAT`
3. `play_card`
4. `end_turn`
5. 至少一次 `use_potion` 或 `discard_potion`
6. 战斗结束后进入奖励或地图

通过标准：

- 卡牌索引与敌人索引稳定
- 药水使用 / 丢弃后状态正确变化
- `available_actions` 与真实 UI 一致

### C. 动态战斗状态门槛

这是正式发布前必须额外补验的一类高风险点。

必须覆盖：

1. 战斗中临时费用变化
2. 星星资源变化
3. 星星 X 费卡
4. 不可打出原因变化

推荐样本：

- `Bullet Time`
- `Falling Star`
- `Stardust`
- 储君（`Regent`）开局

通过标准：

- `energy_cost` 能反映战斗中的临时修正
- `star_cost` 能反映战斗中的临时修正
- `costs_x` 只表示能量 X 费
- `star_costs_x` 正确标记星星 X 费卡
- `unplayable_reason` 会随星数 / 能量变化而刷新，例如 `not_enough_stars`

### D. 奖励链路

必须覆盖：

1. `claim_reward`
2. `choose_reward_card`
3. `skip_reward_cards`
4. `collect_rewards_and_proceed`

通过标准：

- 奖励列表与实际 UI 一致
- 跳过卡牌不会卡死
- 自动收集不会停在半中间状态

### E. 宝箱链路

必须覆盖：

1. `open_chest`
2. `choose_treasure_relic`
3. `proceed`

通过标准：

- 选 relic 后状态变化正确
- 不会把“已开箱但未选 relic”误判成可直接离开

### F. 事件链路

必须覆盖：

1. 普通事件选项
2. `event.is_finished = true` 后的 proceed 选项
3. 至少一个“事件 -> 战斗 -> 事件 / 地图”的嵌套流程

通过标准：

- 事件选项列表会随事件推进刷新
- 嵌套战斗时不会保留旧事件引用

### G. 休息点链路

必须覆盖：

1. `choose_rest_option` 走 `HEAL`
2. `choose_rest_option` 走 `SMITH`
3. `select_deck_card` 选择升级牌
4. `proceed`

通过标准：

- `SMITH` 不会让 HTTP 调用卡死
- 选牌后可回到 `REST` 并继续离开

### H. 商店链路

必须覆盖：

1. `open_shop_inventory`
2. `buy_card`
3. `buy_relic`
4. `buy_potion`
5. `remove_card_at_shop`
6. `select_deck_card`
7. `close_shop_inventory`
8. `proceed`

通过标准：

- 外层房间与内层库存状态切换准确
- 删牌链路不会卡在中间
- 购买后价格、金币、库存状态同步

### I. 收尾链路

必须覆盖：

1. 进入 `GAME_OVER`
2. 读取 `game_over`
3. `return_to_main_menu`

通过标准：

- `game_over` 字段不为 `null`
- 可以稳定回到主菜单

## 5. 文档与易用性门槛

以下文档必须与代码一致：

- [api.md](../docs/api.md)
- [roadmap-current.md](../docs/roadmap-current.md)
- [phase-4c-shop.md](../docs/phase-4c-shop.md)
- [phase-5-full-chain.md](../docs/phase-5-full-chain.md)
- [phase-6-validation-template.md](../docs/phase-6-validation-template.md)
- [phase-6-validation-2026-03-11.md](../docs/phase-6-validation-2026-03-11.md)
- [mcp_server/README.md](../mcp_server/README.md)

通过标准：

- 不再出现“代码已实现，但文档仍写未实现 / null”
- debug 工具默认关闭的策略写清
- 动态费用与星星 X 费字段写清
- 推荐 skill 和状态优先调用策略写清

## 6. 发布前最后确认

全部满足后，才算进入“可正式发布”状态：

- [ ] 静态检查通过
- [ ] Release 构建可安装
- [ ] MCP server 可启动
- [ ] debug 工具默认关闭 / 显式开启均已验证
- [ ] 关键房间与战斗全链路实机通过
- [ ] 动态费用 / 星星费 / 星星 X 费已专项验证
- [ ] Modal / FTUE 不再阻塞自动流程
- [ ] Game Over 可正确收尾
- [ ] 文档已同步
- [ ] 已产出完整的 Phase 6 实机记录
- [ ] 已记录已知限制与剩余风险

如果其中任意一项失败，就还不能叫“正式发布完成”，笨蛋。
