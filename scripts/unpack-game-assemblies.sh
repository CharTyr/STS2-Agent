#!/usr/bin/env bash
# Unpack the assemblies pack written by scripts/pack-game-assemblies.ps1 on a machine that has
# the game, and point STS2_DATA_DIR (or STS2_MIN_DATA_DIR with --as-min) at the result.
#
# This is layer 1 of docs/cloud-dev.md: after it runs, `dotnet build STS2AIAgent` works on a
# machine that has never had Steam installed. It does not make the game runnable -- that needs
# the whole installation, which is a different (and much larger) transfer.
#
# Verification happens before extraction: every file's SHA-256 is checked against the pack's own
# manifest.json, because a truncated download produces a compiler error hundreds of lines away
# from the cause.
#
# Usage:
#   bash scripts/unpack-game-assemblies.sh                         # newest pack it can find
#   bash scripts/unpack-game-assemblies.sh sts2-assemblies-*.zip   # a specific pack
#   bash scripts/unpack-game-assemblies.sh <pack.zip> --as-min     # secondary baseline (STS2_MIN_DATA_DIR)
#   bash scripts/unpack-game-assemblies.sh <pack.zip> --decompile  # also decompile sts2.dll if ilspycmd exists

set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
default_dest="${STS2_ASSEMBLIES_DIR:-$HOME/.sts2/game-assemblies}"
dest=""
slot=""
as_min=0
env_file="$HOME/.sts2-cloud.env"
zip_path=""
write_env=1
force=0
do_decompile=0
decompile_dir=""

# The resolver owns "where the game is", and mcp_server/tests/test_posix_script_portability.py
# keeps every POSIX script sourcing it. It earns its place at the end of this script: on a machine
# that also has a real installation, the pack's version can be compared with the installed one,
# which is exactly the mismatch that otherwise surfaces as a compiler error about a moved member.
# shellcheck source=scripts/lib-sts2-paths.sh
source "$repo_root/scripts/lib-sts2-paths.sh"

usage() {
  cat <<'EOF'
Usage: unpack-game-assemblies.sh [ARCHIVE] [options]

  ARCHIVE             the .zip written by scripts/pack-game-assemblies.ps1; when omitted, the newest
                      one under build/game-assemblies/ and then $HOME is used
  --zip PATH          same as the positional argument
  --dest PATH         extraction directory (default: ~/.sts2/game-assemblies, or ~/.sts2/game-assemblies-min
                      when --as-min is set)
  --slot NAME         unpack into ~/.sts2/game-assemblies-<NAME> without overwriting the default slot
  --as-min            unpack as the secondary minimum-version compatibility baseline and write
                      STS2_MIN_DATA_DIR into ~/.sts2-cloud.env (used by check-cloud-dev.sh --run build)
  --decompile         after unpacking, run ilspycmd on sts2.dll into extraction/decompiled/<version>
                      (gitignored) so cloud editors and AI agents can grep game internals
  --decompile-dir DIR custom destination for --decompile
  --no-env            do not write STS2_DATA_DIR / STS2_MIN_DATA_DIR into ~/.sts2-cloud.env
  --force             replace files already in the destination
  -h, --help          this text
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --zip) zip_path="${2:-}"; shift 2 ;;
    --dest) dest="${2:-}"; shift 2 ;;
    --slot) slot="${2:-}"; shift 2 ;;
    --as-min) as_min=1; shift ;;
    --decompile) do_decompile=1; shift ;;
    --decompile-dir) do_decompile=1; decompile_dir="${2:-}"; shift 2 ;;
    --no-env) write_env=0; shift ;;
    --force) force=1; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    *) zip_path="$1"; shift ;;
  esac
done

if [[ -z "$dest" ]]; then
  if [[ "$as_min" -eq 1 ]]; then
    dest="${STS2_MIN_ASSEMBLIES_DIR:-$HOME/.sts2/game-assemblies-min}"
  elif [[ -n "$slot" ]]; then
    safe_slot="$(printf '%s' "$slot" | tr -cs 'A-Za-z0-9._-' '-')"
    dest="$HOME/.sts2/game-assemblies-$safe_slot"
  else
    dest="$default_dest"
  fi
