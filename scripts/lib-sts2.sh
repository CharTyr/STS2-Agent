#!/usr/bin/env bash

sts2_lib_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

sts2_require_command() {
  local command_name="$1"
  local install_hint="${2:-}"

  if command -v "$command_name" >/dev/null 2>&1; then
    return 0
  fi

  echo "$command_name is not installed or not available in PATH." >&2
  if [[ -n "$install_hint" ]]; then
    echo "$install_hint" >&2
  fi
  return 1
}

sts2_resolve_repo_root() {
  local input_root="${1:-}"
  if [[ -z "$input_root" ]]; then
    cd -- "$sts2_lib_dir/.." && pwd
    return
  fi

  cd -- "$input_root" && pwd
}

sts2_detect_game_root() {
  local candidate=""

  for candidate in \
    "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.steam/steam/steamapps/common/Slay the Spire 2"; do
    if [[ -d "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_detect_app_bundle() {
  local game_root="$1"
  local candidate=""

  for candidate in \
    "$game_root/Slay the Spire 2.app" \
    "$game_root/SlayTheSpire2.app" \
    "$game_root"; do
    if [[ -d "$candidate/Contents/MacOS" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_detect_game_executable() {
  local game_root="$1"
  local app_bundle=""
  local candidate=""

  app_bundle="$(sts2_detect_app_bundle "$game_root" || true)"
  if [[ -n "$app_bundle" ]]; then
    for candidate in \
      "$app_bundle/Contents/MacOS/Slay the Spire 2" \
      "$app_bundle/Contents/MacOS/SlayTheSpire2"; do
      if [[ -x "$candidate" ]]; then
        printf '%s\n' "$candidate"
        return 0
      fi
    done
  fi

  for candidate in \
    "$game_root/Slay the Spire 2" \
    "$game_root/SlayTheSpire2"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_default_app_manifest() {
  printf '%s\n' "$HOME/Library/Application Support/Steam/steamapps/appmanifest_2868840.acf"
}

sts2_resolve_app_id() {
  local explicit_app_id="${1:-}"
  local manifest_path="${2:-}"

  if [[ -n "$explicit_app_id" ]]; then
    printf '%s\n' "$explicit_app_id"
    return 0
  fi

  if [[ -z "$manifest_path" || ! -f "$manifest_path" ]]; then
    printf '%s\n' "2868840"
    return 0
  fi

  python3 - "$manifest_path" <<'PY'
import pathlib
import re
import sys

manifest_path = pathlib.Path(sys.argv[1])
text = manifest_path.read_text(encoding="utf-8", errors="replace")
match = re.search(r'"appid"\s+"(?P<appid>\d+)"', text)
print(match.group("appid") if match else "2868840")
PY
}

sts2_ensure_steam_app_id_file() {
  local game_executable="$1"
  local app_id="$2"
  local target_dir
  local app_id_file
  local current_value=""

  target_dir="$(cd -- "$(dirname -- "$game_executable")" && pwd)"
  app_id_file="$target_dir/steam_appid.txt"

  if [[ -f "$app_id_file" ]]; then
    current_value="$(tr -d '[:space:]' < "$app_id_file")"
  fi

  if [[ "$current_value" == "$app_id" ]]; then
    return 0
  fi

  printf '%s' "$app_id" > "$app_id_file"
}

sts2_port_in_use() {
  local port="$1"
  lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
}

sts2_wait_for_port_release() {
  local port="$1"
  local max_attempts="${2:-10}"
  local sleep_seconds="${3:-1}"
  local attempt

  for ((attempt = 0; attempt < max_attempts; attempt++)); do
    if ! sts2_port_in_use "$port"; then
      return 0
    fi

    sleep "$sleep_seconds"
  done

  return 1
}

sts2_stop_pid() {
  local pid="${1:-}"
  local attempt

  if [[ -z "$pid" ]]; then
    return 0
  fi

  if ! kill -0 "$pid" >/dev/null 2>&1; then
    return 0
  fi

  kill "$pid" >/dev/null 2>&1 || true

  for attempt in 1 2 3 4 5; do
    if ! kill -0 "$pid" >/dev/null 2>&1; then
      return 0
    fi

    sleep 1
  done

  kill -9 "$pid" >/dev/null 2>&1 || true
}

sts2_stop_running_games() {
  local pids=""

  pids="$(pgrep -f 'Slay the Spire 2.app/Contents/MacOS/Slay the Spire 2|SlayTheSpire2.app/Contents/MacOS/SlayTheSpire2|/Slay the Spire 2$|/SlayTheSpire2$' || true)"
  if [[ -z "$pids" ]]; then
    return 0
  fi

  echo "$pids" | while IFS= read -r pid; do
    if [[ -n "$pid" ]]; then
      sts2_stop_pid "$pid"
    fi
  done
}

sts2_wait_for_health() {
  local base_url="$1"
  local max_attempts="$2"
  local sleep_seconds="$3"
  local pid="$4"
  local attempt

  python3 - "$base_url" "$max_attempts" "$sleep_seconds" "$pid" <<'PY'
import json
import sys
import time
from urllib import error, request

base_url = sys.argv[1].rstrip("/")
max_attempts = int(sys.argv[2])
sleep_seconds = float(sys.argv[3])
pid = int(sys.argv[4])


def process_alive(target_pid: int) -> bool:
    try:
        import os

        os.kill(target_pid, 0)
        return True
    except OSError:
        return False


for _ in range(max_attempts):
    time.sleep(sleep_seconds)
    try:
        with request.urlopen(f"{base_url}/health", timeout=2) as response:
            if response.status == 200:
                raise SystemExit(0)
    except Exception:
        pass

    if not process_alive(pid):
        raise SystemExit("Game process exited before /health became ready.")

raise SystemExit("Timed out waiting for /health.")
PY
}

sts2_json_value() {
  local path="$1"
  python3 - "$path" <<'PY'
import json
import sys

path = [part for part in sys.argv[1].split(".") if part]
value = json.load(sys.stdin)
for part in path:
    if isinstance(value, list):
        value = value[int(part)]
    else:
        value = value[part]

if value is None:
    print("")
elif isinstance(value, bool):
    print("true" if value else "false")
else:
    print(value)
PY
}

sts2_run_validation() {
  local repo_root="$1"
  shift

  sts2_require_command uv "On macOS, install it with: brew install uv" >/dev/null
  (
    cd -- "$repo_root/mcp_server"
    uv run python ../scripts/run_sts2_validation.py "$@"
  )
}
