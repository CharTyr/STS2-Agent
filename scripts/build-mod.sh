#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

configuration="${CONFIGURATION:-Debug}"
repo_root_input="${REPO_ROOT:-}"
game_root_input="${STS2_GAME_ROOT:-}"
data_dir_input="${STS2_DATA_DIR:-}"
mods_dir_input="${STS2_MODS_DIR:-}"
godot_exe_input="${GODOT_BIN:-}"

usage() {
  cat <<'EOF'
Usage: build-mod.sh [--configuration Debug|Release] [--repo-root PATH] [--game-root PATH] [--data-dir PATH] [--mods-dir PATH] [--godot-exe PATH]
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="${2:-}"
      shift 2
      ;;
    --repo-root)
      repo_root_input="${2:-}"
      shift 2
      ;;
    --game-root)
      game_root_input="${2:-}"
      shift 2
      ;;
    --data-dir)
      data_dir_input="${2:-}"
      shift 2
      ;;
    --mods-dir)
      mods_dir_input="${2:-}"
      shift 2
      ;;
    --godot-exe)
      godot_exe_input="${2:-}"
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

resolve_existing_dir() {
  local path="$1"
  cd -- "$path" && pwd
}

candidate_exists() {
  local candidate="$1"
  [[ -n "$candidate" && -d "$candidate" ]]
}

first_existing_dir() {
  local candidate
  for candidate in "$@"; do
    if candidate_exists "$candidate"; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}

resolve_repo_root() {
  local input_root="$1"
  if [[ -z "$input_root" ]]; then
    cd -- "$script_dir/.." && pwd
    return
  fi

  resolve_existing_dir "$input_root"
}

detect_game_root() {
  first_existing_dir \
    "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.steam/steam/steamapps/common/Slay the Spire 2"
}

