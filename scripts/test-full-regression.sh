#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib-sts2.sh
source "$script_dir/lib-sts2.sh"

repo_root="$(sts2_resolve_repo_root "${REPO_ROOT:-}")"
keep_game_running=0
configuration="${CONFIGURATION:-Debug}"
exe_path="${STS2_EXE_PATH:-}"
game_root="${STS2_GAME_ROOT:-}"
app_manifest_path="${STS2_APP_MANIFEST:-}"
app_id="${STS2_APP_ID:-}"
skip_steam_app_id_file="${STS2_SKIP_STEAM_APP_ID_FILE:-0}"
api_port="${STS2_API_PORT:-8080}"
base_url="http://127.0.0.1:$api_port"
current_pid=""
failed=0
failure_message=""
results_file="$(mktemp)"
build_game_root=""
runtime_start_args=()
build_mod_args=()

usage() {
  cat <<'EOF'
Usage: test-full-regression.sh [--repo-root PATH] [--configuration Debug|Release] [--exe-path PATH] [--game-root PATH]
                               [--app-manifest PATH] [--app-id ID] [--skip-steam-app-id-file] [--keep-game-running]
EOF
}

cleanup() {
  if [[ "$keep_game_running" != "1" ]]; then
    if [[ -n "$current_pid" ]]; then
      sts2_stop_pid "$current_pid"
    fi
    sts2_stop_running_games "$exe_path"
  fi
  rm -f "$results_file"
}

trap cleanup EXIT

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-root)
      repo_root="$(sts2_resolve_repo_root "${2:-}")"
      shift 2
      ;;
    --configuration)
      configuration="${2:-}"
      shift 2
      ;;
    --exe-path)
      exe_path="${2:-}"
      shift 2
      ;;
    --game-root)
      game_root="${2:-}"
      shift 2
      ;;
    --app-manifest)
      app_manifest_path="${2:-}"
      shift 2
      ;;
    --app-id)
      app_id="${2:-}"
      shift 2
      ;;
    --skip-steam-app-id-file)
      skip_steam_app_id_file=1
      shift
      ;;
    --keep-game-running)
      keep_game_running=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$game_root" && -n "$exe_path" ]]; then
  build_game_root="$(sts2_infer_game_root_from_executable "$exe_path" || true)"
else
  build_game_root="$game_root"
fi

if [[ -n "$exe_path" ]]; then
  runtime_start_args+=(--exe-path "$exe_path")
fi
if [[ -n "$game_root" ]]; then
  runtime_start_args+=(--game-root "$game_root")
fi
if [[ -n "$app_manifest_path" ]]; then
  runtime_start_args+=(--app-manifest "$app_manifest_path")
fi
if [[ -n "$app_id" ]]; then
  runtime_start_args+=(--app-id "$app_id")
fi
if [[ "$skip_steam_app_id_file" == "1" ]]; then
  runtime_start_args+=(--skip-steam-app-id-file)
fi

build_mod_args=("$script_dir/build-mod.sh" --repo-root "$repo_root" --configuration "$configuration")
if [[ -n "$build_game_root" ]]; then
  build_mod_args+=(--game-root "$build_game_root")
fi

export REPO_ROOT="$repo_root"
if [[ -n "$exe_path" ]]; then
  export STS2_EXE_PATH="$exe_path"
else
  unset STS2_EXE_PATH || true
fi
if [[ -n "$game_root" ]]; then
  export STS2_GAME_ROOT="$game_root"
else
  unset STS2_GAME_ROOT || true
fi
if [[ -n "$app_manifest_path" ]]; then
  export STS2_APP_MANIFEST="$app_manifest_path"
else
  unset STS2_APP_MANIFEST || true
fi
if [[ -n "$app_id" ]]; then
  export STS2_APP_ID="$app_id"
else
  unset STS2_APP_ID || true
fi
if [[ "$skip_steam_app_id_file" == "1" ]]; then
  export STS2_SKIP_STEAM_APP_ID_FILE=1
else
  unset STS2_SKIP_STEAM_APP_ID_FILE || true
fi

