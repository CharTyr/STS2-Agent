#!/usr/bin/env bash
# Bootstrap a Linux cloud development machine for this repository.
#
# The machine is described as three layers, and this script builds layer 0 (plus optional
# layer-1 developer tools like ilspycmd and Godot headless):
#
#   layer 0  offline  .NET SDK 9, uv + Python, PowerShell 7 -> core tests, MCP tests, all gates
#   layer 1  build    layer 0 plus the game's assemblies (+ optional ilspycmd / Godot 4.x)
#   layer 2  run      layer 1 plus a game installation and a display -> real-machine validation
#
# Layer 0 needs no game, no Steam and no network beyond the installers. Layer 1 is what
# scripts/unpack-game-assemblies.sh adds; layer 2 is a Steam installation, which this script
# deliberately does not attempt -- see docs/cloud-dev.md.
#
# Everything installs under $HOME, so no root is required once the apt packages are present.
# Re-running is safe: each step is skipped when it is already satisfied.
#
# Usage:
#   bash scripts/setup-cloud-dev.sh                               # layer 0, then a capability report
#   bash scripts/setup-cloud-dev.sh --with-ilspy --with-godot     # also install ilspycmd + Godot 4.5.1 headless
#   bash scripts/setup-cloud-dev.sh --no-apt                      # inside a container image
#   bash scripts/setup-cloud-dev.sh --dry-run                     # print what would be installed

set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$HOME/.sts2-cloud.env"
bashrc_marker="sts2-cloud-dev"

# The install layout belongs to the resolver, and mcp_server/tests/test_posix_script_portability.py
# enforces that every POSIX script sources it rather than re-deriving where the game is. Here it
# buys a real behaviour: on a machine that already has the game (a WSL box, a workstation), the
# detected paths are recorded without the caller passing --game-root/--data-dir.
# shellcheck source=scripts/lib-sts2-paths.sh
source "$repo_root/scripts/lib-sts2-paths.sh"

with_apt=1
with_dotnet=1
with_uv=1
with_pwsh=1
with_ilspy=0
with_godot=0
with_node=0
with_gh=0
do_sync=1
dry_run=0
game_root="${STS2_GAME_ROOT:-}"
data_dir="${STS2_DATA_DIR:-}"
min_data_dir="${STS2_MIN_DATA_DIR:-}"
mods_dir="${STS2_MODS_DIR:-}"

usage() {
  cat <<'EOF'
Usage: setup-cloud-dev.sh [options]

  --no-apt            skip apt-get (an image or a container that already has the packages)
  --no-dotnet         skip the .NET SDK install
  --no-uv             skip uv
  --no-pwsh           skip PowerShell 7 (the ps1 verification gate and the MCP contract script
                      use it; without it those two checks are reported as unavailable)
  --with-ilspy        install ilspycmd as a .NET global tool (~/.dotnet/tools) so you can inspect
                      and decompile sts2.dll directly on the cloud machine
  --with-godot        install the matching Linux Godot 4.x binary (~/.local/godot) so build-mod.sh
                      can pack STS2AIAgent.pck without a full game install
  --with-node         also install Node.js LTS and pnpm (only needed for the fast-context MCP
                      dev dependency in package.json, not for any test)
  --with-gh           also install the GitHub CLI through apt
  --no-sync           do not run `uv sync --locked` in mcp_server
  --game-root PATH    record STS2_GAME_ROOT in ~/.sts2-cloud.env
  --data-dir PATH     record STS2_DATA_DIR (the directory holding sts2.dll)
  --min-data-dir PATH record STS2_MIN_DATA_DIR (optional secondary baseline for dual-version builds)
  --mods-dir PATH     record STS2_MODS_DIR
  --dry-run           print every command instead of running it
  -h, --help          this text

Environment overrides:
  PWSH_VERSION  (default 7.4.6)
  GODOT_VERSION (default auto-detected from unpacked manifest.json, else 4.5.1)
  NODE_VERSION  (default 22.11.0)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-apt) with_apt=0; shift ;;
    --no-dotnet) with_dotnet=0; shift ;;
    --no-uv) with_uv=0; shift ;;
    --no-pwsh) with_pwsh=0; shift ;;
    --with-ilspy) with_ilspy=1; shift ;;
    --with-godot) with_godot=1; shift ;;
    --with-node) with_node=1; shift ;;
    --with-gh) with_gh=1; shift ;;
    --no-sync) do_sync=0; shift ;;
    --game-root) game_root="${2:-}"; shift 2 ;;
    --data-dir) data_dir="${2:-}"; shift 2 ;;
    --min-data-dir) min_data_dir="${2:-}"; shift 2 ;;
    --mods-dir) mods_dir="${2:-}"; shift 2 ;;
    --dry-run) dry_run=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

