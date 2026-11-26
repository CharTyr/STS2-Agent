#!/usr/bin/env bash
# What can this machine do for the STS2 Agent repository, and what is it missing?
#
# The report is built around the same three layers docs/cloud-dev.md describes:
#
#   layer 0  offline  core tests, MCP tests, CI offline gates/contracts -- no game needed
#   layer 1  build    the mod compiles (+ optional dual-version build & PCK pack) -- 3 game DLLs
#   layer 2  run      real-machine validation -- needs the game + a display
#
# Missing pieces are reported, not failed: a cloud box that only ever does layer 0 is a valid
# setup. The exit status is non-zero only when a check that was asked for actually failed (or,
# with --strict, when any layer is incomplete).
#
# Usage:
#   bash scripts/check-cloud-dev.sh                 # report only
#   bash scripts/check-cloud-dev.sh --run offline   # report, then run the full offline suite
#   bash scripts/check-cloud-dev.sh --run build     # report, then compile the mod (+ min baseline if set)
#   bash scripts/check-cloud-dev.sh --run all --strict

set -uo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib-sts2-paths.sh
source "$repo_root/scripts/lib-sts2-paths.sh"

# Pick up ~/.sts2-cloud.env when running non-interactively (e.g. CI / ssh exec / devcontainer).
if [[ -f "$HOME/.sts2-cloud.env" ]]; then
  # shellcheck disable=SC1090
  source "$HOME/.sts2-cloud.env"
fi

mode="none"
strict=0
quiet=0

passed=0
failed=0
missing=0
log_dir="$(mktemp -d)"
keep_logs=0
# A failed check prints the log path, so the directory has to outlive the run -- cleaning it up
# unconditionally would hand the reader a path to a file that is already gone.
cleanup() { [[ "$keep_logs" -eq 1 ]] || rm -rf "$log_dir"; }
trap cleanup EXIT

usage() {
  cat <<'EOF'
Usage: check-cloud-dev.sh [--run none|offline|build|all] [--strict] [--quiet]

  --run offline   core tests, MCP tests, verification gates, path resolver tests, budget proxy
                  self-test, release metadata, API schema semantics, decision benchmark, release
                  package source contract, and MCP tool contracts (when pwsh is installed)
  --run build     compile STS2AIAgent against STS2_DATA_DIR (and STS2_MIN_DATA_DIR if present);
                  also verify PCK packaging via build-mod.sh --skip-install when godot4 is installed
  --run all       both
  --strict        exit non-zero when any layer is incomplete, not only on a failed check
  --quiet         print only the summary and the failures
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run) mode="${2:-none}"; shift 2 ;;
    --strict) strict=1; shift ;;
    --quiet) quiet=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$mode" in
  none|offline|build|all) ;;
  *) echo "--run takes none, offline, build or all" >&2; exit 2 ;;
esac

# Named `section`, not `head`: a function called `head` would shadow the tool `version_of` pipes
# through, and the argument list would come back as a section title.
section() { [[ "$quiet" -eq 1 ]] || printf '\n== %s ==\n' "$*"; }
line() { [[ "$quiet" -eq 1 ]] || printf '  %-38s %s\n' "$1" "$2"; }
ok() { passed=$((passed + 1)); line "$1" "ok    ${2:-}"; }
bad() { failed=$((failed + 1)); line "$1" "FAIL  ${2:-}"; }
gone() { missing=$((missing + 1)); line "$1" "missing  ${2:-}"; }
have() { command -v "$1" >/dev/null 2>&1; }
version_of() { "$@" 2>/dev/null | command head -n 1; }

run_check() {   # run_check <label> <command...>
  local label="$1"
  shift
  local log="$log_dir/$(printf '%s' "$label" | tr ' /()' '____').log"
  local started=$SECONDS

  printf '  ... %s\n' "$label"
  if "$@" >"$log" 2>&1; then
    ok "$label" "$((SECONDS - started))s"
    return 0
  fi

  bad "$label" "$((SECONDS - started))s, log: $log"
  keep_logs=1
  tail -n 25 "$log" | sed 's/^/      /'
  return 1
}

# --- 1. the machine ---------------------------------------------------------
section "machine"
if [[ -r /etc/os-release ]]; then
  # shellcheck disable=SC1091
  . /etc/os-release
  line "system" "${PRETTY_NAME:-unknown} / $(uname -m)"
else
  line "system" "$(uname -s) $(uname -r) / $(uname -m)"
