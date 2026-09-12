# 设计

## 1. `test-main-menu-active-run.ps1`（活动存档菜单）

python 的结论是契约：有存档时 `open_timeline` 被禁用。改法：

- 删除 `:92` 的 `Assert-ActionAvailable ... "open_timeline"`；
- `:112-115` 的等待条件改用活动存档菜单的真实标志（`continue_run` 或 `abandon_run`，与 `:88` 一致）；
- `:117-152` 的 timeline 序列删除（该路径已由 `run_sts2_validation.py` 的 `suite_new_run_lifecycle`
  在无存档菜单覆盖）；若想保留诊断，改成"报告 `open_timeline_available` 后跳过"。

## 2. `run_sts2_validation.py` 的 epoch 索引

```python
timeline_payload = timeline_state.get("timeline") or {}
slots = list(timeline_payload.get("slots") or [])
actionable = next((slot for slot in slots if slot.get("is_actionable")), None)
if actionable is None:
    raise ValidationError("choose_timeline_epoch is available but no slot reports is_actionable")
timeline_state = ensure_action_ok(
    client.action("choose_timeline_epoch", option_index=int(actionable["index"])),
    "choose_timeline_epoch",
)["data"]["state"]
```

契约依据：`TimelineIndexContractTests.ExecutorIndexesTheSameSlotListTheStateExposes`
（执行器与 state 用同一槽位列表，不得二次过滤）与 `NonActionableSlotsAreRejectedExplicitly`
（非 actionable 槽 409 `invalid_target`）。

## 3. `test-multiplayer-lobby-flow.ps1` 的屏幕 switch

在 `:655` 的 switch 补：

- `"CAPSTONE_SELECTION"`：`choose_capstone_option` + `option_index = 0`；
- `"UNLOCK"`：`confirm_unlock`。

兜底 `throw` 已带完整 JSON dump，确认其中含屏幕名与可用动作即可。

## 4. python 屏幕名清单

在 `run_sts2_validation.py` 的 run-progression 区域（`settle_*` / `collect_rewards_if_needed` 附近）
加注释块：把 `ResolveNonModalScreen` 的 24 个返回值分成"正常流程会消费/由 debug 命令绕过/不会出现"，
把确实会在正常流程出现却没有分支的屏幕补上处理。分类结论进 evidence。
`api-facts` gate 会从源码提取屏幕枚举，注释与其同源，不重复维护。

## 陷阱

- `scripts/*.ps1` 含非 ASCII 必须 UTF-8 BOM；改后跑 `--only script-encoding`。
- python 改动后 `python -m py_compile scripts/run_sts2_validation.py`。
- 不引入对 `STS2_ENABLE_DEBUG_ACTIONS` 的新依赖（gating suite 已覆盖 debug 命令）。

