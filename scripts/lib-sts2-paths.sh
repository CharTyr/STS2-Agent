#!/usr/bin/env bash
# Where Slay the Spire 2 lives on macOS and Linux, resolved from the caller instead of assumed.
#
# This is the POSIX half of the contract the Windows scripts follow through lib-sts2-paths.ps1:
# explicit argument, then environment variable, then detection, then the conventional location as
# a last resort. Detection adds the extra libraries a Steam installation lists in its own
# libraryfolders.vdf, so a game moved to a second library is still found.
#
# Nothing here starts the game, opens a socket, or needs the game to be running. It may run an
# interpreter, and that is what makes it testable without a game -- see scripts/test-lib-sts2-paths.sh.

STS2_DEFAULT_APP_ID="2868840"

sts2_has_text() {
  # A value that is nothing but whitespace is not a path. Windows asks the same question with
  # [string]::IsNullOrWhiteSpace, so "not set" means the same thing on both sides.
  local value="${1:-}"

  [[ -n "${value//[[:space:]]/}" ]]
}

sts2_unique_lines() {
  # Order-preserving deduplication. A Steam library is often reachable through more than one root
  # (the conventional path and the installation's own library list name the same directory), and a
  # repeated candidate is noise every caller then has to reason about. A linear search rather than
  # an associative array, because macOS ships bash 3.2.
  local line=""
  local seen=""
  local key=""

  while IFS= read -r line; do
    key="|$line|"
    case "$seen" in
      *"$key"*) continue ;;
    esac
    seen="$seen$key"
    printf '%s\n' "$line"
  done
}

