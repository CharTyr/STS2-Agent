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
extra_args=()

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

# Git Bash and MSYS report Windows paths, and the game reads %APPDATA%\SlayTheSpire2 there.
# Seeding the XDG location on that platform writes a settings file the game never opens, so the
# Windows root has to be translated instead of assumed.
sts2_windows_path_to_posix() {
  local path="$1" slashed drive

  if command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$path"
    return 0
  fi

  slashed="$(printf '%s' "$path" | tr '\\' '/')"
  case "$slashed" in
    [A-Za-z]:/*)
      drive="$(printf '%s' "${slashed:0:1}" | tr '[:upper:]' '[:lower:]')"
      printf '%s\n' "/$drive${slashed:2}"
      ;;
    *)
      printf '%s\n' "$slashed"
      ;;
  esac
}

sts2_slay_user_root() {
  if [[ -n "${STS2_SLAY_USER_ROOT:-}" ]]; then
    printf '%s\n' "$STS2_SLAY_USER_ROOT"
    return 0
  fi

  case "$(uname -s)" in
    Darwin)
      printf '%s\n' "${HOME}/Library/Application Support/SlayTheSpire2"
      ;;
    MINGW*|MSYS*|CYGWIN*)
      if [[ -n "${APPDATA:-}" ]]; then
        printf '%s\n' "$(sts2_windows_path_to_posix "$APPDATA")/SlayTheSpire2"
        return 0
      fi
      printf '%s\n' "${HOME}/AppData/Roaming/SlayTheSpire2"
      ;;
    *)
      printf '%s\n' "${XDG_DATA_HOME:-${HOME}/.local/share}/SlayTheSpire2"
      ;;
  esac
}

sts2_client_id_from_args() {
  local arg
  while [[ $# -gt 0 ]]; do
    arg="$1"
    shift
    if [[ "$arg" == "--clientId" ]]; then
      if [[ $# -gt 0 ]]; then
        printf '%s\n' "$1"
      fi
      return 0
    fi
    if [[ "$arg" == --clientId=* ]]; then
      printf '%s\n' "${arg#--clientId=}"
      return 0
    fi
  done
  return 0
}

sts2_settings_agent_ready() {
  local path="$1"
  local interpreter
  [[ -f "$path" ]] || return 1
  interpreter="$(sts2_python)" || return 1
  "$interpreter" - "$path" <<'PY'
import json
import sys

try:
    with open(sys.argv[1], encoding="utf-8-sig") as handle:
        data = json.load(handle)
except Exception:
    raise SystemExit(1)
if not isinstance(data, dict):
    raise SystemExit(1)
mod_settings = data.get("mod_settings")
if not isinstance(mod_settings, dict) or mod_settings.get("mods_enabled") is not True:
    raise SystemExit(1)
mod_list = mod_settings.get("mod_list")
if not isinstance(mod_list, list):
    raise SystemExit(1)
for item in mod_list:
    if isinstance(item, dict) and item.get("id") == "STS2AIAgent" and item.get("is_enabled") is not False:
        raise SystemExit(0)
raise SystemExit(1)
PY
}

sts2_write_isolated_fallback_settings() {
  local settings_path="$1"
  cat > "$settings_path" <<'JSON'
{
  "schema_version": 8,
  "mod_settings": {
    "mods_enabled": true,
    "mod_list": [
      { "id": "STS2AIAgent", "is_enabled": true, "source": "mods_directory" }
    ]
  },
  "seen_ea_disclaimer": true,
  "skip_intro_logo": true,
  "fullscreen": false,
  "limit_fps_in_background": false,
  "vsync": "disabled",
  "window_size": { "X": 1280, "Y": 720 },
  "window_position": { "X": -1, "Y": -1 },
  "language": "en",
  "aspect_ratio": "sixteen_by_nine",
  "fps_limit": 60,
  "msaa": 2,
  "resize_windows": true,
  "target_display": 0,
  "volume_ambience": 0.5,
  "volume_bgm": 0.5,
  "volume_master": 0.5,
  "volume_sfx": 0.5,
  "controller_mapping_type": "default",
  "controller_mapping": {},
  "keyboard_mapping": {},
  "keyboard_only_mapping": {}
}
JSON
}

sts2_patch_isolated_settings_file() {
  local path="$1"
  local interpreter
  interpreter="$(sts2_python)" || return 1
  "$interpreter" - "$path" <<'PY'
import json
import sys

path = sys.argv[1]
agent = {"id": "STS2AIAgent", "is_enabled": True, "source": "mods_directory"}
try:
    with open(path, encoding="utf-8-sig") as handle:
        data = json.load(handle)
except Exception:
    raise SystemExit(2)
if not isinstance(data, dict):
    raise SystemExit(2)

mod_settings = data.get("mod_settings")
if not isinstance(mod_settings, dict):
    mod_settings = {}
    data["mod_settings"] = mod_settings

mod_settings["mods_enabled"] = True
mod_list = mod_settings.get("mod_list")
if not isinstance(mod_list, list):
    mod_list = []
    mod_settings["mod_list"] = mod_list

found = False
for item in mod_list:
    if isinstance(item, dict) and item.get("id") == "STS2AIAgent":
        item["is_enabled"] = True
        found = True
        break
if not found:
    mod_list.append(agent)

with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(data, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY
}

sts2_initialize_isolated_client_settings() {
  local client_id="${1:-}"
  local user_root="${2:-}"
  local dir settings_path steam_root template candidate

  if [[ -z "$client_id" ]]; then
    return 0
  fi

  if [[ -z "$user_root" ]]; then
    user_root="$(sts2_slay_user_root)"
  fi

  # A root that does not exist means the seeder is about to write a settings file the game will
  # never open. Say so instead of reporting a seed that changed nothing.
  if [[ ! -d "$user_root" ]]; then
    echo "[start-game-session] warning: $user_root does not exist, so the game will not read $user_root/default/$client_id/settings.save. Set STS2_SLAY_USER_ROOT to the directory that holds SlayTheSpire2's save data." >&2
  fi

  dir="$user_root/default/$client_id"
  settings_path="$dir/settings.save"
  mkdir -p "$dir"

  if sts2_settings_agent_ready "$settings_path"; then
    return 0
  fi

  if [[ -f "$settings_path" ]] && [[ -s "$settings_path" ]]; then
    if sts2_patch_isolated_settings_file "$settings_path"; then
      echo "[start-game-session] patched isolated settings for clientId $client_id"
      return 0
    fi
    sts2_write_isolated_fallback_settings "$settings_path"
    echo "[start-game-session] seeded isolated settings for clientId $client_id"
    return 0
  fi

  template=""
  steam_root="$user_root/steam"
  if [[ -d "$steam_root" ]]; then
    for candidate in "$steam_root"/settings.save "$steam_root"/*/settings.save; do
      if [[ -f "$candidate" ]]; then
        template="$candidate"
        break
      fi
    done
  fi

  if [[ -n "$template" && -f "$template" ]]; then
    cp -f "$template" "$settings_path"
    if sts2_patch_isolated_settings_file "$settings_path"; then
      echo "[start-game-session] seeded isolated settings for clientId $client_id from steam template"
      return 0
    fi
  fi

  sts2_write_isolated_fallback_settings "$settings_path"
  echo "[start-game-session] seeded isolated settings for clientId $client_id"
}

