#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib-sts2.sh
source "$script_dir/lib-sts2.sh"

repo_root="$(sts2_resolve_repo_root "${REPO_ROOT:-}")"
exe_path="${STS2_EXE_PATH:-}"
game_root="${STS2_GAME_ROOT:-}"
command="help"
attempts=40
delay_seconds=2
enable_debug_actions=0
api_port=8080
pid=""

usage() {
  cat <<'EOF'
Usage: test-debug-console-gating.sh [--exe-path PATH] [--game-root PATH] [--command CMD] [--attempts N] [--delay-seconds N] [--enable-debug-actions] [--api-port PORT]
EOF
}

cleanup() {
  if [[ -n "$pid" ]]; then
    sts2_stop_pid "$pid"
  fi
}

trap cleanup EXIT

while [[ $# -gt 0 ]]; do
  case "$1" in
    --exe-path)
      exe_path="${2:-}"
      shift 2
      ;;
    --game-root)
      game_root="${2:-}"
      shift 2
      ;;
    --command)
      command="${2:-}"
      shift 2
      ;;
    --attempts)
      attempts="${2:-}"
      shift 2
      ;;
    --delay-seconds)
      delay_seconds="${2:-}"
      shift 2
      ;;
    --enable-debug-actions)
      enable_debug_actions=1
      shift
      ;;
    --api-port)
      api_port="${2:-}"
      shift 2
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

start_args=(
  "$script_dir/start-game-session.sh"
  --attempts "$attempts"
  --delay-seconds "$delay_seconds"
  --api-port "$api_port"
)

if [[ -n "$exe_path" ]]; then
  start_args+=(--exe-path "$exe_path")
fi
if [[ -n "$game_root" ]]; then
  start_args+=(--game-root "$game_root")
fi
if [[ "$enable_debug_actions" == "1" ]]; then
  start_args+=(--enable-debug-actions)
fi

session_json="$("${start_args[@]}")"
pid="$(printf '%s' "$session_json" | sts2_json_value "pid")"

base_url="http://127.0.0.1:$api_port"
validation_args=(
  debug-console-gating
  --base-url "$base_url"
  --timeout-sec 10
  --command "$command"
)

if [[ "$enable_debug_actions" == "1" ]]; then
  validation_args+=(--enable-debug-actions)
fi

sts2_run_validation "$repo_root" "${validation_args[@]}"
