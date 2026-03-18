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
skip_steam_app_id_file="${STS2_SKIP_STEAM_APP_ID_FILE:-0}"
port_release_attempts="${STS2_PORT_RELEASE_ATTEMPTS:-10}"
port_release_delay_seconds="${STS2_PORT_RELEASE_DELAY_SECONDS:-1}"
pid=""
start_succeeded=0
steam_app_id_file=""
steam_app_id_backup=""
steam_app_id_original_exists=0
steam_app_id_restore_needed=0

restore_steam_app_id_file() {
  if [[ "$steam_app_id_restore_needed" != "1" || -z "$steam_app_id_file" ]]; then
    return 0
  fi

  if [[ "$steam_app_id_original_exists" == "1" && -n "$steam_app_id_backup" && -f "$steam_app_id_backup" ]]; then
    cp -f "$steam_app_id_backup" "$steam_app_id_file"
  else
    rm -f "$steam_app_id_file"
  fi

  if [[ -n "$steam_app_id_backup" ]]; then
    rm -f "$steam_app_id_backup"
    steam_app_id_backup=""
  fi

  steam_app_id_restore_needed=0
}

cleanup() {
  restore_steam_app_id_file
  if [[ "$start_succeeded" != "1" && -n "$pid" ]]; then
    sts2_stop_pid "$pid"
    sts2_wait_for_port_release "$api_port" "$port_release_attempts" "$port_release_delay_seconds" || true
  fi
}

trap cleanup EXIT

usage() {
  cat <<'EOF'
Usage: start-game-session.sh [--exe-path PATH] [--game-root PATH] [--app-manifest PATH] [--app-id ID] [--attempts N] [--delay-seconds N] [--enable-debug-actions] [--api-port PORT] [--keep-existing-processes]
                            [--skip-steam-app-id-file]
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

sts2_require_command python3
sts2_require_command lsof

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

if [[ "$skip_steam_app_id_file" != "1" ]]; then
  if [[ -z "$app_manifest_path" ]]; then
    app_manifest_path="$(sts2_default_app_manifest)"
  fi

  resolved_app_id="$(sts2_resolve_app_id "$app_id" "$app_manifest_path")"
  steam_app_id_file="$(sts2_steam_app_id_file_path "$exe_path")"

  if [[ -f "$steam_app_id_file" ]]; then
    current_app_id="$(tr -d '[:space:]' < "$steam_app_id_file")"
    if [[ "$current_app_id" != "$resolved_app_id" ]]; then
      steam_app_id_backup="$(mktemp)"
      cp -f "$steam_app_id_file" "$steam_app_id_backup"
      steam_app_id_original_exists=1
      steam_app_id_restore_needed=1
      sts2_ensure_steam_app_id_file "$exe_path" "$resolved_app_id"
    fi
  else
    steam_app_id_restore_needed=1
    sts2_ensure_steam_app_id_file "$exe_path" "$resolved_app_id"
  fi
fi

base_url="http://127.0.0.1:$api_port"

if [[ "$keep_existing_processes" != "1" ]]; then
  sts2_stop_running_games "$exe_path"
  if ! sts2_wait_for_port_release "$api_port" "$port_release_attempts" "$port_release_delay_seconds"; then
    echo "Timed out waiting for port $api_port to be released before starting a new game session." >&2
    exit 1
  fi
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
start_succeeded=1

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
