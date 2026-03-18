#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

repo_root_input=""
host="127.0.0.1"
port="8765"
path="/mcp"
api_base_url="http://127.0.0.1:8080"

usage() {
  cat <<'EOF'
Usage: start-mcp-network.sh [--repo-root PATH] [--host HOST] [--port PORT] [--path PATH] [--api-base-url URL]
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-root)
      repo_root_input="${2:-}"
      shift 2
      ;;
    --host)
      host="${2:-}"
      shift 2
      ;;
    --port)
      port="${2:-}"
      shift 2
      ;;
    --path)
      path="${2:-}"
      shift 2
      ;;
    --api-base-url)
      api_base_url="${2:-}"
      shift 2
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

echo "[start-mcp-network] Syncing dependencies..."
uv sync

echo "[start-mcp-network] Starting MCP server on http://$host:$port$path"
exec uv run sts2-network-mcp-server \
  --host "$host" \
  --port "$port" \
  --path "$path" \
  --api-base-url "$api_base_url"