fi
case "$(uname -s)" in
  Linux) ok "kernel" ;;
  *) line "kernel" "$(uname -s) -- this report assumes Linux; layers 0 and 1 still apply" ;;
esac
if [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
  ok "display" "${DISPLAY:-$WAYLAND_DISPLAY}"
else
  gone "display" "layer 2 needs one (or Xvfb)"
fi

# --- 2. toolchains ----------------------------------------------------------
section "toolchains"

# Prefer the user-local install the setup script writes, then whatever is on PATH.
dotnet_bin=""
for candidate in "$HOME/.dotnet/dotnet" dotnet; do
  if [[ "$candidate" == */* ]]; then
    [[ -x "$candidate" ]] && dotnet_bin="$candidate"
  elif have "$candidate"; then
    dotnet_bin="$candidate"
  fi
  [[ -n "$dotnet_bin" ]] && break
done

if [[ -n "$dotnet_bin" ]]; then
  sdk_version="$("$dotnet_bin" --version 2>/dev/null || true)"
  sdk9="$("$dotnet_bin" --list-sdks 2>/dev/null | grep -cE '^9\.0\.' || true)"
  if [[ "$sdk9" -gt 0 ]]; then
    ok "dotnet SDK" "$sdk_version (global.json pins 9.0.100, rollForward latestFeature)"
  else
    bad "dotnet SDK" "$sdk_version -- global.json needs a 9.0.x SDK"
  fi
else
  bad "dotnet SDK" "not found; layer 0 and 1 need it"
fi

python_bin=""
for candidate in python3 python; do
  if have "$candidate" && "$candidate" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 9) else 1)' >/dev/null 2>&1; then
    python_bin="$candidate"
    break
  fi
done
if [[ -n "$python_bin" ]]; then
  ok "python (for the gates)" "$("$python_bin" --version 2>&1)"
else
  bad "python (for the gates)" "no python3 >= 3.9 on PATH"
fi

if have uv; then
  ok "uv" "$(version_of uv --version)"
  if ( cd "$repo_root/mcp_server" && uv run --locked python -c 'import fastmcp' >/dev/null 2>&1 ); then
    ok "mcp_server environment" "uv run --locked imports fastmcp"
  else
    gone "mcp_server environment" "run: (cd mcp_server && uv sync --locked)"
  fi
else
  bad "uv" "not found; the MCP tests cannot run"
fi

if have pwsh; then
  ok "pwsh" "$(pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' 2>/dev/null || echo 'installed, does not start')"
else
  gone "pwsh" "the ps1-syntax gate and test-mcp-tool-profile.ps1 are skipped without it"
fi

ilspy_bin=""
for candidate in "$HOME/.dotnet/tools/ilspycmd" ilspycmd; do
  if [[ "$candidate" == */* ]]; then
    [[ -x "$candidate" ]] && ilspy_bin="$candidate"
  elif have "$candidate"; then
    ilspy_bin="$(command -v "$candidate")"
  fi
  [[ -n "$ilspy_bin" ]] && break
done
if [[ -n "$ilspy_bin" ]]; then
  ok "ilspycmd (optional)" "$(version_of "$ilspy_bin" --version)"
else
  line "ilspycmd (optional)" "not installed (setup-cloud-dev.sh --with-ilspy to inspect sts2.dll)"
fi

if have node; then
  ok "node (optional)" "$(version_of node --version)"
else
  line "node (optional)" "not installed (nothing in the test suite needs it)"
fi

for tool in git unzip zip; do
  if ! have "$tool"; then
    gone "$tool" "needed by packaging/packing steps"
    continue
  fi
  case "$tool" in
    git) ok "$tool" "$(version_of git --version)" ;;
    # zip and unzip do not answer --version; -v is the spelling that prints a banner.
    *) ok "$tool" "$("$tool" -v 2>/dev/null | command head -n 1)" ;;
  esac
done

godot_bin="${GODOT_BIN:-}"
if [[ -z "$godot_bin" ]]; then
  godot_bin="$(command -v godot4 2>/dev/null || command -v godot 2>/dev/null || true)"
fi
if [[ -n "$godot_bin" && -x "$godot_bin" ]]; then
  ok "godot (optional, for PCK)" "$godot_bin"
else
  line "godot (optional, for PCK)" "not installed (setup-cloud-dev.sh --with-godot to pack .pck)"
fi