say() { printf '[setup] %s\n' "$*"; }
warn() { printf '[setup] warning: %s\n' "$*" >&2; }
die() { printf '[setup] error: %s\n' "$*" >&2; exit 1; }

run() {
  if [[ "$dry_run" -eq 1 ]]; then
    # Quoted per argument: `bash -c "cd ... && uv sync"` printed as `$*` reads like
    # `bash -c cd ...`, which is a different command.
    printf '  + '
    printf '%q ' "$@"
    printf '\n'
    return 0
  fi
  "$@"
}

have() { command -v "$1" >/dev/null 2>&1; }

apt_install() {
  local packages=("$@")
  if [[ "${#packages[@]}" -eq 0 ]]; then
    return 0
  fi
  if [[ "$with_apt" -eq 0 ]]; then
    say "skipping apt: ${packages[*]}"
    return 0
  fi

  local sudo_cmd=()
  if [[ "$(id -u)" -ne 0 ]]; then
    if have sudo; then
      sudo_cmd=(sudo)
    else
      warn "not root and sudo is missing; install these packages yourself: ${packages[*]}"
      return 1
    fi
  fi

  run "${sudo_cmd[@]}" env DEBIAN_FRONTEND=noninteractive apt-get update -qq
  run "${sudo_cmd[@]}" env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${packages[@]}"
}

missing_packages() {
  local names=("$@")
  local missing=()
  local probe=""
  local name=""

  for name in "${names[@]}"; do
    case "$name" in
      git|curl|unzip|tar|jq|python3) probe="$name" ;;
      xz-utils) probe="xz" ;;
      ca-certificates) probe="update-ca-certificates" ;;
      *) probe="$name" ;;
    esac
    if ! have "$probe"; then
      missing+=("$name")
    fi
  done
  if [[ "${#missing[@]}" -gt 0 ]]; then
    printf '%s\n' "${missing[@]}"
  fi
}

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

# --- 1. base apt packages ---------------------------------------------------
if [[ "$with_apt" -eq 1 ]]; then
  base_pkgs=(git curl ca-certificates xz-utils unzip tar jq python3)
  if [[ "$with_gh" -eq 1 ]]; then
    base_pkgs+=(gh)
  fi
  to_install=()
  while IFS= read -r pkg; do
    [[ -n "$pkg" ]] && to_install+=("$pkg")
  done < <(missing_packages "${base_pkgs[@]}")
  if [[ "${#to_install[@]}" -gt 0 ]]; then
    say "installing base packages: ${to_install[*]}"
    apt_install "${to_install[@]}" || warn "some apt packages could not be installed"
  else
    say "base packages already present"
  fi
fi

# --- 2. .NET SDK 9.0 --------------------------------------------------------
install_dotnet() {
  local dotnet_cmd=""
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    dotnet_cmd="$HOME/.dotnet/dotnet"
  elif have dotnet; then
    dotnet_cmd="$(command -v dotnet)"
  fi

  if [[ -n "$dotnet_cmd" ]]; then
    local sdk9=""
    sdk9="$("$dotnet_cmd" --list-sdks 2>/dev/null | grep -cE '^9\.0\.' || true)"
    if [[ "$sdk9" -gt 0 ]]; then
      say ".NET 9 SDK already present: $("$dotnet_cmd" --version 2>/dev/null || echo 9.0.x)"
      return 0
    fi
  fi

  say "installing .NET 9.0 SDK into ~/.dotnet (matches global.json 9.0.100 rollForward)"
  run mkdir -p "$HOME/.dotnet"
  run curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$tmp_dir/dotnet-install.sh"
  run chmod +x "$tmp_dir/dotnet-install.sh"
  run bash "$tmp_dir/dotnet-install.sh" --channel 9.0 --install-dir "$HOME/.dotnet" --no-path
}

