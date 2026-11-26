#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
# shellcheck source=scripts/lib-sts2-paths.sh
source "${SCRIPT_DIR}/lib-sts2-paths.sh"

CONFIGURATION="Release"
MOD_DLL=""
GAME_ROOT=""
DATA_DIR=""
JSON_FLAG=""

require_value() {
  if [[ $# -lt 2 || -z "${2:-}" ]]; then
    echo "[check-game-abi] Missing value for $1" >&2
    exit 2
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration|-c) require_value "$@"; CONFIGURATION="$2"; shift 2 ;;
    --mod-dll) require_value "$@"; MOD_DLL="$2"; shift 2 ;;
    --game-root) require_value "$@"; GAME_ROOT="$2"; shift 2 ;;
    --data-dir) require_value "$@"; DATA_DIR="$2"; shift 2 ;;
    --json) JSON_FLAG="--json"; shift ;;
    -h|--help)
      echo "Usage: $0 [--configuration Release|Debug] [--mod-dll PATH] [--data-dir PATH] [--game-root PATH] [--json]"
      exit 0
      ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

RESOLVED_DATA_DIR=""
if sts2_has_text "$DATA_DIR"; then
  RESOLVED_DATA_DIR="$DATA_DIR"
elif sts2_has_text "${STS2_DATA_DIR:-}"; then
  RESOLVED_DATA_DIR="$STS2_DATA_DIR"
else
  RESOLVED_GAME_ROOT="$(sts2_resolve_game_root "$GAME_ROOT")"
  APP_BUNDLE="$(sts2_detect_app_bundle "$RESOLVED_GAME_ROOT" || true)"
  RESOLVED_DATA_DIR="$(sts2_detect_data_dir "$RESOLVED_GAME_ROOT" "$APP_BUNDLE" || true)"
fi

if [[ -z "$RESOLVED_DATA_DIR" || ! -f "${RESOLVED_DATA_DIR}/sts2.dll" ]]; then
  echo "[check-game-abi] Could not find sts2.dll. Pass --data-dir or set STS2_DATA_DIR." >&2
  exit 2
fi
RESOLVED_DATA_DIR="$(sts2_resolve_existing_dir "$RESOLVED_DATA_DIR")"

if [[ -n "$MOD_DLL" ]]; then
  if [[ ! -f "$MOD_DLL" ]]; then
    echo "[check-game-abi] Mod assembly not found: ${MOD_DLL}" >&2
    exit 2
  fi
else
  MOD_DLL="${REPO_ROOT}/STS2AIAgent/bin/${CONFIGURATION}/net9.0/STS2AIAgent.dll"
  if [[ ! -f "$MOD_DLL" ]]; then
    dotnet build "${REPO_ROOT}/STS2AIAgent/STS2AIAgent.csproj" -c "${CONFIGURATION}" "-p:Sts2DataDir=${RESOLVED_DATA_DIR}" --nologo -v minimal >&2
  fi
fi

TEST_DLL="${REPO_ROOT}/STS2AIAgent.Tests/bin/${CONFIGURATION}/net9.0/STS2AIAgent.Tests.dll"
dotnet build "${REPO_ROOT}/STS2AIAgent.Tests/STS2AIAgent.Tests.csproj" -c "${CONFIGURATION}" --nologo -v minimal >&2

if [[ -n "$JSON_FLAG" ]]; then
  exec dotnet "${TEST_DLL}" --check-abi "${MOD_DLL}" "${RESOLVED_DATA_DIR}" --json
else
  exec dotnet "${TEST_DLL}" --check-abi "${MOD_DLL}" "${RESOLVED_DATA_DIR}"
fi