# --- 3. the repository ------------------------------------------------------
section "repository"
line "root" "$repo_root"
# Tracked sentinels only (AGENTS.md is gitignored and absent from a fresh checkout).
for path in global.json STS2AIAgent/mod_manifest.json docs/project-map.md mcp_server/pyproject.toml mcp_server/uv.lock scripts/check_verification_gates.py; do
  if [[ -e "$repo_root/$path" ]]; then
    ok "$path"
  else
    gone "$path" "wrong directory, or an incomplete checkout"
  fi
done

# --- 4. the game ------------------------------------------------------------
section "game"
data_dir="${STS2_DATA_DIR:-}"
if [[ -z "${data_dir//[[:space:]]/}" ]]; then
  data_dir="$(sts2_detect_data_dir "$(sts2_detect_game_root || true)" "" || true)"
fi
if [[ -z "${data_dir//[[:space:]]/}" && -f "$HOME/.sts2/game-assemblies/sts2.dll" ]]; then
  data_dir="$HOME/.sts2/game-assemblies"
fi

assemblies_ready=1
if [[ -n "$data_dir" && -d "$data_dir" ]]; then
  pack_meta=""
  if [[ -n "$python_bin" && -f "$data_dir/manifest.json" ]]; then
    pack_meta="$("$python_bin" -c 'import json,sys; m=json.load(open(sys.argv[1], encoding="utf-8")); parts=[m.get("gameVersion","unknown")]; m.get("gameCommit") and parts.append(m["gameCommit"]); m.get("godotVersion") and parts.append("Godot "+m["godotVersion"]); print(" (" + ", ".join(parts) + ")")' "$data_dir/manifest.json" 2>/dev/null || true)"
  fi
  ok "data directory" "${data_dir}${pack_meta}"
  for assembly in sts2.dll GodotSharp.dll 0Harmony.dll; do
    if [[ -f "$data_dir/$assembly" ]]; then
      ok "$assembly" "$(du -h "$data_dir/$assembly" 2>/dev/null | cut -f1)"
    else
      gone "$assembly" "layer 1 needs it; run scripts/unpack-game-assemblies.sh"
      assemblies_ready=0
    fi
  done
  if [[ -f "$data_dir/sts2.xml" ]]; then
    ok "sts2.xml (API docs)" "$(du -h "$data_dir/sts2.xml" 2>/dev/null | cut -f1)"
  else
    line "sts2.xml (API docs)" "absent (re-pack without -Minimal to include XML docs)"
  fi
else
  gone "data directory" "set STS2_DATA_DIR, or run scripts/unpack-game-assemblies.sh"
  assemblies_ready=0
fi

min_data_dir="${STS2_MIN_DATA_DIR:-}"
if [[ -z "${min_data_dir//[[:space:]]/}" && -f "$HOME/.sts2/game-assemblies-min/sts2.dll" ]]; then
  min_data_dir="$HOME/.sts2/game-assemblies-min"
fi
if [[ -n "$min_data_dir" && -f "$min_data_dir/sts2.dll" ]]; then
  min_meta=""
  if [[ -n "$python_bin" && -f "$min_data_dir/manifest.json" ]]; then
    min_meta="$("$python_bin" -c 'import json,sys; m=json.load(open(sys.argv[1], encoding="utf-8")); print(" (" + str(m.get("gameVersion","unknown")) + ")")' "$min_data_dir/manifest.json" 2>/dev/null || true)"
  fi
  ok "min-version baseline (optional)" "${min_data_dir}${min_meta}"
else
  line "min-version baseline (optional)" "not configured (unpack-game-assemblies.sh <stable.zip> --as-min)"
fi

game_root="$(sts2_detect_game_root || true)"
if [[ -n "$game_root" && -d "$game_root" ]]; then
  ok "game installation" "$game_root"
  if [[ -f "$game_root/SlayTheSpire2.pck" || -f "$game_root/Slay the Spire 2.pck" ]]; then
    ok "game content (pck)" "$(du -h "$game_root"/*.pck 2>/dev/null | head -n 1 | cut -f1)"
  fi
else
  gone "game installation" "layer 2 only -- docs/cloud-dev.md section 4"
fi

# --- 5. what the layers mean on this machine --------------------------------
section "layers"

layer0=0
if [[ -n "$dotnet_bin" && -n "$python_bin" ]] && have uv; then
  layer0=1
fi
layer1=0
if [[ "$layer0" -eq 1 && "$assemblies_ready" -eq 1 ]]; then
  layer1=1