if [[ "$with_dotnet" -eq 1 ]]; then
  install_dotnet
fi

# --- 3. uv ------------------------------------------------------------------
install_uv() {
  if have uv || [[ -x "$HOME/.local/bin/uv" ]]; then
    say "uv already present"
    return 0
  fi

  say "installing uv into ~/.local/bin (UV_UNMANAGED_INSTALL keeps shell rc clean)"
  run mkdir -p "$HOME/.local/bin"
  run curl -fsSL "https://astral.sh/uv/install.sh" -o "$tmp_dir/uv-install.sh"
  run env UV_UNMANAGED_INSTALL="$HOME/.local/bin" sh "$tmp_dir/uv-install.sh"
}

if [[ "$with_uv" -eq 1 ]]; then
  install_uv
fi

# --- 4. PowerShell 7 --------------------------------------------------------
install_pwsh() {
  local version="${PWSH_VERSION:-7.4.6}"
  local arch=""

  if have pwsh || [[ -x "$HOME/.local/bin/pwsh" ]]; then
    say "pwsh already present"
    return 0
  fi

  case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) warn "no pwsh tarball for $(uname -m); skipping"; return 0 ;;
  esac

  say "installing PowerShell $version into ~/.local/powershell"
  run mkdir -p "$HOME/.local/powershell" "$HOME/.local/bin"
  run curl -fsSL \
    "https://github.com/PowerShell/PowerShell/releases/download/v${version}/powershell-${version}-linux-${arch}.tar.gz" \
    -o "$tmp_dir/pwsh.tar.gz"
  run tar -xzf "$tmp_dir/pwsh.tar.gz" -C "$HOME/.local/powershell"
  run chmod +x "$HOME/.local/powershell/pwsh"
  run ln -sf "$HOME/.local/powershell/pwsh" "$HOME/.local/bin/pwsh"

  if [[ "$dry_run" -eq 1 ]]; then
    return 0
  fi
  if ! "$HOME/.local/bin/pwsh" -NoProfile -Command 'exit 0' >/dev/null 2>&1; then
    warn "pwsh was extracted but does not start; the usual cause on a minimal server image is a"
    warn "missing ICU library -- install it with: sudo apt-get install -y libicu-dev"
  else
    say "pwsh works: $("$HOME/.local/bin/pwsh" -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')"
  fi
}

if [[ "$with_pwsh" -eq 1 ]]; then
  install_pwsh
fi

# --- 5. Optional developer tools: ilspycmd, Godot headless, Node ------------
install_ilspy() {
  local dotnet_cmd=""
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    dotnet_cmd="$HOME/.dotnet/dotnet"
  elif have dotnet; then
    dotnet_cmd="$(command -v dotnet)"
  fi

  if [[ -x "$HOME/.dotnet/tools/ilspycmd" ]] || have ilspycmd; then
    say "ilspycmd already present"
    return 0
  fi
  if [[ -z "$dotnet_cmd" ]]; then
    warn "dotnet is missing; cannot install ilspycmd"
    return 0
  fi

  say "installing ilspycmd into ~/.dotnet/tools (for inspecting/decompiling sts2.dll)"
  run "$dotnet_cmd" tool install --global ilspycmd
}

if [[ "$with_ilspy" -eq 1 ]]; then
  install_ilspy
fi

detect_godot_version() {
  if [[ -n "${GODOT_VERSION:-}" ]]; then
    printf '%s\n' "$GODOT_VERSION"
    return 0
  fi
  local manifest_candidate="${data_dir:-$HOME/.sts2/game-assemblies}/manifest.json"
  local py=""
  py="$(sts2_python || true)"
  if [[ -n "$py" && -f "$manifest_candidate" ]]; then
    local parsed=""
    parsed="$("$py" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("godotVersion") or "")' "$manifest_candidate" 2>/dev/null || true)"
    if [[ -n "$parsed" ]]; then
      printf '%s\n' "$parsed"
      return 0
    fi
  fi
  printf '%s\n' "4.5.1"
}