fi

python_bin=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'pass' >/dev/null 2>&1; then
    python_bin="$candidate"
    break
  fi
done
if [[ -z "$python_bin" ]]; then
  echo "python3 is required to verify the pack; install it first." >&2
  exit 1
fi

# Discovery is newest-first and deliberately narrow: a stray sts2-assemblies-*.zip in $HOME is
# much more likely to be the pack someone just scp'd than a coincidence.
if [[ -z "$zip_path" ]]; then
  newest=""
  while IFS= read -r candidate; do
    [[ -z "$candidate" ]] && continue
    if [[ -z "$newest" || "$candidate" -nt "$newest" ]]; then
      newest="$candidate"
    fi
  done < <(ls -1t "$repo_root"/build/game-assemblies/sts2-assemblies-*.zip "$HOME"/sts2-assemblies-*.zip 2>/dev/null || true)
  zip_path="$newest"
fi

if [[ -z "$zip_path" || ! -f "$zip_path" ]]; then
  echo "No assembly pack found." >&2
  echo "Copy one over first (see docs/cloud-dev.md), then:" >&2
  echo "  bash scripts/unpack-game-assemblies.sh /path/to/sts2-assemblies-*.zip" >&2
  exit 1
fi

echo "[unpack] pack:        $zip_path"
echo "[unpack] destination: $dest"

"$python_bin" - "$zip_path" "$dest" "$force" <<'PY'
import hashlib
import json
import pathlib
import sys
import zipfile

zip_path = pathlib.Path(sys.argv[1])
dest = pathlib.Path(sys.argv[2])
force = sys.argv[3] == "1"

REQUIRED = ("sts2.dll", "GodotSharp.dll", "0Harmony.dll")


def fail(message):
    print(f"[unpack] error: {message}", file=sys.stderr)
    raise SystemExit(1)


with zipfile.ZipFile(zip_path) as archive:
    names = archive.namelist()
    if "manifest.json" not in names:
        fail(f"{zip_path.name} has no manifest.json -- it is not an assembly pack")

    manifest = json.loads(archive.read("manifest.json"))
    entries = manifest.get("files") or []
    if not entries:
        fail("manifest.json lists no files")

    packed = {entry["name"] for entry in entries}
    absent = [name for name in REQUIRED if name not in packed]
    if absent:
        fail(f"the pack is missing {', '.join(absent)}")

    # Verify every byte before writing any of it. A path outside the destination would mean the
    # archive was not written by the packing script, and is refused rather than sanitised.
    for entry in entries:
        name = entry["name"]
        if not name or "/" in name or "\\" in name or name.startswith("."):
            fail(f"refusing a suspicious member name: {name!r}")
        expected = str(entry.get("sha256", "")).lower()
        if len(expected) != 64:
            fail(f"manifest has no usable checksum for {name}")
        blob = archive.read(name)
        actual = hashlib.sha256(blob).hexdigest()
        if actual != expected:
            fail(f"checksum mismatch for {name}: manifest {expected[:12]}, archive {actual[:12]}")
        if len(blob) >= 1048576:
            size_text = f"{len(blob) / 1048576:6.1f} MB"
        else:
            size_text = f"{len(blob) / 1024:6.1f} KB"
        print(f"[unpack] verified {name:<24} {size_text}")

    if dest.exists() and any(dest.iterdir()) and not force:
        fail(f"{dest} already holds files; pass --force to replace them")

    dest.mkdir(parents=True, exist_ok=True)
    for entry in entries:
        archive.extract(entry["name"], dest)
    (dest / "manifest.json").write_bytes(archive.read("manifest.json"))

version = manifest.get("gameVersion", "unknown")
platform = manifest.get("platform", "unknown")
commit = manifest.get("gameCommit") or ""
godot_ver = manifest.get("godotVersion") or ""
runtime_ver = manifest.get("runtimeFrameworkVersion") or ""
extra = []
if commit:
    extra.append(f"commit={commit}")