record_result() {
  local name="$1"
  local status="$2"
  local duration="$3"
  local error_message="${4:-}"
  python3 - "$results_file" "$name" "$status" "$duration" "$error_message" <<'PY'
import json
import sys

record = {
    "name": sys.argv[2],
    "status": sys.argv[3],
    "duration_seconds": float(sys.argv[4]),
}
if sys.argv[5]:
    record["error"] = sys.argv[5]

with open(sys.argv[1], "a", encoding="utf-8") as handle:
    handle.write(json.dumps(record, ensure_ascii=False) + "\n")
PY
}

run_step() {
  local name="$1"
  shift
  local started
  local duration
  local error_message=""

  started="$(python3 - <<'PY'
import time
print(time.time())
PY
)"
  echo "==> $name"

  if "$@"; then
    duration="$(python3 - "$started" <<'PY'
import sys
import time
print(round(time.time() - float(sys.argv[1]), 1))
PY
)"
    record_result "$name" "passed" "$duration"
    return 0
  fi

  error_message="command failed"
  duration="$(python3 - "$started" <<'PY'
import sys
import time
print(round(time.time() - float(sys.argv[1]), 1))
PY
)"
  record_result "$name" "failed" "$duration" "$error_message"
  return 1
}

start_debug_session() {
  local session_json
  session_json="$("$script_dir/start-game-session.sh" --enable-debug-actions --api-port "$api_port" "${runtime_start_args[@]}")"
  current_pid="$(printf '%s' "$session_json" | sts2_json_value "pid")"
}

restart_debug_session() {
  stop_current_game
  start_debug_session
}

stop_current_game() {
  if [[ -n "$current_pid" ]]; then
    sts2_stop_pid "$current_pid"
    current_pid=""
  else
    sts2_stop_running_games "$exe_path"
  fi
}

ensure_active_run_main_menu() {
  if sts2_run_validation "$repo_root" assert-active-run-main-menu --base-url "$base_url" --timeout-sec 5 --poll-attempts 160 --poll-delay-ms 250 >/dev/null; then
    return 0
  fi

  if sts2_run_validation "$repo_root" bootstrap-active-run --base-url "$base_url" --timeout-sec 15 --poll-attempts 160 --poll-delay-ms 250 >/dev/null; then
    stop_current_game
    start_debug_session
    sts2_run_validation "$repo_root" assert-active-run-main-menu --base-url "$base_url" --timeout-sec 5 --poll-attempts 160 --poll-delay-ms 250 >/dev/null
    return 0
  fi

  stop_current_game
  start_debug_session
  sts2_run_validation "$repo_root" assert-active-run-main-menu --base-url "$base_url" --timeout-sec 5 --poll-attempts 160 --poll-delay-ms 250 >/dev/null
}

run_flow_script() {
  local script_name="$1"
  shift
  "$script_dir/$script_name" --base-url "$base_url" "$@"
}

finish_summary() {
  python3 - "$results_file" "$repo_root" "$keep_game_running" "$failed" "$failure_message" <<'PY'
import json
import pathlib
import sys

results_path = pathlib.Path(sys.argv[1])
steps = [json.loads(line) for line in results_path.read_text(encoding="utf-8").splitlines() if line]
payload = {
    "project_root": sys.argv[2],
    "keep_game_running": bool(int(sys.argv[3])),
    "total_steps": len(steps),
    "failed": bool(int(sys.argv[4])),
    "failure_message": sys.argv[5] or None,
    "steps": steps,
}
print(json.dumps(payload, ensure_ascii=False, indent=2))
PY
}

if ! run_step "stop running game before install" sts2_stop_running_games "$exe_path"; then
  failed=1
  failure_message="failed to stop running game before install"
elif ! run_step "build mod" "${build_mod_args[@]}"; then
  failed=1
  failure_message="build mod failed"
elif ! run_step "mod load deep check" "$script_dir/test-mod-load.sh" --deep-check --api-port "$api_port" "${runtime_start_args[@]}"; then
  failed=1
  failure_message="mod load deep check failed"
elif ! run_step "debug console gating (disabled)" "$script_dir/test-debug-console-gating.sh" --api-port "$api_port" "${runtime_start_args[@]}"; then
  failed=1
  failure_message="debug console gating (disabled) failed"
