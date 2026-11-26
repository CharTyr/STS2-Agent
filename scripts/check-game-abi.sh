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

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration|-c) CONFIGURATION="$2"; shift 2 ;;
    --mod-dll) MOD_DLL="$2"; shift 2 ;;
    --game-root) GAME_ROOT="$2"; shift 2 ;;
    --data-dir) DATA_DIR="$2"; shift 2 ;;
    --json) JSON_FLAG="--json"; shift ;;
    -h|--help)
      echo "Usage: $0 [--configuration Release|Debug] [--mod-dll PATH] [--data-dir PATH] [--game-root PATH] [--json]"
      exit 0
      ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

RESOLVED_DATA_DIR="$(resolve_sts2_data_dir "$DATA_DIR" "$GAME_ROOT" || true)"
if [[ -z "$RESOLVED_DATA_DIR" || ! -f "${RESOLVED_DATA_DIR}/sts2.dll" ]]; then
  echo "[check-game-abi] Could not find sts2.dll. Pass --data-dir or set STS2_DATA_DIR." >&2
  exit 2
fi

if [[ -z "$MOD_DLL" ]]; then
  MOD_DLL="${REPO_ROOT}/STS2AIAgent/bin/${CONFIGURATION}/net9.0/STS2AIAgent.dll"
fi

if [[ ! -f "$MOD_DLL" ]]; then
  dotnet build "${REPO_ROOT}/STS2AIAgent/STS2AIAgent.csproj" -c "${CONFIGURATION}" "-p:Sts2DataDir=${RESOLVED_DATA_DIR}" --nologo -v minimal
fi

TEST_DLL="${REPO_ROOT}/STS2AIAgent.Tests/bin/${CONFIGURATION}/net9.0/STS2AIAgent.Tests.dll"
if [[ ! -f "$TEST_DLL" ]]; then
  dotnet build "${REPO_ROOT}/STS2AIAgent.Tests/STS2AIAgent.Tests.csproj" -c "${CONFIGURATION}" --nologo -v minimal
fi

if [[ -n "$JSON_FLAG" ]]; then
  exec dotnet "${TEST_DLL}" --check-abi "${MOD_DLL}" "${RESOLVED_DATA_DIR}" --json
else
  exec dotnet "${TEST_DLL}" --check-abi "${MOD_DLL}" "${RESOLVED_DATA_DIR}"
fi