install_godot() {
  local version=""
  version="$(detect_godot_version)"
  local arch=""

  if have godot4 || [[ -x "$HOME/.local/bin/godot4" ]]; then
    say "godot4 already present"
    return 0
  fi

  case "$(uname -m)" in
    x86_64|amd64) arch="linux.x86_64" ;;
    aarch64|arm64) arch="linux.arm64" ;;
    *) warn "no Godot tarball for $(uname -m); skipping"; return 0 ;;
  esac

  local zip_name="Godot_v${version}-stable_${arch}.zip"
  local bin_name="Godot_v${version}-stable_${arch}"
  local url="https://github.com/godotengine/godot-builds/releases/download/${version}-stable/${zip_name}"

  say "installing Godot ${version}-stable (${arch}) into ~/.local/godot (for PCK packaging)"
  run mkdir -p "$HOME/.local/godot" "$HOME/.local/bin"
  run curl -fsSL "$url" -o "$tmp_dir/$zip_name"
  run unzip -oq "$tmp_dir/$zip_name" -d "$HOME/.local/godot"
  run chmod +x "$HOME/.local/godot/$bin_name"
  run ln -sf "$HOME/.local/godot/$bin_name" "$HOME/.local/bin/godot4"
}

if [[ "$with_godot" -eq 1 ]]; then
  install_godot
fi

install_node() {
  local version="${NODE_VERSION:-22.11.0}"
  local arch=""

  if have node; then
    say "node already present: $(node --version)"
  else
    case "$(uname -m)" in
      x86_64|amd64) arch="x64" ;;
      aarch64|arm64) arch="arm64" ;;
      *) warn "no Node tarball for $(uname -m); skipping"; return 0 ;;
    esac

    say "installing Node $version into ~/.local/node"
    run mkdir -p "$HOME/.local/node" "$HOME/.local/bin"
    run curl -fsSL "https://nodejs.org/dist/v${version}/node-v${version}-linux-${arch}.tar.xz" \
      -o "$tmp_dir/node.tar.xz"
    run tar -xJf "$tmp_dir/node.tar.xz" -C "$HOME/.local/node" --strip-components=1
    run ln -sf "$HOME/.local/node/bin/node" "$HOME/.local/bin/node"
    run ln -sf "$HOME/.local/node/bin/npm" "$HOME/.local/bin/npm"
  fi

  if [[ "$dry_run" -eq 0 ]] && ! have pnpm; then
    run "$HOME/.local/bin/npm" install -g pnpm || warn "pnpm install failed; not needed for any test"
  fi
}

if [[ "$with_node" -eq 1 ]]; then
  install_node
fi

# --- 6. remember where everything is ---------------------------------------
if [[ -z "${game_root//[[:space:]]/}" ]]; then
  game_root="$(sts2_detect_game_root || true)"
fi
if [[ -z "${data_dir//[[:space:]]/}" && -n "$game_root" ]]; then
  data_dir="$(sts2_detect_data_dir "$game_root" "" || true)"
fi
if [[ -z "${data_dir//[[:space:]]/}" && -f "$HOME/.sts2/game-assemblies/sts2.dll" ]]; then
  data_dir="$HOME/.sts2/game-assemblies"
fi
if [[ -z "${min_data_dir//[[:space:]]/}" && -f "$HOME/.sts2/game-assemblies-min/sts2.dll" ]]; then
  min_data_dir="$HOME/.sts2/game-assemblies-min"
fi

