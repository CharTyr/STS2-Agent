"""Bounded combat-plan orchestration around the existing single action surface.

Plain act calls retain their old path and response. Opt-in plans stop on any
non-completed step, never replay an ambiguous action, and expose each result.
"""
from __future__ import annotations

import time
from typing import Any

from .action_results import project_agent_state
from .client import Sts2ApiError, Sts2Client
from .turn_plan import parse, resolve, diverged, find_card_index

_COMBAT_ACTIONS = {"play_card", "end_turn", "use_potion", "discard_potion"}
_DEBUG_ACTIONS = {"run_console_command", "inject_event_churn"}


def _stop(result: dict[str, Any]) -> str | None:
    if "error" in result:
        return "rejected"
    if result.get("status") == "outcome_unknown":
        return "outcome_unknown"
    if result.get("status") != "completed" or result.get("stable") is False:
        return "unsettled"
    return None


_SETTLE_SECONDS = 8.0


def _settled_state(client: Sts2Client, deadline: float) -> tuple[dict[str, Any], bool]:
    """Fresh compact state once the previous card has resolved, bounded by the plan deadline.

    Read at once, the frame is often mid-animation: its combat gate is closed and play_card is
    missing, which stopped every live in-game plan after one step (2026-10-29). Leaving combat also
    counts as settled; divergence handles it.
    """
    stop_at = min(deadline, time.monotonic() + _SETTLE_SECONDS)
    while True:
        current = project_agent_state(client.get_state())
        combat = current.get("combat") if isinstance(current, dict) else None
        readiness = combat.get("action_readiness") if isinstance(combat, dict) else None
        gate_closed = isinstance(readiness, dict) and readiness.get("can_use_combat_actions") is False
        if not gate_closed or current.get("screen") != "COMBAT":
            return current, True
        if time.monotonic() >= stop_at:
            return current, False
        time.sleep(0.1)


def execute_plan_act(
    client: Sts2Client, profile: str, action: str, *,
    card_index: int | None = None, target_index: int | None = None,
    option_index: int | None = None, x: int | None = None, y: int | None = None,
    tool: str | None = None, reason: str | None = None, raw_state: bool = False,
    card_cid: int | None = None, then: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    """Execute once, then optionally run at most nine checked combat follow-ups.

    The 30-second orchestration budget is checked before each new operation;
    individual HTTP calls retain the client's own bounded transport timeout.
    """
    action = action.strip().lower()
    if action in _DEBUG_ACTIONS:
        raise RuntimeError(f"{action} is gated separately and must use its own tool when enabled.")
    context: dict[str, Any] = {"source": "mcp", "tool_name": "act", "tool_profile": profile}
    if reason and reason.strip():
        context["decision_reason"] = reason.strip()
    # The card identity is forwarded, not resolved, for the dispatch itself: the mod re-checks it
    # against its live hand inside the one-action lease. A position resolved here would be one more
    # read that can go stale before the request lands, which is the race this field closes.
    play_card = action == "play_card"
    baseline = None
    if card_cid is not None or then:
        baseline = project_agent_state(client.get_state())
    if play_card and card_cid is not None:
        card_index = find_card_index(baseline, card_cid)
        if card_index is None:
            return {"error": {"code": "invalid_target", "message": "card_cid is not in the current hand", "retryable": False}}
    result = client.execute_action(
        action, card_index=card_index, target_index=target_index, option_index=option_index,
        x=x, y=y, tool=tool, raw_state=raw_state, client_context=context,
        card_cid=card_cid if play_card else None,
    )
    if not then:
        return result
    plan = parse({"action": action, "then": then})
    results = [result]
    stop = _stop(result)
    if stop is None and (not isinstance(baseline, dict) or baseline.get("screen") != "COMBAT"):
        stop = "illegal_action"
    deadline = time.monotonic() + 30.0
    for step in plan.then:
        if stop:
            break
        if time.monotonic() >= deadline:
            stop = "unsettled"
            break
        current, settled = _settled_state(client, deadline)
        stop = diverged(baseline, current) or (None if settled else "unsettled")
        if stop:
            break
        if step.action not in _COMBAT_ACTIONS:
            stop = "illegal_action"
            break
        resolved = resolve(step, current, require_cid=True)
        if not resolved.ok:
            stop = resolved.stop_reason
            break
        step_context = dict(context)
        step_context.pop("decision_reason", None)
        if step.reason:
            step_context["decision_reason"] = step.reason
        try:
            following = client.execute_action(
                resolved.action, card_index=resolved.card_index,
                target_index=resolved.target_index, option_index=resolved.option_index,
                raw_state=False, client_context=step_context,
                # Same reason as the first step: the mod validates the identity under its lease, so
                # a step whose card has already left can be refused instead of turned into a
                # different card at that position.
                card_cid=step.card_cid if step.action == "play_card" else None,
            )
        except Sts2ApiError as exc:
            # The mod answered, so the step was refused rather than lost. Only a transport loss is an
            # unknown outcome; a 4xx means nothing was executed and must not be called ambiguous.
            if exc.status_code is not None and 400 <= exc.status_code < 500:
                stop = "rejected"
                results.append({
                    "action": resolved.action, "status": "failed", "stable": False,
                    "error": {"code": exc.code, "message": exc.message, "retryable": False},
                })
            else:
                stop = "outcome_unknown"
            break
        except Exception:
            # No replay: after dispatch the outcome may be unknown. Preserve accepted earlier steps.
            stop = "outcome_unknown"
            break
        results.append(following)
        stop = _stop(following)
    output = dict(result)
    output["plan"] = {"steps": results, "executed": len(results),
                      "stop_reason": stop or "completed", "warnings": plan.warnings}
    if "state" in results[-1]:
        output["final_state"] = results[-1]["state"]
    return output
