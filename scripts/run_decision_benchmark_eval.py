#!/usr/bin/env python3
"""Opt-in offline model evaluator and multi-model comparator for docs/decision-benchmark.json.

Safety & cost contract:
* Never makes network calls unless ``--allow-live-api`` is explicitly passed.
* Enforces a hard ``--max-requests`` ceiling (default 25) before every model request.
* Reads API credentials only from an environment variable (``--api-key-env``) and never writes keys
  into logs, stdout, or JSON/Markdown reports.
* Supports zero-cost ``--dry-run`` and ``--mock-responses`` modes for offline CI testing.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import sys
import time
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence
from urllib import error, request

REPO_ROOT = Path(__file__).resolve().parent.parent
STRATEGY_PATH = REPO_ROOT / "skills" / "sts2-mcp-player" / "references" / "strategy.md"
PLAYBOOKS_PATH = REPO_ROOT / "skills" / "sts2-mcp-player" / "references" / "screen-playbooks.md"


def _load_benchmark_module() -> Any:
    module_path = Path(__file__).resolve().parent / "decision_benchmark.py"
    spec = importlib.util.spec_from_file_location("sts2_decision_benchmark_mod", module_path)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = mod
    spec.loader.exec_module(mod)
    return mod


benchmark = _load_benchmark_module()

ACT_TOOL_SCHEMA: dict[str, Any] = {
    "type": "function",
    "function": {
        "name": "act",
        "description": "Choose the single best legal action for the current STS2 state snapshot.",
        "parameters": {
            "type": "object",
            "properties": {
                "action": {"type": "string", "description": "Action name from available_actions, or wait_until_actionable when gated."},
                "card_index": {"type": "integer", "minimum": 0, "description": "Hand card index when action is play_card."},
                "option_index": {"type": "integer", "minimum": 0, "description": "Option index for map/event/rest/shop/reward actions."},
                "target_index": {"type": "integer", "minimum": 0, "description": "Enemy/player target index when required."},
                "reason": {"type": "string", "description": "One concise sentence explaining the decision."},
            },
            "required": ["action"],
        },
    },
}

SCREEN_STRATEGY_HEADINGS: dict[str, tuple[str, ...]] = {
    "COMBAT": ("Combat: what to prioritise", "Potions: when to drink"),
    "MAP": ("Route: which node to enter",),
    "REST": ("Rest site: heal or upgrade",),
    "REWARD": ("Card rewards: take or skip",),
    "SHOP": ("Shop: what to buy",),
    "EVENT": ("Event options: how to choose",),
}

SCREEN_PLAYBOOK_HEADINGS: dict[str, tuple[str, ...]] = {
    "COMBAT": ("COMBAT", "Potion Targeting"),
    "MAP": ("MAP",),
    "REST": ("REST",),
    "REWARD": ("REWARD",),
    "SHOP": ("SHOP",),
    "EVENT": ("EVENT",),
}


def _extract_sections(markdown: str) -> dict[str, str]:
    sections: dict[str, str] = {}
    current: str | None = None
    lines: list[str] = []
    for raw_line in markdown.splitlines():
        if raw_line.startswith("## ") and not raw_line.startswith("### "):
            if current is not None:
                sections[current] = "\n".join(lines).strip()
            current = raw_line[3:].strip()
            lines = []
        elif current is not None:
            lines.append(raw_line)
    if current is not None:
        sections[current] = "\n".join(lines).strip()
    return sections


def _load_guidance_tables() -> tuple[dict[str, str], dict[str, str]]:
    strategy = _extract_sections(STRATEGY_PATH.read_text(encoding="utf-8")) if STRATEGY_PATH.exists() else {}
    playbooks = _extract_sections(PLAYBOOKS_PATH.read_text(encoding="utf-8")) if PLAYBOOKS_PATH.exists() else {}
    return strategy, playbooks


def build_case_messages(
    case: Any,
    strategy_sections: Mapping[str, str],
    playbook_sections: Mapping[str, str],
) -> list[dict[str, str]]:
    """Build deterministic chat-completion messages for one benchmark snapshot."""
    screen = str(case.state.get("screen", "")).upper()
    parts = [
        "You are evaluating a single Slay the Spire 2 decision snapshot.",
        "Call the `act` tool (or reply with a single JSON object) with the best legal action.",
        "Use `card_index` for `play_card`, `option_index` for option-based actions, and `target_index` when targeting an enemy.",
        "If `combat.action_readiness.can_use_combat_actions` is false and no safe action applies, use `wait_until_actionable`.",
    ]
    pb_chunks = [f"## {h}\n{playbook_sections[h]}" for h in SCREEN_PLAYBOOK_HEADINGS.get(screen, ()) if h in playbook_sections]
    if pb_chunks:
        parts.append("Playbook:\n" + "\n\n".join(pb_chunks))
    st_chunks = [f"## {h}\n{strategy_sections[h]}" for h in SCREEN_STRATEGY_HEADINGS.get(screen, ()) if h in strategy_sections]
    if st_chunks:
        parts.append("Strategy:\n" + "\n\n".join(st_chunks))

    state_json = json.dumps(case.state, ensure_ascii=False, sort_keys=True)
    return [
        {"role": "system", "content": "\n\n".join(parts)},
        {"role": "user", "content": f"Case: {case.id} ({case.title})\nState:\n{state_json}"},
    ]


def _parse_int_opt(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int) and value >= 0:
        return value
    if isinstance(value, str) and value.strip().isdigit():
        return int(value.strip())
    return None


def parse_model_answer(case_id: str, completion: Mapping[str, Any]) -> dict[str, Any] | None:
    """Extract a benchmark answer dict from an OpenAI chat-completions payload."""
    choices = completion.get("choices")
    if not isinstance(choices, list) or not choices:
        return None
    message = choices[0].get("message") if isinstance(choices[0], Mapping) else None
    if not isinstance(message, Mapping):
        return None

    raw_args: Mapping[str, Any] | None = None
    tool_calls = message.get("tool_calls")
    if isinstance(tool_calls, list):
        for call in tool_calls:
            if not isinstance(call, Mapping):
                continue
            fn = call.get("function")
            if isinstance(fn, Mapping) and isinstance(fn.get("arguments"), str):
                try:
                    parsed = json.loads(fn["arguments"])
                    if isinstance(parsed, Mapping):
                        raw_args = parsed
                        break
                except json.JSONDecodeError:
                    continue

    if raw_args is None and isinstance(message.get("content"), str):
        content = message["content"].strip()
        fence = re.search(r"```(?:json)?\s*(\{.*?\})\s*```", content, re.S)
        candidate = fence.group(1) if fence else content
        start = candidate.find("{")
        end = candidate.rfind("}")
        if start >= 0 and end > start:
            try:
                parsed = json.loads(candidate[start : end + 1])
                if isinstance(parsed, Mapping):
                    raw_args = parsed
            except json.JSONDecodeError:
                raw_args = None

    if not isinstance(raw_args, Mapping):
        return None
    action = raw_args.get("action")
    if not isinstance(action, str) or not action.strip():
        return None
    action = action.strip()

    answer: dict[str, Any] = {"case_id": case_id, "action": action}
    # Benchmark normalizes play_card's card_index into option_index.
    if action == "play_card":
        opt = _parse_int_opt(raw_args.get("card_index"))
        if opt is None:
            opt = _parse_int_opt(raw_args.get("option_index"))
    else:
        opt = _parse_int_opt(raw_args.get("option_index"))
    if opt is not None:
        answer["option_index"] = opt

    target = _parse_int_opt(raw_args.get("target_index"))
    if target is not None:
        answer["target_index"] = target

    reason = raw_args.get("reason")
    if isinstance(reason, str) and reason.strip():
        answer["reason"] = reason.strip()[:300]
    return answer


def _http_chat_completion(
    *,
    base_url: str,
    api_key: str,
    model: str,
    messages: list[dict[str, str]],
    timeout_seconds: float,
    temperature: float | None,
) -> Mapping[str, Any]:
    url = base_url.rstrip("/") + "/chat/completions"
    payload: dict[str, Any] = {
        "model": model,
        "messages": messages,
        "tools": [ACT_TOOL_SCHEMA],
        "tool_choice": "auto",
    }
    if temperature is not None:
        payload["temperature"] = temperature
    body = json.dumps(payload).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json",
    }
    if api_key:
        headers["Authorization"] = f"Bearer {api_key}"
    req = request.Request(url=url, data=body, headers=headers, method="POST")
    try:
        with request.urlopen(req, timeout=timeout_seconds) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")[:300]
        raise RuntimeError(f"HTTP {exc.code} from {url}: {detail}") from exc


def evaluate_suite(
    suite: Any,
    *,
    model: str,
    base_url: str | None = None,
    api_key: str = "",
    max_requests: int = 25,
    timeout_seconds: float = 60.0,
    temperature: float | None = 0.0,
    dry_run: bool = False,
    allow_live_api: bool = False,
    requester: Callable[[str, list[dict[str, str]]], Mapping[str, Any]] | None = None,
) -> dict[str, Any]:
    """Run the benchmark suite with strict budget enforcement and score via decision_benchmark."""
    if max_requests < 0:
        raise ValueError("max_requests must be >= 0")
    if not dry_run and requester is None:
        if not allow_live_api:
            raise ValueError("Refusing to call a live model API without --allow-live-api (use --dry-run for zero-cost checks).")
        if not base_url:
            raise ValueError("--base-url is required for live model evaluation.")

    strategy_sections, playbook_sections = _load_guidance_tables()
    answers: list[dict[str, Any]] = []
    telemetry: list[dict[str, Any]] = []
    requests_sent = 0
    prompt_tokens = 0
    completion_tokens = 0
    budget_stopped = False

    for case in suite.cases:
        messages = build_case_messages(case, strategy_sections, playbook_sections)
        prompt_chars = sum(len(m["content"]) for m in messages)
        if dry_run:
            telemetry.append(
                {
                    "case_id": case.id,
                    "status": "dry_run",
                    "prompt_chars": prompt_chars,
                    "latency_ms": 0,
                }
            )
            continue

        if requests_sent >= max_requests:
            budget_stopped = True
            telemetry.append({"case_id": case.id, "status": "budget_capped", "prompt_chars": prompt_chars})
            continue

        requests_sent += 1
        t0 = time.monotonic()
        try:
            if requester is not None:
                raw_resp = requester(case.id, messages)
            else:
                raw_resp = _http_chat_completion(
                    base_url=base_url or "",
                    api_key=api_key,
                    model=model,
                    messages=messages,
                    timeout_seconds=timeout_seconds,
                    temperature=temperature,
                )
            latency_ms = int((time.monotonic() - t0) * 1000)
            usage = raw_resp.get("usage") if isinstance(raw_resp, Mapping) else None
            p_tok = int(usage.get("prompt_tokens") or 0) if isinstance(usage, Mapping) else 0
            c_tok = int(usage.get("completion_tokens") or 0) if isinstance(usage, Mapping) else 0
            prompt_tokens += p_tok
            completion_tokens += c_tok
            parsed = parse_model_answer(case.id, raw_resp)
            if parsed is not None:
                answers.append(parsed)
                telemetry.append(
                    {
                        "case_id": case.id,
                        "status": "answered",
                        "latency_ms": latency_ms,
                        "prompt_tokens": p_tok,
                        "completion_tokens": c_tok,
                    }
                )
            else:
                telemetry.append(
                    {
                        "case_id": case.id,
                        "status": "unparseable",
                        "latency_ms": latency_ms,
                        "prompt_tokens": p_tok,
                        "completion_tokens": c_tok,
                    }
                )
        except Exception as exc:
            latency_ms = int((time.monotonic() - t0) * 1000)
            # Redact the API key if an exception string echoed it.
            err_msg = str(exc)
            if api_key:
                err_msg = err_msg.replace(api_key, "[REDACTED]")
            telemetry.append(
                {
                    "case_id": case.id,
                    "status": "error",
                    "error": err_msg[:240],
                    "latency_ms": latency_ms,
                }
            )

    scored = benchmark.score_suite(suite, {"answers": answers})
    latencies = [row["latency_ms"] for row in telemetry if row.get("status") == "answered"]
    avg_latency_ms = round(sum(latencies) / len(latencies), 1) if latencies else 0.0
    return {
        "model": model,
        "dry_run": dry_run,
        "max_requests": max_requests,
        "requests_sent": requests_sent,
        "budget_stopped": budget_stopped,
        "prompt_tokens": prompt_tokens,
        "completion_tokens": completion_tokens,
        "avg_latency_ms": avg_latency_ms,
        "score": scored,
        "telemetry": telemetry,
    }


def compare_reports(reports: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
    """Compare two or more evaluation reports side by side."""
    summary_rows: list[dict[str, Any]] = []
    per_case: dict[str, dict[str, float]] = {}

    for report in reports:
        model = str(report.get("model") or "unknown")
        score = report.get("score") or {}
        summary_rows.append(
            {
                "model": model,
                "score_percent": score.get("score_percent", 0.0),
                "earned_points": score.get("earned_points", 0.0),
                "available_points": score.get("available_points", 0.0),
                "answered_cases": score.get("answered_cases", 0),
                "total_cases": score.get("total_cases", 0),
                "requests_sent": report.get("requests_sent", 0),
                "prompt_tokens": report.get("prompt_tokens", 0),
                "completion_tokens": report.get("completion_tokens", 0),
                "avg_latency_ms": report.get("avg_latency_ms", 0.0),
            }
        )
        for case in score.get("cases") or []:
            cid = case["case_id"]
            per_case.setdefault(cid, {})[model] = float(case.get("earned_points", 0.0))

    disagreements: list[dict[str, Any]] = []
    for cid, scores_by_model in sorted(per_case.items()):
        values = list(scores_by_model.values())
        if len(set(values)) > 1:
            disagreements.append({"case_id": cid, "earned_by_model": scores_by_model})

    summary_rows.sort(key=lambda r: (-float(r["score_percent"]), float(r["avg_latency_ms"]), r["model"]))
    return {"models": summary_rows, "disagreements": disagreements}


def render_comparison_markdown(comparison: Mapping[str, Any]) -> str:
    lines = [
        "# STS2 Offline Decision Benchmark Comparison",
        "",
        "| Model | Score | Answered | Requests | Tokens (in/out) | Avg Latency |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for row in comparison["models"]:
        lines.append(
            f"| {row['model']} | {row['score_percent']:.2f}% ({row['earned_points']}/{row['available_points']}) "
            f"| {row['answered_cases']}/{row['total_cases']} | {row['requests_sent']} "
            f"| {row['prompt_tokens']}/{row['completion_tokens']} | {row['avg_latency_ms']:.1f} ms |"
        )
    if comparison["disagreements"]:
        lines.extend(["", "## Case Disagreements", ""])
        for diff in comparison["disagreements"]:
            pairs = ", ".join(f"{m}={pts}" for m, pts in sorted(diff["earned_by_model"].items()))
            lines.append(f"- `{diff['case_id']}`: {pairs}")
    return "\n".join(lines) + "\n"


def _parse_args(argv: Sequence[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Opt-in OpenAI-compatible evaluator and comparator for STS2 decision benchmarks.")
    parser.add_argument("--suite", type=Path, default=benchmark.DEFAULT_SUITE)
    parser.add_argument("--model", help="Model name to evaluate")
    parser.add_argument("--base-url", help="OpenAI-compatible base URL (e.g. https://api.openai.com/v1)")
    parser.add_argument("--api-key-env", default="OPENAI_API_KEY", help="Env var holding the API key (never written to reports)")
    parser.add_argument("--max-requests", type=int, default=25, help="Hard request cap before stopping")
    parser.add_argument("--timeout", type=float, default=60.0)
    parser.add_argument("--temperature", type=float, default=0.0)
    parser.add_argument("--dry-run", action="store_true", help="Validate prompts and count requests without calling any API")
    parser.add_argument("--allow-live-api", action="store_true", help="Explicit opt-in required to send real HTTP API requests")
    parser.add_argument("--mock-responses", type=Path, help="Offline JSON map {case_id: chat_completion_payload} for deterministic local runs")
    parser.add_argument("--compare", nargs="+", type=Path, help="Compare two or more saved evaluation report JSON files")
    parser.add_argument("--markdown", action="store_true", help="With --compare, print Markdown summary instead of JSON")
    parser.add_argument("--output", type=Path, help="Write report JSON/Markdown to this path (e.g. build/decision-benchmark/...)")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    if args.compare:
        loaded = [json.loads(path.read_text(encoding="utf-8")) for path in args.compare]
        comparison = compare_reports(loaded)
        rendered = render_comparison_markdown(comparison) if args.markdown else json.dumps(comparison, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(rendered, encoding="utf-8")
        else:
            sys.stdout.write(rendered)
        return 0

    suite = benchmark.load_suite(args.suite)
    model_name = args.model or ("dry-run" if args.dry_run else "")
    if not model_name:
        print("Error: --model is required (or pass --dry-run / --compare).", file=sys.stderr)
        return 2

    requester = None
    if args.mock_responses is not None:
        mock_map = json.loads(args.mock_responses.read_text(encoding="utf-8"))
        requester = lambda case_id, _messages: mock_map[case_id]

    api_key = os.environ.get(args.api_key_env, "")
    try:
        report = evaluate_suite(
            suite,
            model=model_name,
            base_url=args.base_url,
            api_key=api_key,
            max_requests=args.max_requests,
            timeout_seconds=args.timeout,
            temperature=args.temperature,
            dry_run=args.dry_run,
            allow_live_api=args.allow_live_api,
            requester=requester,
        )
    except ValueError as exc:
        print(f"decision benchmark eval refused: {exc}", file=sys.stderr)
        return 2

    rendered = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
        print(f"[decision-benchmark-eval] wrote {args.output} ({report['score']['score_percent']}% over {report['score']['answered_cases']}/{report['score']['total_cases']} cases)")
    else:
        sys.stdout.write(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