elif ! run_step "debug console gating (enabled)" "$script_dir/test-debug-console-gating.sh" --enable-debug-actions --api-port "$api_port" "${runtime_start_args[@]}"; then
  failed=1
  failure_message="debug console gating (enabled) failed"
elif ! run_step "mcp tool profile" "$script_dir/test-mcp-tool-profile.sh"; then
  failed=1
  failure_message="mcp tool profile failed"
elif ! run_step "start debug session for main-menu lifecycle" start_debug_session; then
  failed=1
  failure_message="start debug session for main-menu lifecycle failed"
elif ! run_step "ensure active-run MAIN_MENU for main-menu lifecycle" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for main-menu lifecycle failed"
elif ! run_step "main-menu active-run lifecycle" run_flow_script "test-main-menu-active-run.sh"; then
  failed=1
  failure_message="main-menu active-run lifecycle failed"
elif ! run_step "state invariants after main-menu lifecycle" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after main-menu lifecycle failed"
elif ! run_step "start debug session for combat hand confirm flow" restart_debug_session; then
  failed=1
  failure_message="start debug session for combat hand confirm flow failed"
elif ! run_step "ensure active-run MAIN_MENU for combat hand confirm flow" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for combat hand confirm flow failed"
elif ! run_step "combat hand confirm flow" run_flow_script "test-combat-hand-confirm-flow.sh"; then
  failed=1
  failure_message="combat hand confirm flow failed"
elif ! run_step "state invariants after combat hand confirm flow" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after combat hand confirm flow failed"
elif ! run_step "start debug session for deferred potion flow" restart_debug_session; then
  failed=1
  failure_message="start debug session for deferred potion flow failed"
elif ! run_step "ensure active-run MAIN_MENU for deferred potion flow" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for deferred potion flow failed"
elif ! run_step "deferred potion flow" run_flow_script "test-deferred-potion-flow.sh"; then
  failed=1
  failure_message="deferred potion flow failed"
elif ! run_step "state invariants after deferred potion flow" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after deferred potion flow failed"
elif ! run_step "start debug session for target index contracts" restart_debug_session; then
  failed=1
  failure_message="start debug session for target index contracts failed"
elif ! run_step "ensure active-run MAIN_MENU for target index contracts" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for target index contracts failed"
elif ! run_step "target index contracts" run_flow_script "test-target-index-contract.sh"; then
  failed=1
  failure_message="target index contracts failed"
elif ! run_step "state invariants after target index contracts" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after target index contracts failed"
elif ! run_step "start debug session for enemy intents payload" restart_debug_session; then
  failed=1
  failure_message="start debug session for enemy intents payload failed"
elif ! run_step "ensure active-run MAIN_MENU for enemy intents payload" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for enemy intents payload failed"
elif ! run_step "enemy intents payload" run_flow_script "test-enemy-intents-payload.sh"; then
  failed=1
  failure_message="enemy intents payload failed"
elif ! run_step "state invariants after enemy intents payload" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after enemy intents payload failed"
elif ! run_step "multiplayer lobby flow" "$script_dir/test-multiplayer-lobby-flow.sh" --host-api-port "$api_port" --client-api-port "$((api_port + 1))" "${runtime_start_args[@]}"; then
  failed=1
  failure_message="multiplayer lobby flow failed"
elif ! run_step "start debug session for new-run lifecycle" restart_debug_session; then
  failed=1
  failure_message="start debug session for new-run lifecycle failed"
elif ! run_step "ensure active-run MAIN_MENU for new-run lifecycle" ensure_active_run_main_menu; then
  failed=1
  failure_message="ensure active-run MAIN_MENU for new-run lifecycle failed"
elif ! run_step "new-run lifecycle" run_flow_script "test-new-run-lifecycle.sh" --timeout-sec 15 --request-retries 3 --retry-delay-ms 500 --poll-attempts 120 --poll-delay-ms 250; then
  failed=1
  failure_message="new-run lifecycle failed"
elif ! run_step "state invariants after new-run lifecycle" run_flow_script "test-state-invariants.sh"; then
  failed=1
  failure_message="state invariants after new-run lifecycle failed"
fi

finish_summary

if [[ "$failed" == "1" ]]; then
  echo "$failure_message" >&2
  exit 1
fi