if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
  return 0
fi

trap cleanup EXIT

usage() {
  cat <<'EOF'
Usage: start-game-session.sh [--exe-path PATH] [--game-root PATH] [--app-manifest PATH] [--app-id ID] [--attempts N] [--delay-seconds N] [--enable-debug-actions] [--api-port PORT] [--keep-existing-processes]
                            [--skip-steam-app-id-file]
                            [-- extra game args...]

Unknown options (including --clientId VALUE or --clientId=VALUE) and everything
after -- are forwarded to the game executable. When --clientId is present, an
isolated SlayTheSpire2/default/<clientId>/settings.save is seeded unless that
file already has mods_enabled=true and an enabled STS2AIAgent entry. Its root is
$STS2_SLAY_USER_ROOT when set, otherwise %APPDATA%/SlayTheSpire2 under
Git Bash or MSYS, otherwise the platform default (macOS application support,
or $XDG_DATA_HOME / ~/.local/share anywhere else).
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
    --)
      shift
      while [[ $# -gt 0 ]]; do
        extra_args+=("$1")
        shift
      done
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      extra_args+=("$1")
      shift
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

client_id=""
if [[ ${#extra_args[@]} -gt 0 ]]; then
  client_id="$(sts2_client_id_from_args "${extra_args[@]}")"
fi
sts2_initialize_isolated_client_settings "$client_id"

(
  cd -- "$launch_dir"
  export STS2_API_PORT="$api_port"
  if [[ "$enable_debug_actions" == "1" ]]; then
    export STS2_ENABLE_DEBUG_ACTIONS=1
  else
    unset STS2_ENABLE_DEBUG_ACTIONS
  fi
  if [[ ${#extra_args[@]} -gt 0 ]]; then
    exec "$exe_path" "${extra_args[@]}"
  else
    exec "$exe_path"
  fi
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
