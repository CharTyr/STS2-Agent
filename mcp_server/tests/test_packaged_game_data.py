"""Shallow integrity checks for the packaged game-data snapshot.

mcp_server/data/eng/*.json is a point-in-time capture of game metadata that ships inside
the package. It is not the runtime data source: the tools read each collection from the
mod over GET /data/{collection} (see data/README.md), and this snapshot's schema
intentionally differs from that export (for example potions.json has no usage or
target_type, and relics.json has no is_melted). No code reads these files, so nothing
else would notice if one were truncated or corrupted in a refactor.

The checks are therefore deliberately shallow: every file parses to a list or a mapping,
list entries carry a non-empty unique id, and mapping keys are non-empty and not
duplicated. This must not grow into a field-by-field schema check - the snapshot is not
supposed to match the export contract.

The directory is a known decision point (keep or delete the 1.2 MiB snapshot). When it is
absent or empty this class skips instead of failing, so that decision is not blocked by a
test with nothing left to check.
"""

from __future__ import annotations

import json
import unittest
from pathlib import Path

_ENG_RELATIVE = Path("data") / "eng"


def _find_eng_dir() -> Path | None:
    """Locate mcp_server/data/eng from this file, never from the process CWD."""
    candidate = Path(__file__).resolve().parents[1] / _ENG_RELATIVE
    return candidate if candidate.is_dir() else None


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    seen: set[str] = set()
    for key, _ in pairs:
        if key in seen:
            raise ValueError(f"duplicate mapping key {key!r}")
        seen.add(key)
    return dict(pairs)


def _load(path: Path) -> object:
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)


class PackagedGameDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        directory = _find_eng_dir()
        if directory is None:
            raise unittest.SkipTest(f"packaged game data directory is absent: {_ENG_RELATIVE}")
        cls.eng_dir = directory
        cls.json_files = sorted(directory.glob("*.json"))
        if not cls.json_files:
            raise unittest.SkipTest("packaged game data directory has no JSON files")

    def test_every_packaged_json_file_parses_to_a_list_or_mapping(self) -> None:
        broken: list[str] = []
        for path in self.json_files:
            try:
                payload = _load(path)
            except (OSError, ValueError) as exc:
                broken.append(f"{path.name}: not valid JSON/UTF-8 ({exc})")
                continue
            if not isinstance(payload, (list, dict)):
                broken.append(
                    f"{path.name}: expected a list or mapping, got {type(payload).__name__}"
                )

        self.assertEqual(
            [],
            broken,
            "Packaged game data files are unreadable:\n  - " + "\n  - ".join(broken),
        )

    def test_every_entry_has_a_unique_non_empty_id(self) -> None:
        violations: list[str] = []
        for path in self.json_files:
            try:
                payload = _load(path)
            except (OSError, ValueError):
                # Unreadable files are reported by the parse test, not here; keep this
                # loop going so one broken file cannot hide the other violations.
                continue
            if isinstance(payload, list):
                seen: list[str] = []
                for index, entry in enumerate(payload):
                    if not isinstance(entry, dict):
                        violations.append(f"{path.name}[{index}]: entry is not an object")
                        continue
                    item_id = entry.get("id")
                    if not isinstance(item_id, str) or not item_id.strip():
                        violations.append(f"{path.name}[{index}]: missing a non-empty string id")
                        continue
                    if item_id in seen:
                        violations.append(f"{path.name}[{index}]: duplicate id {item_id!r}")
                    seen.append(item_id)
            else:
                for key in payload:
                    if not isinstance(key, str) or not key.strip():
                        violations.append(f"{path.name}[{key!r}]: mapping key is empty")

        self.assertEqual(
            [],
            violations,
            "Packaged game data lost an id or a mapping key:\n  - " + "\n  - ".join(violations),
        )


if __name__ == "__main__":
    unittest.main()