fi
layer2=0
if [[ "$layer1" -eq 1 && -n "$game_root" && -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
  layer2=1
fi

printf '  layer 0 (offline)  %s\n' "$([[ "$layer0" -eq 1 ]] && echo ready || echo incomplete)"
printf '  layer 1 (build)    %s\n' "$([[ "$layer1" -eq 1 ]] && echo ready || echo incomplete)"
printf '  layer 2 (run)      %s\n' "$([[ "$layer2" -eq 1 ]] && echo ready || echo incomplete)"

# --- 6. the checks that were asked for -------------------------------------
if [[ "$mode" != "none" ]]; then
  section "checks ($mode)"

  if [[ "$mode" == "offline" || "$mode" == "all" ]]; then
    if [[ -n "$dotnet_bin" ]]; then
      run_check "core tests (no game needed)" \
        "$dotnet_bin" run --project "$repo_root/STS2AIAgent.Tests/STS2AIAgent.Tests.csproj" -c Release
    else
      bad "core tests" "no dotnet SDK"
    fi

    if have uv; then
      run_check "MCP unit tests" \
        bash -c "cd '$repo_root/mcp_server' && uv run --locked python -m unittest discover -s tests"
    else
      bad "MCP unit tests" "no uv"
    fi

    if [[ -n "$python_bin" ]]; then
      run_check "verification gates" \
        "$python_bin" "$repo_root/scripts/check_verification_gates.py"
      run_check "model budget proxy self-test" \
        "$python_bin" "$repo_root/scripts/sts2-model-budget-proxy-selftest.py"
      run_check "release metadata" \
        "$python_bin" "$repo_root/scripts/check_release_metadata.py"
      run_check "API schema semantics" \
        "$python_bin" "$repo_root/scripts/test-api-schema.py"
      run_check "offline decision benchmark" \
        bash -c "'$python_bin' '$repo_root/scripts/decision_benchmark.py' && '$python_bin' '$repo_root/scripts/test-decision-benchmark.py'"
      run_check "release package source contract" \
        "$python_bin" "$repo_root/scripts/check_release_package.py" --source-root "$repo_root"
    else
      bad "python offline checks" "no python3"
    fi

    run_check "path resolver tests" bash "$repo_root/scripts/test-lib-sts2-paths.sh"

    if have pwsh; then
      run_check "MCP tool contracts" \
        pwsh -NoProfile -ExecutionPolicy Bypass -File "$repo_root/scripts/test-mcp-tool-profile.ps1"
    else
      gone "MCP tool contracts" "pwsh missing"
    fi
  fi

  if [[ "$mode" == "build" || "$mode" == "all" ]]; then
    if [[ -n "$dotnet_bin" && -f "$data_dir/sts2.dll" ]]; then
      run_check "mod compiles (primary)" \
        "$dotnet_bin" build "$repo_root/STS2AIAgent/STS2AIAgent.csproj" -c Release \
        --nologo -v minimal "-p:Sts2DataDir=$data_dir"
    else
      bad "mod compiles (primary)" "needs the SDK and sts2.dll (layer 1)"
    fi

    if [[ -n "$dotnet_bin" && -n "$min_data_dir" && -f "$min_data_dir/sts2.dll" && "$min_data_dir" != "$data_dir" ]]; then
      run_check "mod compiles (min baseline)" \
        "$dotnet_bin" build "$repo_root/STS2AIAgent/STS2AIAgent.csproj" -c Release \
        --nologo -v minimal "-p:Sts2DataDir=$min_data_dir"
    fi

    if [[ -n "$godot_bin" && -x "$godot_bin" && -f "$data_dir/sts2.dll" ]]; then
      run_check "mod DLL + PCK stages (build-mod.sh)" \
        bash "$repo_root/scripts/build-mod.sh" --configuration Release \
        --data-dir "$data_dir" --godot-exe "$godot_bin" --skip-install
    fi
  fi
fi

# --- 7. summary -------------------------------------------------------------
printf '\nsummary: %d ok, %d missing, %d failed\n' "$passed" "$missing" "$failed"
if [[ "$keep_logs" -eq 1 ]]; then
  printf 'logs: %s\n' "$log_dir"
fi
if [[ "$failed" -gt 0 ]]; then
  exit 1
fi
if [[ "$strict" -eq 1 && "$missing" -gt 0 ]]; then
  exit 1
fi
exit 0