detect_godot_exe() {
  local candidate=""

  if [[ -n "$godot_exe_input" ]]; then
    printf '%s\n' "$godot_exe_input"
    return 0
  fi

  # Prefer the game-bundled runtime so generated PCK version matches the game engine.
  if [[ -n "${app_bundle:-}" ]]; then
    for candidate in \
      "$app_bundle/Contents/MacOS/Slay the Spire 2" \
      "$app_bundle/Contents/MacOS/SlayTheSpire2"; do
      if [[ -x "$candidate" ]]; then
        printf '%s\n' "$candidate"
        return 0
      fi
    done
  fi

  for candidate in godot godot4 Godot; do
    if command -v "$candidate" >/dev/null 2>&1; then
      command -v "$candidate"
      return 0
    fi
  done

  for candidate in \
    "/Applications/Godot.app/Contents/MacOS/Godot" \
    "$HOME/Applications/Godot.app/Contents/MacOS/Godot"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

repo_root="$(resolve_repo_root "$repo_root_input")"
mod_name="STS2AIAgent"
mod_project="$repo_root/STS2AIAgent/STS2AIAgent.csproj"
build_output_dir="$repo_root/STS2AIAgent/bin/$configuration/net9.0"
staging_dir="$repo_root/build/mods/$mod_name"
manifest_source="$repo_root/STS2AIAgent/mod_manifest.json"
dll_source="$build_output_dir/$mod_name.dll"
pck_output="$staging_dir/$mod_name.pck"
dll_target="$staging_dir/$mod_name.dll"
builder_project_dir="$repo_root/tools/pck_builder"
builder_script="$builder_project_dir/build_pck.gd"

if [[ ! -f "$mod_project" ]]; then
  echo "Mod project not found: $mod_project" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is not installed or not available in PATH." >&2
  echo "On macOS, install it with: brew install dotnet" >&2
  exit 1
fi

game_root="$game_root_input"
if [[ -z "$game_root" ]]; then
  game_root="$(detect_game_root || true)"
fi

if [[ -n "$game_root" ]]; then
  game_root="$(resolve_existing_dir "$game_root")"
fi

app_bundle=""
if [[ -n "$game_root" ]]; then
  if [[ "$game_root" == *.app ]]; then
    app_bundle="$game_root"
  elif [[ -d "$game_root/Slay the Spire 2.app" ]]; then
    app_bundle="$game_root/Slay the Spire 2.app"
  elif [[ -d "$game_root/SlayTheSpire2.app" ]]; then
    app_bundle="$game_root/SlayTheSpire2.app"
  fi
fi

godot_exe="$(detect_godot_exe || true)"
if [[ -z "$godot_exe" ]]; then
  echo "Could not find a Godot executable." >&2
  echo "Pass --godot-exe /path/to/Godot or set GODOT_BIN." >&2
  exit 1
fi

data_dir="$data_dir_input"
if [[ -z "$data_dir" && -n "$game_root" ]]; then
  data_dir="$(first_existing_dir \
    "$game_root/data_sts2_windows_x86_64" \
    "$game_root/data_sts2_osx_arm64" \
    "$game_root/data_sts2_osx_x86_64" \
    "$game_root/data_sts2_macos" \
    "$game_root/data_sts2_macos_arm64" \
    "$game_root/data_sts2_macos_x86_64" \
    "$app_bundle/Contents/Resources/data_sts2_osx_arm64" \
    "$app_bundle/Contents/Resources/data_sts2_osx_x86_64" \
    "$app_bundle/Contents/Resources/data_sts2_macos" \
    "$app_bundle/Contents/Resources/data_sts2_macos_arm64" \
    "$app_bundle/Contents/Resources/data_sts2_macos_x86_64" \
    "$app_bundle/Contents/MacOS/data_sts2_osx_arm64" \
    "$app_bundle/Contents/MacOS/data_sts2_osx_x86_64" \
    "$app_bundle/Contents/MacOS/data_sts2_macos" \
    "$app_bundle/Contents/MacOS/data_sts2_macos_arm64" \
    "$app_bundle/Contents/MacOS/data_sts2_macos_x86_64" \
  || true)"
fi

if [[ -z "$data_dir" ]]; then
  echo "Could not determine the game's data directory." >&2
  echo "Pass --data-dir /path/to/data_sts2_* or set STS2_DATA_DIR." >&2
  exit 1
fi

data_dir="$(resolve_existing_dir "$data_dir")"

mods_dir="$mods_dir_input"
if [[ -z "$mods_dir" && -n "$app_bundle" ]]; then
  mods_dir="$app_bundle/Contents/MacOS/mods"
fi
if [[ -z "$mods_dir" && -n "$game_root" ]]; then
  mods_dir="$game_root/mods"
fi

if [[ -z "$mods_dir" ]]; then
  echo "Could not determine the mods directory." >&2
  echo "Pass --mods-dir /path/to/mods or set STS2_MODS_DIR." >&2
  exit 1
fi

mkdir -p "$staging_dir"
mkdir -p "$mods_dir"

echo "[build-mod] Building C# mod project..."
dotnet build "$mod_project" -c "$configuration" /p:Sts2DataDir="$data_dir"

if [[ ! -f "$dll_source" ]]; then
  echo "Built DLL not found: $dll_source" >&2
  exit 1
fi

cp -f "$dll_source" "$dll_target"

if [[ ! -f "$manifest_source" ]]; then
  echo "Manifest not found: $manifest_source" >&2
  exit 1
fi

echo "[build-mod] Packing mod_manifest.json into PCK..."
"$godot_exe" --headless --path "$builder_project_dir" --script "$builder_script" -- "$manifest_source" "$pck_output"

if [[ ! -f "$pck_output" ]]; then
  echo "PCK output not found: $pck_output" >&2
  exit 1
fi

echo "[build-mod] Preparing game mods directory..."
cp -f "$dll_target" "$mods_dir/$mod_name.dll"
cp -f "$pck_output" "$mods_dir/$mod_name.pck"

echo "[build-mod] Done."
echo "[build-mod] Using data dir: $data_dir"
echo "[build-mod] Using mods dir: $mods_dir"
echo "[build-mod] Using Godot: $godot_exe"
echo "[build-mod] Installed files:"
echo "  $mods_dir/$mod_name.dll"
echo "  $mods_dir/$mod_name.pck"
