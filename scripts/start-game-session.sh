#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib-sts2.sh
source "$script_dir/lib-sts2.sh"

exe_path="${STS2_EXE_PATH:-}"
game_root="${STS2_GAME_ROOT:-}"
app_manifest_path="${STS2_APP_MANIFEST:-}"
app_id="${STS2_APP_ID:-}"
attempts=40
delay_seconds=2
enable_debug_actions=0
api_port="${STS2_API_PORT:-8080}"
keep_existing_processes=0

usage() {
  cat <<'EOF'
Usage: start-game-session.sh [--exe-path PATH] [--game-root PATH] [--app-manifest PATH] [--app-id ID] [--attempts N] [--delay-seconds N] [--enable-debug-actions] [--api-port PORT] [--keep-existing-processes]
EOF
}

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
    --enable-debug-actions)
      enable_debug_actions=1
      shift
      ;;
    --api-port)
      api_port="${2:-}"
      shift 2
      ;;
    --keep-existing-processes)
      keep_existing_processes=1
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

sts2_require_command python3

if [[ -z "$game_root" ]]; then
  game_root="$(sts2_detect_game_root || true)"
fi

if [[ -z "$exe_path" ]]; then
  if [[ -z "$game_root" ]]; then
    echo "Could not determine the game root. Pass --game-root or --exe-path." >&2
    exit 1
  fi

  exe_path="$(sts2_detect_game_executable "$game_root" || true)"
fi

if [[ -z "$exe_path" || ! -x "$exe_path" ]]; then
  echo "Game executable not found or not executable: $exe_path" >&2
  exit 1
fi

if [[ -z "$app_manifest_path" ]]; then
  app_manifest_path="$(sts2_default_app_manifest)"
fi

resolved_app_id="$(sts2_resolve_app_id "$app_id" "$app_manifest_path")"
sts2_ensure_steam_app_id_file "$exe_path" "$resolved_app_id"

base_url="http://127.0.0.1:$api_port"

if [[ "$keep_existing_processes" != "1" ]]; then
  sts2_stop_running_games
  sts2_wait_for_port_release "$api_port" 10 1 || true
fi

launch_dir="$(cd -- "$(dirname -- "$exe_path")" && pwd)"

(
  cd -- "$launch_dir"
  export STS2_API_PORT="$api_port"
  if [[ "$enable_debug_actions" == "1" ]]; then
    export STS2_ENABLE_DEBUG_ACTIONS=1
  else
    unset STS2_ENABLE_DEBUG_ACTIONS
  fi
  exec "$exe_path"
) >/dev/null 2>&1 &
pid=$!

sts2_wait_for_health "$base_url" "$attempts" "$delay_seconds" "$pid"

python3 - "$pid" "$enable_debug_actions" "$api_port" "$base_url" <<'PY'
import json
import sys

print(
    json.dumps(
        {
            "pid": int(sys.argv[1]),
            "debug_actions_enabled": bool(int(sys.argv[2])),
            "api_port": int(sys.argv[3]),
            "base_url": sys.argv[4],
            "health": "ready",
        },
        ensure_ascii=False,
    )
)
PY