sts2_python() {
  # The interpreter for the jobs bash is bad at (JSON, sockets, process tables). macOS ships
  # python3; a Windows Git Bash may only have `python`, and may have a `python3` stub that
  # resolves on PATH and then exits without a word -- so a candidate counts only once it has
  # actually run something.
  local candidate=""

  for candidate in "${STS2_PYTHON:-}" python3 python; do
    if [[ -z "$candidate" ]]; then
      continue
    fi
    if ! command -v "$candidate" >/dev/null 2>&1; then
      continue
    fi
    if "$candidate" -c 'pass' >/dev/null 2>&1; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_steam_roots() {
  # The conventional roots, in the order Steam itself would have written them.
  printf '%s\n' "$HOME/Library/Application Support/Steam"
  printf '%s\n' "$HOME/.steam/steam"
  printf '%s\n' "$HOME/.local/share/Steam"
}

sts2_library_paths_from_vdf() {
  local vdf="$1"
  local interpreter=""

  if [[ ! -f "$vdf" ]]; then
    return 0
  fi

  interpreter="$(sts2_python || true)"
  if [[ -z "$interpreter" ]]; then
    # Without an interpreter the conventional roots still answer for the first library; a second
    # one is simply not seen. Silence is right here -- the caller decides what a miss means.
    return 0
  fi

  "$interpreter" - "$vdf" <<'PY' || true
import pathlib
import re
import sys

# Steam stores the library paths with escaped backslashes on some installs and plain ones on
# others, so both the escaping and any trailing separator are settled here, in a language where
# that is one expression instead of a quoting puzzle.
backslash = chr(92)
text = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
for match in re.finditer(r'"path"\s+"([^"]+)"', text):
    value = match.group(1)
    while backslash * 2 in value:
        value = value.replace(backslash * 2, backslash)
    value = value.rstrip("/" + backslash)
    if value:
        # Written as bytes: a Windows interpreter turns \n into \r\n on the way out, and
        # the reader here would otherwise hand every path a trailing carriage return.
        sys.stdout.buffer.write((value + "\n").encode("utf-8"))
PY
}

sts2_steam_libraries() {
  # Every Steam library on the machine: the conventional roots, plus whatever each conventional
  # installation lists for itself. A root that is not installed is still listed -- it is a
  # candidate like any other, and the caller's existence check is what rejects it. The carriage
  # return is stripped defensively, because the interpreter above is whatever the machine has.
  local steam_root=""
  local library=""

  {
    while IFS= read -r steam_root; do
      printf '%s\n' "$steam_root"
      while IFS= read -r library; do
        printf '%s\n' "${library%$'\r'}"
      done < <(sts2_library_paths_from_vdf "$steam_root/steamapps/libraryfolders.vdf")
    done < <(sts2_steam_roots)
  } | sts2_unique_lines
}

sts2_resolve_existing_dir() {
  # The absolute spelling of a directory that exists, so a path that travels keeps meaning the
  # same place.
  local path="${1:-}"

  cd -- "$path" && pwd
}

sts2_data_dir_candidates() {
  # Where the game keeps its engine data, in the order worth trying. A macOS install is a .app
  # bundle, so the data may sit beside the binary, under Resources, or -- for an unpacked build
  # -- at the top of the game directory. The Windows directory name is here too, so the same
  # script also builds from a Git Bash shell on a Windows install.
  local game_root="${1:-}"
  local app_bundle="${2:-}"

  # With neither input the candidates would anchor to the filesystem root, which is a way to find
  # something unrelated rather than to find the game.
  if [[ -z "$game_root" && -z "$app_bundle" ]]; then
    return 1
  fi

  printf '%s\n' "$game_root/data_sts2_windows_x86_64"
  printf '%s\n' "$game_root/data_sts2_osx_arm64"
  printf '%s\n' "$game_root/data_sts2_osx_x86_64"
  printf '%s\n' "$game_root/data_sts2_macos"
  printf '%s\n' "$game_root/data_sts2_macos_arm64"
  printf '%s\n' "$game_root/data_sts2_macos_x86_64"
  printf '%s\n' "$app_bundle/Contents/Resources/data_sts2_osx_arm64"
  printf '%s\n' "$app_bundle/Contents/Resources/data_sts2_osx_x86_64"
  printf '%s\n' "$app_bundle/Contents/Resources/data_sts2_macos"
  printf '%s\n' "$app_bundle/Contents/Resources/data_sts2_macos_arm64"
  printf '%s\n' "$app_bundle/Contents/Resources/data_sts2_macos_x86_64"
  printf '%s\n' "$app_bundle/Contents/MacOS/data_sts2_osx_arm64"
  printf '%s\n' "$app_bundle/Contents/MacOS/data_sts2_osx_x86_64"
  printf '%s\n' "$app_bundle/Contents/MacOS/data_sts2_macos"
  printf '%s\n' "$app_bundle/Contents/MacOS/data_sts2_macos_arm64"
  printf '%s\n' "$app_bundle/Contents/MacOS/data_sts2_macos_x86_64"
}

sts2_detect_data_dir() {
  local candidate=""
  local game_root="${1:-}"
  local app_bundle="${2:-}"

  while IFS= read -r candidate; do
    if [[ -n "$candidate" && -d "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done < <(sts2_data_dir_candidates "$game_root" "$app_bundle")

  return 1
}

sts2_mods_dir_for() {
  # Inside a bundle the mods belong beside the binary; an unpacked install keeps them at the
  # top of the game directory. Empty output means the caller has to be told where to put them.
  local app_bundle="${1:-}"
  local game_root="${2:-}"

  if [[ -n "$app_bundle" ]]; then
    printf '%s\n' "$app_bundle/Contents/MacOS/mods"
    return 0
  fi
  if [[ -n "$game_root" ]]; then
    printf '%s\n' "$game_root/mods"
    return 0
  fi

  return 1
}
sts2_game_root_candidates() {
  local library=""

  while IFS= read -r library; do
    printf '%s\n' "$library/steamapps/common/Slay the Spire 2"
  done < <(sts2_steam_libraries)
}

sts2_detect_app_bundle() {
  local game_root="$1"
  local candidate=""

  for candidate in \
    "$game_root/Slay the Spire 2.app" \
    "$game_root/SlayTheSpire2.app" \
    "$game_root"; do
    if [[ -d "$candidate/Contents/MacOS" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_detect_game_executable() {
  local game_root="${1:-}"
  local app_bundle=""
  local candidate=""

  app_bundle="$(sts2_detect_app_bundle "$game_root" || true)"
  if [[ -n "$app_bundle" ]]; then
    for candidate in \
      "$app_bundle/Contents/MacOS/Slay the Spire 2" \
      "$app_bundle/Contents/MacOS/SlayTheSpire2"; do
      if [[ -x "$candidate" ]]; then
        printf '%s\n' "$candidate"
        return 0
      fi
    done
  fi

  for candidate in \
    "$game_root/Slay the Spire 2" \
    "$game_root/SlayTheSpire2"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

sts2_infer_game_root_from_executable() {
  local exe_path="${1:-}"
  local exe_dir=""

  if [[ -z "$exe_path" ]]; then
    return 1
  fi

  exe_dir="$(cd -- "$(dirname -- "$exe_path")" 2>/dev/null && pwd)"
  if [[ -z "$exe_dir" ]]; then
    # A directory that is not there is a miss, not an empty answer: the siblings all report a miss
    # with a non-zero status, and a caller reading this output would otherwise pass an empty
    # string on as if it were a path.
    return 1
  fi

  case "$exe_dir" in
    */Contents/MacOS)
      cd -- "$exe_dir/../../.." && pwd
      ;;
    *)
      printf '%s\n' "$exe_dir"
      ;;
  esac
}

sts2_game_binary_names() {
  # The two spellings the game binary has shipped under; everything that looks for it accepts
  # both.
  printf '%s\n' "Slay the Spire 2"
  printf '%s\n' "SlayTheSpire2"
}

sts2_game_executable_candidates() {
  # Every place the binary can be when nobody said where the game is: inside the .app bundle of
  # each Steam library, and beside it for an unpacked install. Pure path work, which is what keeps
  # the process matcher a process matcher.
  local library=""
  local root=""
  local name=""

  while IFS= read -r library; do
    root="$library/steamapps/common/Slay the Spire 2"
    while IFS= read -r name; do
      printf '%s\n' "$root/Slay the Spire 2.app/Contents/MacOS/$name"
      printf '%s\n' "$root/SlayTheSpire2.app/Contents/MacOS/$name"
      printf '%s\n' "$root/$name"
    done < <(sts2_game_binary_names)
  done < <(sts2_steam_libraries)
}

sts2_game_executable_variants() {
  # What one known executable path also answers to, so a game started through a symlink or under
  # its other name is still recognised.
  local exe_path="${1:-}"
  local directory=""
  local name=""

  if [[ -z "$exe_path" ]]; then
    return 1
  fi

  printf '%s\n' "$exe_path"
  directory="$(dirname -- "$exe_path")"
  while IFS= read -r name; do
    printf '%s\n' "$directory/$name"
  done < <(sts2_game_binary_names)
}

sts2_game_command_pattern() {
  # The regular expression the fallback uses to match a process table by command line.
  printf '%s\n' "(^|/)(Slay the Spire 2|SlayTheSpire2)( |$)"
}

sts2_detect_game_root() {
  # The first candidate that exists on disk. No conventional fallback here: "not found" is a
  # real answer, and the callers already treat a non-zero status as "I have to ask the user".
  local candidate=""

  while IFS= read -r candidate; do
    if [[ -d "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done < <(sts2_game_root_candidates)

  return 1
}

sts2_detect_app_manifest() {
  local library=""
  local candidate=""

  while IFS= read -r library; do
    candidate="$library/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf"
    if [[ -f "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done < <(sts2_steam_libraries)

  return 1
}

sts2_default_app_manifest() {
  # The name the existing callers know, with the contract they rely on: a path comes back even
  # when nothing is installed, so the caller that reads it decides how to fail. The manifest is
  # looked for in every library first, and the conventional location is named as the last resort.
  local found=""

  found="$(sts2_detect_app_manifest || true)"
  if [[ -n "$found" ]]; then
    printf '%s\n' "$found"
    return 0
  fi

  printf '%s\n' "$HOME/Library/Application Support/Steam/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf"
}

sts2_resolve_game_root() {
  # Argument, then variable, then detection, then the conventional path -- printed either way, so
  # a caller can decide whether the answer is good enough. This is the entry point the scripts
  # use; sts2_detect_game_root stays available for callers that want to know it was really found.
  local explicit="${1:-}"
  local detected=""

  if sts2_has_text "$explicit"; then
    printf '%s\n' "$explicit"
    return 0
  fi
  if sts2_has_text "${STS2_GAME_ROOT:-}"; then
    printf '%s\n' "$STS2_GAME_ROOT"
    return 0
  fi

  detected="$(sts2_detect_game_root || true)"
  if [[ -n "$detected" ]]; then
    printf '%s\n' "$detected"
    return 0
  fi

  printf '%s\n' "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
}

sts2_resolve_app_manifest() {
  local explicit="${1:-}"
  local detected=""

  if sts2_has_text "$explicit"; then
    printf '%s\n' "$explicit"
    return 0
  fi
  if sts2_has_text "${STS2_APP_MANIFEST:-}"; then
    printf '%s\n' "$STS2_APP_MANIFEST"
    return 0
  fi

  detected="$(sts2_detect_app_manifest || true)"
  if [[ -n "$detected" ]]; then
    printf '%s\n' "$detected"
    return 0
  fi

  printf '%s\n' "$HOME/Library/Application Support/Steam/steamapps/appmanifest_$STS2_DEFAULT_APP_ID.acf"
}
