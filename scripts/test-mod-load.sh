#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib-sts2.sh
source "$script_dir/lib-sts2.sh"

repo_root="$(sts2_resolve_repo_root "${REPO_ROOT:-}")"
exe_path="${STS2_EXE_PATH:-}"
game_root="${STS2_GAME_ROOT:-}"
app_manifest_path="${STS2_APP_MANIFEST:-}"
app_id="${STS2_APP_ID:-}"
attempts=15
delay_seconds=2
deep_check=0
api_port="${STS2_API_PORT:-8080}"
skip_steam_app_id_file="${STS2_SKIP_STEAM_APP_ID_FILE:-0}"
pid=""

usage() {
  cat <<'EOF'
Usage: test-mod-load.sh [--exe-path PATH] [--game-root PATH] [--app-manifest PATH] [--app-id ID] [--attempts N] [--delay-seconds N] [--deep-check] [--api-port PORT]
                         [--skip-steam-app-id-file]
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
    --app-manifest)
      app_manifest_path="${2:-}"
      shift 2
      ;;
    --app-id)
      app_id="${2:-}"
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
    --deep-check)
      deep_check=1
      shift
      ;;
    --api-port)
      api_port="${2:-}"
      shift 2
      ;;
    --skip-steam-app-id-file)
      skip_steam_app_id_file=1
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
if [[ -n "$app_manifest_path" ]]; then
  start_args+=(--app-manifest "$app_manifest_path")
fi
if [[ -n "$app_id" ]]; then
  start_args+=(--app-id "$app_id")
fi
if [[ "$skip_steam_app_id_file" == "1" ]]; then
  start_args+=(--skip-steam-app-id-file)
fi

session_json="$("${start_args[@]}")"
pid="$(printf '%s' "$session_json" | sts2_json_value "pid")"

base_url="http://127.0.0.1:$api_port"
if [[ "$deep_check" == "1" ]]; then
  sts2_run_validation "$repo_root" mod-load --base-url "$base_url" --timeout-sec 10 --deep-check
else
  sts2_run_validation "$repo_root" mod-load --base-url "$base_url" --timeout-sec 10
fi
