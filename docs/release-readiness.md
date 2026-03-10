# 发布验收清单

更新时间：`2026-03-11`

目标不是“代码能编译”，而是让这个 STS2 MCP Mod 达到可正式发布、可稳定使用的状态。发布前必须同时满足静态门槛、安装门槛、MCP 门槛、以及完整实机链路门槛。

---

## 1. 静态门槛

以下命令必须全部通过：

```powershell
dotnet build "C:/Users/chart/Documents/project/sp/STS2AIAgent/STS2AIAgent.csproj" -c Release
python -m py_compile "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/client.py" "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/server.py"
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

也可以直接运行：

```powershell
powershell -ExecutionPolicy Bypass -File "C:/Users/chart/Documents/project/sp/scripts/preflight-release.ps1"
```

通过标准：

- Mod C# 项目可在 `Release` 配置下编译
- MCP Python 源码可编译
- MCP server 包可导入并创建 `FastMCP` 实例
- 发布相关文档齐全

---

## 2. 安装门槛

### Mod 安装

```powershell
powershell -ExecutionPolicy Bypass -File "C:/Users/chart/Documents/project/sp/scripts/build-mod.ps1" -Configuration Release
powershell -ExecutionPolicy Bypass -File "C:/Users/chart/Documents/project/sp/scripts/test-mod-load.ps1" -DeepCheck
```

通过标准：

- `STS2AIAgent.dll` 已复制到游戏 `mods/` 目录
- `STS2AIAgent.pck` 已复制到游戏 `mods/` 目录
- 启动游戏后 `/health`、`/state`、`/actions/available` 均可返回成功

### MCP 启动

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv sync
uv run sts2-mcp-server
```

通过标准：

- MCP server 可正常启动
- MCP 客户端可调用 `health_check`
- Mod 未启动时返回的错误可理解，不是崩溃

---

## 3. 实机链路门槛

### A. 开局链路

必须覆盖：

1. `CHARACTER_SELECT`
2. `select_character`
3. `embark`
4. 如出现 `MODAL` / FTUE，执行 `confirm_modal` 或 `dismiss_modal`
5. 成功进入 `MAP`

通过标准：

- `character_select.selected_character_id` 与实际选择一致
- `available_actions` 在开局阶段准确暴露 `select_character` / `embark`
- Modal 出现时普通动作被正确拦截

### B. 地图与战斗链路

必须覆盖：

1. `MAP -> choose_map_node`
2. 进入 `COMBAT`
3. `play_card`
4. `end_turn`
5. 至少一次 `use_potion` 或 `discard_potion`
6. 战斗结束后进入奖励或地图

通过标准：

- 卡牌与敌人索引稳定
- 药水使用后槽位状态正确变化
- `available_actions` 与真实可操作 UI 一致

### C. 奖励链路

必须覆盖：

1. `claim_reward`
2. `choose_reward_card`
3. `skip_reward_cards`
4. `collect_rewards_and_proceed`

通过标准：

- 奖励列表与实际 UI 一致
- 跳过卡牌不会卡死
- 自动收集不会误停在中间状态

### D. 宝箱链路

必须覆盖：

1. `open_chest`
2. `choose_treasure_relic`
3. `proceed`

通过标准：

- 选 relic 后状态变化正确
- 不会把“已开箱但未选 relic”误判成可直接离开

### E. 事件链路

必须覆盖：

1. 普通事件选项
2. `event.is_finished = true` 后的 proceed 选项
3. 至少一个“事件 -> 战斗 -> 奖励 / 返回地图”的嵌套流程

通过标准：

- 事件选项列表会随着事件推进刷新
- 嵌套战斗时不会错误保留旧 `event` 状态

### F. 休息点链路

必须覆盖：

1. `choose_rest_option` 走 `HEAL`
2. `choose_rest_option` 走 `SMITH`
3. `select_deck_card` 选择升级牌
4. `proceed`

通过标准：

- `SMITH` 不会让 HTTP 调用卡死
- 选牌后可回到 `REST` 并继续离开

### G. 商店链路

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

### H. 收尾链路

必须覆盖：

1. 进入 `GAME_OVER`
2. 读取 `game_over`
3. `return_to_main_menu`

通过标准：

- `game_over` 字段不为 `null`
- 可以稳定退出到主菜单，不会卡死在总结页

---

## 4. 文档门槛

以下文档必须与代码一致：

- [api.md](/C:/Users/chart/Documents/project/sp/docs/api.md)
- [roadmap-current.md](/C:/Users/chart/Documents/project/sp/docs/roadmap-current.md)
- [phase-4c-shop.md](/C:/Users/chart/Documents/project/sp/docs/phase-4c-shop.md)
- [phase-5-full-chain.md](/C:/Users/chart/Documents/project/sp/docs/phase-5-full-chain.md)
- [mcp_server/README.md](/C:/Users/chart/Documents/project/sp/mcp_server/README.md)

通过标准：

- 不再出现“代码已实现，但文档还写 `null` / 未实现”
- MCP 工具列表与 `server.py` 实际注册一致
- 用户按文档能完成安装、启动、排错

---

## 5. 发布前最后确认

全部满足后，才算进入“可发布”状态：

- [ ] 静态检查通过
- [ ] Release 构建可安装
- [ ] MCP server 可启动
- [ ] 关键房间与战斗全链路实机通过
- [ ] Modal / FTUE 不再阻塞自动流程
- [ ] Game Over 可正确收尾
- [ ] 文档已同步
- [ ] 已记录已知限制与剩余风险

如果其中任何一项失败，就还不能叫“正式发布完成”，笨蛋。
