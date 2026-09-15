#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

configuration="${CONFIGURATION:-Debug}"
repo_root_input="${REPO_ROOT:-}"
game_root_input="${STS2_GAME_ROOT:-}"
data_dir_input="${STS2_DATA_DIR:-}"
mods_dir_input="${STS2_MODS_DIR:-}"
godot_exe_input="${GODOT_BIN:-}"

skip_install=0

usage() {
  cat <<'EOF'
Usage: build-mod.sh [--configuration Debug|Release] [--repo-root PATH] [--game-root PATH] [--data-dir PATH] [--mods-dir PATH] [--godot-exe PATH] [--skip-install]
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
    --skip-install)
      skip_install=1
      shift
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

# Path work lives in lib-sts2-paths.sh: one owner for where the game is, shared with the
# validation scripts and exercised offline by scripts/test-lib-sts2-paths.sh. This file keeps
# only what is specific to building.
. "$script_dir/lib-sts2-paths.sh"

resolve_repo_root() {
  local input_root="$1"
  if [[ -z "$input_root" ]]; then
    cd -- "$script_dir/.." && pwd
    return
  fi

  sts2_resolve_existing_dir "$input_root"
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
mod_id_source="$repo_root/STS2AIAgent/mod_id.json"
dll_source="$build_output_dir/$mod_name.dll"
pck_output="$staging_dir/$mod_name.pck"
dll_target="$staging_dir/$mod_name.dll"
mod_id_target="$staging_dir/mod_id.json"
legacy_manifest_target="$staging_dir/$mod_name.json"
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

# Argument, then STS2_GAME_ROOT, then detection across every Steam library, then the
# conventional location -- one order, shared with the Windows scripts.
game_root="$(sts2_resolve_game_root "$game_root_input")"

# A path that is not there used to be inherited silently: everything downstream fell back to
# an empty string and the run ended much later with a message about the data directory. Say
# it here instead, unless the caller supplied the directories the build actually needs.
if [[ ! -d "$game_root" && -z "$data_dir_input" ]]; then
  echo "Slay the Spire 2 install not found at '$game_root'." >&2
  echo "Pass --game-root PATH, set STS2_GAME_ROOT, or pass --data-dir (and --mods-dir) to build without the game." >&2
  exit 1
fi

if [[ -d "$game_root" ]]; then
  game_root="$(sts2_resolve_existing_dir "$game_root")"
fi

app_bundle="$(sts2_detect_app_bundle "$game_root" || true)"

godot_exe="$(detect_godot_exe || true)"
if [[ -z "$godot_exe" ]]; then
  echo "Could not find a Godot executable." >&2
  echo "Pass --godot-exe /path/to/Godot or set GODOT_BIN." >&2
  exit 1
fi

data_dir="$data_dir_input"
if [[ -z "$data_dir" ]]; then
  data_dir="$(sts2_detect_data_dir "$game_root" "$app_bundle" || true)"
fi

if [[ -z "$data_dir" ]]; then
  echo "Could not determine the game's data directory." >&2
  echo "Pass --data-dir /path/to/data_sts2_* or set STS2_DATA_DIR." >&2
  exit 1
fi

if [[ ! -d "$data_dir" ]]; then
  echo "Game data directory not found: $data_dir" >&2
  echo "Check --data-dir / STS2_DATA_DIR; it has to be the directory holding sts2.dll." >&2
  exit 1
fi

data_dir="$(sts2_resolve_existing_dir "$data_dir")"

mods_dir="$mods_dir_input"
if [[ -z "$mods_dir" && -d "$game_root" ]]; then
  mods_dir="$(sts2_mods_dir_for "$app_bundle" "$game_root" || true)"
fi

if [[ -z "$mods_dir" ]]; then
  echo "Could not determine the mods directory." >&2
  echo "Pass --mods-dir /path/to/mods or set STS2_MODS_DIR." >&2
  exit 1
fi

if [[ "$skip_install" -eq 0 ]]; then
  mods_parent="$(dirname -- "$mods_dir")"
  if [[ ! -d "$mods_parent" ]]; then
    echo "Install location not found at '$mods_parent'." >&2
    echo "Pass --game-root PATH, set STS2_GAME_ROOT, or use --skip-install to build without installing." >&2
    exit 1
  fi
fi

mkdir -p "$staging_dir"

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

if [[ ! -f "$mod_id_source" ]]; then
  echo "Mod ID manifest not found: $mod_id_source" >&2
  exit 1
fi

echo "[build-mod] Packing mod_manifest.json into PCK..."
"$godot_exe" --headless --path "$builder_project_dir" --script "$builder_script" -- "$manifest_source" "$pck_output"

if [[ ! -f "$pck_output" ]]; then
  echo "PCK output not found: $pck_output" >&2
  exit 1
fi

cp -f "$mod_id_source" "$mod_id_target"
if [[ -f "$legacy_manifest_target" ]]; then
  rm -f "$legacy_manifest_target"
fi

if [[ "$skip_install" -eq 0 ]]; then
  echo "[build-mod] Preparing game mods directory..."
  mkdir -p "$mods_dir"
  cp -f "$dll_target" "$mods_dir/$mod_name.dll"
  cp -f "$pck_output" "$mods_dir/$mod_name.pck"
  cp -f "$mod_id_target" "$mods_dir/mod_id.json"
  if [[ -f "$mods_dir/$mod_name.json" ]]; then
    rm -f "$mods_dir/$mod_name.json"
  fi
fi

echo "[build-mod] Done."
echo "[build-mod] Using data dir: $data_dir"
echo "[build-mod] Using mods dir: $mods_dir"
echo "[build-mod] Using Godot: $godot_exe"
if [[ "$skip_install" -eq 1 ]]; then
  echo "[build-mod] Skipped installation; staged files are in: $staging_dir"
else
  echo "[build-mod] Installed files:"
  echo "  $mods_dir/$mod_name.dll"
  echo "  $mods_dir/$mod_name.pck"
  echo "  $mods_dir/mod_id.json"
fi
