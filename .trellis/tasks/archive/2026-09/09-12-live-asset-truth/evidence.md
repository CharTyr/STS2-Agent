# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `scripts/test-main-menu-active-run.ps1` | 删除 `:92` 的 `open_timeline` 硬断言与 `:117-152` 的 timeline 交互序列；等待条件改用 `continue_run`；输出改为诊断字段 `open_timeline_available_with_active_run` |
| `scripts/run_sts2_validation.py` | `suite_new_run_lifecycle` 的 `choose_timeline_epoch` 改取 `timeline.slots[]` 中第一个 `is_actionable` 槽的 `index`，并加守卫（无可用槽时抛 `ValidationError`）；新增 24 值屏幕分类注释块；`settle_main_menu` / `continue_from_main_menu_if_needed` 补 `confirm_unlock` 臂 |
| `scripts/test-multiplayer-lobby-flow.ps1` | 屏幕 switch 补 `CAPSTONE_SELECTION`（`choose_capstone_option` + `option_index = capstone.options[0].i`）与 `UNLOCK`（`confirm_unlock`）；兜底 `throw` 带上 `screen=` 与 `available_actions=[...]` |

合计 `+88 / -53`，无新增仓库文件。检出的 `scripts/__pycache__/` 被 `.gitignore:12` 忽略。

## 验收

| 标准 | 证据 |
|---|---|
| 两边 timeline 结论一致 | ps1 不再要求 `open_timeline`；python 侧原本只输出诊断字段。与 `run_sts2_validation.py:1548-1550`（`NMainMenu.UpdateTimelineButtonBehavior` 在有存档时禁用该按钮）一致 |
| epoch 索引不写死 | 红：把索引改回 `option_index=0`，payload `slots=[{index:0,is_actionable:false},{index:1,is_actionable:true}]` → `RED: mixed payload did NOT pick the first actionable slot (index 1)`（实机即 409 `invalid_target`）；绿：取到 `option_index 1`。守卫红：改成 `if False:` → `TypeError: 'NoneType' object is not subscriptable`（不可读），恢复后回到 `ValidationError` |
| lobby switch 覆盖 | 用 AST 抽取真实 `Invoke-LocalRunProgressionStep` + mock `Invoke-Action`：`PASS: CAPSTONE_SELECTION -> choose_capstone_option option_index 0`、`PASS: UNLOCK -> confirm_unlock`、`PASS: MAP -> null`、`PASS: unsupported fallback names screen=CRYSTAL_SPHERE`。红：把标签改到不匹配 → 落到兜底且报出 `screen=` 与 `available_actions=[...]` |
| python 屏幕覆盖与清单 | `settle_main_menu` 的 `confirm_unlock` 臂红/绿：无该臂时 `AssertionError: []`（实机表现为等待超时），有该臂时从 `UNLOCK` 回到 `MAIN_MENU` |
| 编码 / 编译 / gate | 两个 ps1 仍为纯 ASCII（`nonAscii=0, bom=False`，gate 只对含非 ASCII 的脚本要求 BOM，未破坏）；`py_compile` OK；`Parser::ParseFile` 两个 ps1 均 0 error；`--only script-encoding` ok |
| 全量门禁 | C# 347 PASS / 0 FAIL；MCP 167 OK；6 gate 全绿；preflight exit 0 |

## 屏幕消费清单（prd 第 4 项要求落盘）

- **由脚本经 Mod API 消费**：`MAIN_MENU`、`CHARACTER_SELECT`、`COMBAT`、`CARD_SELECTION`、`REWARD`、`GAME_OVER`、`MULTIPLAYER_LOBBY`、`TIMELINE`、`UNLOCK`、`UNKNOWN`。
- **不进入，改用 debug 控制台推进**：`MAP`、`EVENT`、`CHEST`、`REST`、`SHOP`、`FAKE_MERCHANT`、`CAPSTONE_SELECTION`、`BUNDLE_SELECTION`、`CRYSTAL_SPHERE`。
- **玩家驱动，脚本不应打开**：`CARDS_VIEW`、`CARD_INSPECT`、`RELIC_INSPECT`、`PATCH_NOTES`、`FEEDBACK`。
- `MODAL` 不在 `ResolveNonModalScreen` 的 24 值内，由 modal 路径产生，归 `dismiss_blocking_modal` / `resolve_modals`。

## 只能实机验证

- 活动存档主菜单是否确实不再因缺 `open_timeline` 失败，且 `dismiss_modal` 后能等到 `continue_run` 并进入存档。
- 真实 timeline：槽 0 非 actionable 时选中第一个 actionable 槽而不返回 409；无 actionable 槽时给出显式 `ValidationError`。
- 死亡后 `settle_main_menu` 在真实 `UNLOCK` 覆盖层上逐层 `confirm_unlock` 回到可用菜单。
- 多人脚本跨过 `CAPSTONE_SELECTION` / `UNLOCK` 不再抛 `Unsupported run progression state`。

