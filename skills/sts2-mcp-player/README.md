# STS2 MCP Player 使用说明

这是一个给 Codex/Agent 用的技能，目标是更稳定地通过 `sts2-ai-agent` MCP 接口游玩或验证《Slay the Spire 2》。

它解决的核心问题是：

- 每一步都先读状态，再决定动作
- 不误用 `proceed`、旧索引、过期 screen 信息
- 区分普通游玩、全量验证、debug 注入三种场景
- 优先使用精简的 guided 工具集，减少 AI 被过多 MCP 工具干扰

## 适用场景

在这些情况下调用它：

- 让 Agent 直接玩一局 STS2
- 让 Agent 接管战斗、事件、商店、奖励、休息点等流程
- 验证 MCP 行为是否和游戏状态一致
- 在开启 debug actions 时复现特定战斗或状态问题

## 如何触发

在提示词里直接提到技能名：

```text
使用 $sts2-mcp-player 继续当前这一局，优先走稳定流程，不要乱点 debug。
```

也可以这样写：

```text
用 $sts2-mcp-player 帮本小姐检查当前 reward 流程有没有异常，并把关键状态告诉我。
```

```text
用 $sts2-mcp-player 跑一遍主菜单到新开局的完整链路，必要时做 MCP 验证。
```

## 推荐前置条件

普通游玩：

- 游戏已启动
- mod 已加载
- MCP server 可访问

验证模式：

- 先运行 `scripts/build-mod.ps1`
- 再确认 `/health` 正常

debug 模式：

- 明确启用 `STS2_ENABLE_DEBUG_ACTIONS=1`
- 只在开发和复现问题时使用 `run_console_command`

## 标准使用方式

### 1. 普通游玩

让 Agent 接管当前局面：

```text
使用 $sts2-mcp-player 接管当前局面。
先检查 health 和 game state，
然后按 state-first 规则继续推进，遇到 card selection、reward、shop 时按技能约定处理。
```

### 2. 单点状态处理

只处理当前 screen：

```text
使用 $sts2-mcp-player 只处理当前 screen。
如果现在是 CARD_SELECTION，就把这一步结清后停下并汇报结果。
```

### 3. 战斗接管

```text
使用 $sts2-mcp-player 处理当前战斗。
优先不浪费免费价值，按最新 hand 和 enemy 状态决策，不要复用旧索引。
```

### 4. 流程验证

```text
使用 $sts2-mcp-player 验证当前 MCP 流程。
优先使用 guided 工具集，必要时再切到 full profile。
检查 state.available_actions 是否与当前 screen 一致。
```

### 5. Debug 复现

```text
使用 $sts2-mcp-player 复现一个 potion/card-selection 相关 bug。
只有在检测到 debug actions 可用时才使用 run_console_command，
否则退回普通 MCP 流程。
```

## 这个技能会怎么工作

它的核心循环很简单：

1. 先 `health_check`
2. 每次决策前先 `get_game_state`
3. 只调用 `available_actions` 里真实存在的动作
4. 动作后重新读取状态
5. 所有索引都从最新 payload 重新计算

这意味着它特别适合处理这些容易出错的情况：

- `REWARD` 里卡牌奖励和底层 reward item 不是一回事
- `CARD_SELECTION` 会覆盖原本房间 screen
- `SHOP` 分为外层房间和内层库存
- `EVENT` 可能在战斗后回到事件本体
- `MAIN_MENU` 里 timeline gate 会阻塞开局

## 工具使用建议

优先使用 guided profile：

- `health_check`
- `get_game_state`
- `get_available_actions`
- `act`

只有在这些情况下才建议用 full profile：

- 需要逐动作覆盖测试
- 需要验证 legacy per-action tools
- 需要明确比较 guided 和 full 的工具暴露差异

## 常见注意事项

- 不要把 `completed` 当成绝对完成，仍然要看返回的 `state`
- 不要在 reward 流程里乱用 `proceed`
- 不要假设一次 `select_deck_card` 就一定结束多选流程
- 不要在 `shop.is_open=true` 时直接认为商店已经处理完
- 不要在 debug 不可用时依赖 `run_console_command`

## 相关文件

- 技能主说明：
  [SKILL.md](./SKILL.md)
- 分 screen 剧本：
  [screen-playbooks.md](./references/screen-playbooks.md)
- 调试与验证说明：
  [debug-and-validation.md](./references/debug-and-validation.md)
- UI metadata：
  [openai.yaml](./agents/openai.yaml)

## 一句话模板

如果你懒得写，就直接用这一句：

```text
使用 $sts2-mcp-player 接管当前 STS2 MCP 会话，按 state-first 工作流推进，并在每个关键阶段汇报当前 screen、可用动作和下一步决策理由。
```
