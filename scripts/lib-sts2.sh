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

sts2_infer_game_root_from_executable() {
  local exe_path="$1"
  local exe_dir=""

  if [[ -z "$exe_path" ]]; then
    return 1
  fi

  exe_dir="$(cd -- "$(dirname -- "$exe_path")" && pwd)"
  case "$exe_dir" in
    */Contents/MacOS)
      cd -- "$exe_dir/../../.." && pwd
      ;;
    *)
      printf '%s\n' "$exe_dir"
      ;;
  esac
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
  local app_id_file
  local current_value=""

  app_id_file="$(sts2_steam_app_id_file_path "$game_executable")"

  if [[ -f "$app_id_file" ]]; then
    current_value="$(tr -d '[:space:]' < "$app_id_file")"
  fi

  if [[ "$current_value" == "$app_id" ]]; then
    return 0
  fi

  printf '%s' "$app_id" > "$app_id_file"
}

sts2_steam_app_id_file_path() {
  local game_executable="$1"
  local target_dir

  target_dir="$(cd -- "$(dirname -- "$game_executable")" && pwd)"
  printf '%s\n' "$target_dir/steam_appid.txt"
}

sts2_port_in_use() {
  local port="$1"
  python3 - "$port" <<'PY'
import socket
import sys

port = int(sys.argv[1])

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.settimeout(0.25)
    raise SystemExit(0 if sock.connect_ex(("127.0.0.1", port)) == 0 else 1)
PY
}

sts2_wait_for_port_release() {
  local port="$1"
  local max_attempts="${2:-${STS2_PORT_RELEASE_ATTEMPTS:-10}}"
  local sleep_seconds="${3:-${STS2_PORT_RELEASE_DELAY_SECONDS:-1}}"
  local attempt

  for ((attempt = 0; attempt < max_attempts; attempt++)); do
    if ! sts2_port_in_use "$port"; then
      return 0
    fi

    sleep "$sleep_seconds"
  done

  ! sts2_port_in_use "$port"
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

sts2_running_game_pids() {
  local exe_path="${1:-${STS2_EXE_PATH:-}}"
  local game_root="${STS2_GAME_ROOT:-}"

  if [[ -z "$exe_path" && -n "$game_root" ]]; then
    exe_path="$(sts2_detect_game_executable "$game_root" || true)"
  fi

  python3 - "$exe_path" <<'PY'
import pathlib
import os
import re
import subprocess
import sys

exe_path = sys.argv[1].strip()
binary_names = ("Slay the Spire 2", "SlayTheSpire2")


def executable_variants(path: pathlib.Path) -> set[str]:
    variants = {str(path)}
    for name in binary_names:
        variants.add(str(path.with_name(name)))
    return variants


def default_candidates() -> set[str]:
    candidates: set[str] = set()
    roots = [
        pathlib.Path.home() / "Library/Application Support/Steam/steamapps/common/Slay the Spire 2",
        pathlib.Path.home() / ".steam/steam/steamapps/common/Slay the Spire 2",
    ]
    bundle_names = ("Slay the Spire 2.app", "SlayTheSpire2.app")

    for root in roots:
        for bundle_name in bundle_names:
            bundle = root / bundle_name / "Contents" / "MacOS"
            for binary_name in binary_names:
                candidates.add(str(bundle / binary_name))

        for binary_name in binary_names:
            candidates.add(str(root / binary_name))

    return candidates


def command_matches_known_binary(command: str) -> bool:
    return re.search(r"(^|/)(Slay the Spire 2|SlayTheSpire2)(\s|$)", command) is not None


if exe_path:
    targets = executable_variants(pathlib.Path(exe_path))
else:
    targets = default_candidates()
    targets.update(binary_names)

try:
    process_table = subprocess.check_output(["ps", "-axww", "-o", "pid=,command="], text=True)
except (OSError, subprocess.CalledProcessError):
    pattern = "|".join(re.escape(target) for target in sorted(targets))
    if not pattern:
        raise SystemExit(0)

    try:
        fallback_output = subprocess.check_output(["pgrep", "-f", pattern], stderr=subprocess.DEVNULL, text=True)
    except (OSError, subprocess.CalledProcessError):
        raise SystemExit(0)

    excluded = {os.getpid(), os.getppid()}
    seen: set[int] = set()
    for line in fallback_output.splitlines():
        line = line.strip()
        if not line.isdigit():
            continue
        pid = int(line)
        if pid in excluded or pid in seen:
            continue
        seen.add(pid)
        print(pid)
    raise SystemExit(0)

for line in process_table.splitlines():
    line = line.strip()
    if not line:
        continue

    pid_text, _, command = line.partition(" ")
    pid_text = pid_text.strip()
    command = command.strip()
    if not pid_text.isdigit() or not command:
        continue

    if any(command == target or command.startswith(target + " ") for target in targets):
        print(pid_text)
        continue

    if not exe_path and command_matches_known_binary(command):
        print(pid_text)
PY
}

sts2_stop_running_games() {
  local exe_path="${1:-}"
  local pids=""

  pids="$(sts2_running_game_pids "$exe_path")"
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
import subprocess
import sys
import time
from urllib import error, parse, request

base_url = sys.argv[1].rstrip("/")
max_attempts = int(sys.argv[2])
sleep_seconds = float(sys.argv[3])
pid = int(sys.argv[4])
port = parse.urlparse(base_url).port


def process_alive(target_pid: int) -> bool:
    try:
        import os

        os.kill(target_pid, 0)
        return True
    except OSError:
        return False


def port_owned_by_pid(target_port: int | None, target_pid: int) -> bool:
    if target_port is None:
        return False

    try:
        subprocess.check_output(
            ["lsof", "-nP", "-a", "-p", str(target_pid), f"-iTCP:{target_port}", "-sTCP:LISTEN"],
            stderr=subprocess.DEVNULL,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return False

    return True


for _ in range(max_attempts):
    if not process_alive(pid):
        raise SystemExit("Game process exited before /health became ready.")

    if not port_owned_by_pid(port, pid):
        time.sleep(sleep_seconds)
        continue

    try:
        with request.urlopen(f"{base_url}/health", timeout=2) as response:
            payload = json.load(response)
            data = payload.get("data") if isinstance(payload, dict) else {}
            if response.status == 200 and payload.get("ok") and data.get("status") == "ready":
                raise SystemExit(0)
    except SystemExit:
        raise
    except Exception:
        pass

    time.sleep(sleep_seconds)

raise SystemExit("Timed out waiting for /health from the launched game process.")
PY
}

sts2_json_value() {
  local path="$1"
  python3 -c '
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
' "$path"
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