if godot_ver:
    extra.append(f"Godot={godot_ver}")
if runtime_ver:
    extra.append(f".NET={runtime_ver}")
suffix = f" ({', '.join(extra)})" if extra else ""
print(f"[unpack] game version: {version} [{platform}]{suffix}")
PY

packed_version="$("$python_bin" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("gameVersion","unknown"))' "$dest/manifest.json" 2>/dev/null || echo unknown)"
packed_godot="$("$python_bin" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("godotVersion") or "")' "$dest/manifest.json" 2>/dev/null || true)"

# If this machine also has a real installation, the pack and the game should be the same version
# (skip warning when unpacking an explicit secondary minimum-version baseline).
if [[ "$as_min" -eq 0 ]]; then
  game_root="$(sts2_detect_game_root || true)"
  if [[ -n "$game_root" && -f "$game_root/release_info.json" ]]; then
    installed_version="$("$python_bin" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("version","unknown"))' "$game_root/release_info.json" 2>/dev/null || echo unknown)"
    if [[ "$installed_version" != "$packed_version" ]]; then
      echo "[unpack] warning: pack is from $packed_version but the installed game is $installed_version" >&2
      echo "[unpack]          re-run scripts/pack-game-assemblies.ps1 on the machine with the game" >&2
    else
      echo "[unpack] version matches the installed game: $installed_version"
    fi
  fi
fi

env_var="STS2_DATA_DIR"
if [[ "$as_min" -eq 1 ]]; then
  env_var="STS2_MIN_DATA_DIR"
fi

if [[ "$write_env" -eq 1 ]]; then
  quoted="$(printf '%q' "$dest")"
  if [[ ! -f "$env_file" ]]; then
    printf '# Generated by scripts/setup-cloud-dev.sh -- edit the script, not this file.\n' > "$env_file"
  fi
  tmp="$env_file.tmp"
  {
    grep -v "^export ${env_var}=" "$env_file" || true
    printf 'export %s=%s\n' "$env_var" "$quoted"
  } > "$tmp"
  mv "$tmp" "$env_file"
  echo "[unpack] wrote $env_var to $env_file"
  echo "[unpack] in this shell: export $env_var=$quoted"
fi

if [[ "$do_decompile" -eq 1 ]]; then
  ilspy_bin=""
  for candidate in "$HOME/.dotnet/tools/ilspycmd" ilspycmd; do
    if [[ "$candidate" == */* ]]; then
      [[ -x "$candidate" ]] && ilspy_bin="$candidate"
    elif command -v "$candidate" >/dev/null 2>&1; then
      ilspy_bin="$(command -v "$candidate")"
    fi
    [[ -n "$ilspy_bin" ]] && break
  done

  if [[ -z "$ilspy_bin" ]]; then
    echo "[unpack] warning: ilspycmd not found; run 'bash scripts/setup-cloud-dev.sh --with-ilspy' (or 'dotnet tool install -g ilspycmd') first" >&2
  else
    if [[ -z "$decompile_dir" ]]; then
      safe_ver="$(printf '%s' "$packed_version" | tr -cs 'A-Za-z0-9._-' '-')"
      decompile_dir="$repo_root/extraction/decompiled/$safe_ver"
    fi
    mkdir -p "$decompile_dir"
    echo "[unpack] decompiling sts2.dll into $decompile_dir ..."
    "$ilspy_bin" -p -o "$decompile_dir" -r "$dest" "$dest/sts2.dll"
    echo "[unpack] decompiled source ready at $decompile_dir (gitignored)"
  fi
fi

cat <<EOF

[unpack] next steps
  dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release -p:Sts2DataDir=$dest
  bash scripts/check-cloud-dev.sh --run build
EOF
if [[ -n "$packed_godot" ]]; then
  echo "  # optional: install matching Godot $packed_godot headless for PCK packaging:"
  echo "  GODOT_VERSION=$packed_godot bash scripts/setup-cloud-dev.sh --no-apt --no-dotnet --no-uv --no-pwsh --no-sync --with-godot"
fi
echo ""