write_env_file() {
  local lines=()
  local path_prefix="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin"

  lines+=("export PATH=\"$path_prefix:\$PATH\"")
  lines+=("export DOTNET_CLI_TELEMETRY_OPTOUT=1")
  lines+=("export DOTNET_NOLOGO=1")
  if [[ -n "${game_root//[[:space:]]/}" ]]; then
    # shellcheck disable=SC2059
    lines+=("export STS2_GAME_ROOT=$(printf '%q' "$game_root")")
  fi
  if [[ -n "${data_dir//[[:space:]]/}" ]]; then
    # shellcheck disable=SC2059
    lines+=("export STS2_DATA_DIR=$(printf '%q' "$data_dir")")
  fi
  if [[ -n "${min_data_dir//[[:space:]]/}" ]]; then
    # shellcheck disable=SC2059
    lines+=("export STS2_MIN_DATA_DIR=$(printf '%q' "$min_data_dir")")
  fi
  if [[ -n "${mods_dir//[[:space:]]/}" ]]; then
    # shellcheck disable=SC2059
    lines+=("export STS2_MODS_DIR=$(printf '%q' "$mods_dir")")
  fi

  [[ "$dry_run" -eq 1 ]] && return 0

  {
    printf '# Generated by scripts/setup-cloud-dev.sh -- edit the script, not this file.\n'
    printf '# Sourced from ~/.bashrc under the "%s" marker.\n' "$bashrc_marker"
    printf '%s\n' "${lines[@]}"
  } > "$env_file"
  say "wrote $env_file"
}

hook_bashrc() {
  local bashrc="$HOME/.bashrc"

  if [[ "$dry_run" -eq 1 ]]; then
    printf '  + append the %s hook to %s\n' "$bashrc_marker" "$bashrc"
    return 0
  fi
  if [[ -f "$bashrc" ]] && grep -q "# >>> $bashrc_marker >>>" "$bashrc"; then
    return 0
  fi

  {
    printf '\n# >>> %s >>>\n' "$bashrc_marker"
    printf '[ -f %q ] && . %q\n' "$env_file" "$env_file"
    printf '# <<< %s <<<\n' "$bashrc_marker"
  } >> "$bashrc"
  say "hooked $bashrc (open a new shell, or run: . $env_file)"
}

write_env_file
hook_bashrc

# This shell needs the same PATH the next steps use.
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"

# --- 7. the Python side of the repository ----------------------------------
sync_mcp() {
  if [[ "$do_sync" -eq 0 ]]; then
    say "skipping uv sync"
    return 0
  fi
  if ! have uv; then
    warn "uv is missing, so mcp_server was not synced"
    return 0
  fi

  say "uv sync --locked in mcp_server (downloads a managed Python 3.12 on first run)"
  run bash -c "cd '$repo_root/mcp_server' && uv sync --locked"
}

sync_mcp

# --- 8. auto-unpack workspace assembly pack when none is unpacked yet -------
if [[ "$dry_run" -eq 0 && -z "${data_dir//[[:space:]]/}" && -f "$repo_root/scripts/unpack-game-assemblies.sh" ]]; then
  workspace_pack="$(ls -1t "$repo_root"/build/game-assemblies/sts2-assemblies-*.zip 2>/dev/null | head -n 1 || true)"
  if [[ -n "$workspace_pack" ]]; then
    say "found workspace assembly pack $(basename "$workspace_pack"); unpacking into ~/.sts2/game-assemblies"
    bash "$repo_root/scripts/unpack-game-assemblies.sh" "$workspace_pack" --force || warn "auto-unpack failed; run unpack-game-assemblies.sh manually"
  fi
fi

# --- 9. say what this machine can now do -----------------------------------
if [[ "$dry_run" -eq 0 && -f "$repo_root/scripts/check-cloud-dev.sh" ]]; then
  echo
  bash "$repo_root/scripts/check-cloud-dev.sh" --quiet || true
fi

cat <<EOF

[setup] next steps
  layer 0 (works now)      bash scripts/check-cloud-dev.sh --run offline
  layer 1 (the mod builds) bash scripts/unpack-game-assemblies.sh <pack.zip>
                           then re-run: bash scripts/check-cloud-dev.sh --run build
  layer 1+ (decompile/PCK) bash scripts/setup-cloud-dev.sh --no-apt --no-sync --with-ilspy --with-godot
  layer 2 (the game runs)  a Steam installation on this machine -- docs/cloud-dev.md section 4

EOF
