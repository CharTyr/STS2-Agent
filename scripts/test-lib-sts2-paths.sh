#!/usr/bin/env bash
# Offline proof that the POSIX path resolver does what macOS and Linux need.
#
# No game, no Steam, no network: $HOME points at a fixture and the interpreter is discovered, so
# these assertions run anywhere -- the repository's CI runs them under Git Bash on a Windows
# runner. What cannot be proved this way (that the game itself starts) stays with the real-machine
# checklists; this file only claims what it can actually see.
#
# Usage: bash scripts/test-lib-sts2-paths.sh

set -uo pipefail          # deliberately not -e: a non-zero status is the answer in some checks

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=scripts/lib-sts2-paths.sh
source "$here/lib-sts2-paths.sh"

checks=0
failures=0

check_same() {   # check_same <label> <expected> <actual>
  local label="$1"
  local expected="$2"
  local actual="$3"

  checks=$((checks + 1))
  if [[ "$expected" == "$actual" ]]; then
    printf 'ok   %s\n' "$label"
  else
    failures=$((failures + 1))
    printf 'FAIL %s\n     expected: [%s]\n     actual:   [%s]\n' "$label" "$expected" "$actual"
  fi
}

check_fails() {   # check_fails <label> <command...>
  local label="$1"
  shift
  local status=0
  "$@" >/dev/null 2>&1 || status=$?
  if [[ "$status" -eq 0 ]]; then
    check_same "$label" "non-zero status" "0"
  else
    check_same "$label" "non-zero status" "non-zero status"
  fi
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# A python3 that only pretends to work is a real thing on a Windows Git Bash, so the fixture
# includes one and the resolver has to cope with it.
stub_dir="$WORK/stub"
mkdir -p "$stub_dir"
printf '#!/bin/sh\nexit 49\n' > "$stub_dir/python3"
chmod +x "$stub_dir/python3" 2>/dev/null || true

interpreter=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'pass' >/dev/null 2>&1; then
    interpreter="$candidate"
    break
  fi
done
export STS2_PYTHON="$interpreter"

# The fixture library holds the manifest; the game itself sits in a second library, which is the
# case a resolver with two hardcoded paths cannot reach.
fixture_home="$WORK/home"
first_library="$fixture_home/Library/Application Support/Steam"
second_library="$WORK/second library/SteamLibrary"
game_in_second="$second_library/steamapps/common/Slay the Spire 2"

mkdir -p "$first_library/steamapps" "$game_in_second"
printf '\n' > "$first_library/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf"

cat > "$first_library/steamapps/libraryfolders.vdf" <<VDF
"libraryfolders"
 {
  "0"
  {
   "path"    "$first_library"
  }
  "1"
  {
   "path"    "$second_library/"
  }
 }
VDF

printf 'fixture: %s\n\n' "$WORK"

echo "-- the conventional roots are the ones Steam uses on macOS and Linux --"
check_same "~/Library/Application Support/Steam is a root" "1" \
  "$(sts2_steam_roots | grep -Fxc "$HOME/Library/Application Support/Steam" || true)"
check_same "~/.steam/steam is a root" "1" \
  "$(sts2_steam_roots | grep -Fxc "$HOME/.steam/steam" || true)"
check_same "~/.local/share/Steam is a root" "1" \
  "$(sts2_steam_roots | grep -Fxc "$HOME/.local/share/Steam" || true)"

echo
echo "-- a game in a second library is found through libraryfolders.vdf --"
check_same "the second library's install is detected" "$game_in_second" \
  "$(HOME="$fixture_home" sts2_detect_game_root || true)"
check_same "the resolver reports the same path" "$game_in_second" \
  "$(HOME="$fixture_home" sts2_resolve_game_root)"
check_same "an explicit argument still wins" "$WORK/explicit" \
  "$(HOME="$fixture_home" sts2_resolve_game_root "$WORK/explicit")"
check_same "the variable wins over detection" "$WORK/from env" \
  "$(HOME="$fixture_home" STS2_GAME_ROOT="$WORK/from env" sts2_resolve_game_root)"
check_same "the manifest is found in the first library" \
  "$first_library/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf" \
  "$(HOME="$fixture_home" sts2_default_app_manifest)"
check_same "an explicit manifest wins" "$WORK/explicit.acf" \
  "$(HOME="$fixture_home" STS2_APP_MANIFEST="$WORK/from env.acf" sts2_resolve_app_manifest "$WORK/explicit.acf")"

echo
echo "-- nothing installed: detection says so, the resolver names the conventional path --"
empty_home="$WORK/nothing"
mkdir -p "$empty_home"
check_fails "detection reports the miss" env HOME="$empty_home" bash -c ". '$here/lib-sts2-paths.sh'; sts2_detect_game_root"
check_same "the resolver names the conventional macOS path" \
  "$empty_home/Library/Application Support/Steam/steamapps/common/Slay the Spire 2" \
  "$(HOME="$empty_home" sts2_resolve_game_root)"
check_same "the manifest resolver names the conventional path" \
  "$empty_home/Library/Application Support/Steam/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf" \
  "$(HOME="$empty_home" sts2_resolve_app_manifest)"

echo
echo "-- the vdf reader copes with what Steam writes --"
escaped_vdf="$WORK/escaped.vdf"
cat > "$escaped_vdf" <<'VDF'
"libraryfolders"
 {
  "0"
  {
   "path"    "C:\\program files (x86)\\steam"
  }
  "1"
  {
   "path"    "/home/someone/.steam/steam/"
  }
 }
VDF
expected_backslash="$(printf 'C:%sprogram files (x86)%ssteam' '\' '\')"
check_same "escaped separators collapse" "$expected_backslash" \
  "$(STS2_PYTHON="$interpreter" sts2_library_paths_from_vdf "$escaped_vdf" | head -n 1)"
check_same "a trailing separator is dropped" "/home/someone/.steam/steam" \
  "$(STS2_PYTHON="$interpreter" sts2_library_paths_from_vdf "$escaped_vdf" | tail -n 1)"
check_same "a missing vdf is not an error" "0" \
  "$(STS2_PYTHON="$interpreter" sts2_library_paths_from_vdf "$WORK/absent.vdf" >/dev/null; echo $?)"

echo
echo "-- an interpreter that cannot run is skipped, not trusted --"
check_fails "a python3 that exits without working is not accepted" \
  env PATH="$stub_dir" "$BASH" -c ". '$here/lib-sts2-paths.sh'; sts2_python"
check_same "without an interpreter the vdf read is empty, not fatal" "0" \
  "$(env PATH="$stub_dir" "$BASH" -c ". '$here/lib-sts2-paths.sh'; sts2_library_paths_from_vdf '$escaped_vdf' >/dev/null; echo $?")"

echo
echo "-- a macOS install is a .app bundle, and the data can live in three places --"
bundle_root="$WORK/bundled"
bundle="$bundle_root/Slay the Spire 2.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources/data_sts2_macos_arm64"
check_same "the bundle is found under the game root" "$bundle" \
  "$(sts2_detect_app_bundle "$bundle_root")"
check_same "a game root that is itself the bundle is accepted" "$bundle" \
  "$(sts2_detect_app_bundle "$bundle")"
check_same "the data directory under Resources is found" "$bundle/Contents/Resources/data_sts2_macos_arm64" \
  "$(sts2_detect_data_dir "$bundle_root" "$bundle")"

unpacked="$WORK/unpacked"
mkdir -p "$unpacked/data_sts2_osx_x86_64"
check_same "an unpacked macOS install is found" "$unpacked/data_sts2_osx_x86_64" \
  "$(sts2_detect_data_dir "$unpacked" "")"
check_same "no data directory is not an error, just a miss" "non-zero status" \
  "$(sts2_detect_data_dir "$WORK/nothing" "" >/dev/null 2>&1 && echo zero || echo non-zero status)"
check_same "mods live beside the binary inside a bundle" "$bundle/Contents/MacOS/mods" \
  "$(sts2_mods_dir_for "$bundle" "$bundle_root")"
check_same "mods live at the top of an unpacked install" "$unpacked/mods" \
  "$(sts2_mods_dir_for "" "$unpacked")"

echo
echo "-- the process matcher gets its candidate paths from the same resolver --"
game_root_fixture="$WORK/installed/SteamLibrary/steamapps/common/Slay the Spire 2"
mkdir -p "$game_root_fixture/Slay the Spire 2.app/Contents/MacOS"
check_same "both binary spellings are candidates" "2" \
  "$(sts2_game_binary_names | grep -Fxc -e "Slay the Spire 2" -e "SlayTheSpire2" || true)"
check_same "the bundle path is a candidate" "1" \
  "$(sts2_game_executable_candidates | grep -Fxc "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/Slay the Spire 2.app/Contents/MacOS/Slay the Spire 2" || true)"
check_same "the loose command pattern names both spellings" "1" \
  "$(sts2_game_command_pattern | grep -c "Slay the Spire 2" || true)"
check_same "variants stay inside the executable's own directory" "1" \
  "$(sts2_game_executable_variants "/tmp/one/Slay the Spire 2.app/Contents/MacOS/SlayTheSpire2" | grep -Fxc "/tmp/one/Slay the Spire 2.app/Contents/MacOS/Slay the Spire 2" || true)"
check_same "no executable means no variants" "non-zero status" \
  "$(sts2_game_executable_variants >/dev/null 2>&1 && echo zero || echo non-zero status)"

echo
if [[ "$failures" -eq 0 ]]; then
  printf 'all %d checks passed\n' "$checks"
  exit 0
fi

printf '%d of %d checks failed\n' "$failures" "$checks" >&2
exit 1
