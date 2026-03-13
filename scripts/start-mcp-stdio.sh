#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root_input="${1:-}"

resolve_repo_root() {
  local input_root="$1"
  if [[ -z "$input_root" ]]; then
    cd -- "$script_dir/.." && pwd
    return
  fi

  cd -- "$input_root" && pwd
}

repo_root="$(resolve_repo_root "$repo_root_input")"
mcp_root="$repo_root/mcp_server"

if [[ ! -d "$mcp_root" ]]; then
  echo "mcp_server directory not found: $mcp_root" >&2
  exit 1
fi

if ! command -v uv >/dev/null 2>&1; then
  echo "uv is not installed or not available in PATH." >&2
  echo "On macOS, install it with: brew install uv" >&2
  exit 1
fi

cd -- "$mcp_root"

echo "[start-mcp-stdio] Syncing dependencies..."
uv sync

echo "[start-mcp-stdio] Starting MCP server over stdio..."
exec uv run sts2-mcp-server
