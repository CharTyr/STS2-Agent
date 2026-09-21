"""Cross-language contract: the C# and Python "screen -> strategy headings" tables.

The same mapping is written twice: once in `STS2AIAgent/Agent/PlaybookSections.cs` for the in-game
loop and the native MCP surface, once in `mcp_server/src/sts2_mcp/scene_guidance.py` for the
sidecar. Nothing compared the two, and the failure mode is quiet in both directions: a screen added
to one side only means the same `get_scene_guidance` call answers differently depending on which
surface served it, and a heading mapped on one side only means one surface silently injects nothing.

C# is the reference side, as it is for the scene field sets: when the two disagree, Python follows
C#. The comparison reads both as source text, so no game and no C# build are needed.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from sts2_mcp.scene_guidance import (
    MAX_GUIDANCE_CHARACTERS,
    NOT_INJECTED_HEADINGS,
    SCREEN_HEADINGS,
)

_CSHARP_SECTIONS = "STS2AIAgent/Agent/PlaybookSections.cs"

# ["COMBAT"] = new[] { "Combat: what to prioritise", "Potions: when to drink" },
_SCREEN_ENTRY = re.compile(r'\["([A-Za-z_]+)"\]\s*=\s*new\[\]\s*\{([^}]*)\}')
_QUOTED = re.compile(r'"([^"]*)"')

# public const int MaxInjectedCharacters = 2400;
_MAX_CHARACTERS = re.compile(r"MaxInjectedCharacters\s*=\s*(\d+)")

# The NotInjectedHeadings dictionary: ["Co-op: dividing the work"] = "..." entries.
_NOT_INJECTED_ENTRY = re.compile(r'\["([^"]+)"\]\s*=')


def _find_source_root() -> Path:
    for candidate in (Path(__file__).resolve().parents[2], Path.cwd()):
        if (candidate / _CSHARP_SECTIONS).is_file():
            return candidate
    raise AssertionError(f"Could not locate {_CSHARP_SECTIONS} from the test's working directory")


def _csharp_source() -> str:
    return (_find_source_root() / _CSHARP_SECTIONS).read_text(encoding="utf-8")


def _csharp_screen_headings() -> dict[str, list[str]]:
    """The `ScreenHeadings` table, as screen -> headings, reading only that initializer."""
    source = _csharp_source()
    start = source.index("ScreenHeadings = new")
    end = source.index("};", start)
    table = source[start:end]

    entries: dict[str, list[str]] = {}
    for match in _SCREEN_ENTRY.finditer(table):
        entries[match.group(1)] = _QUOTED.findall(match.group(2))
    if len(entries) < 4:
        raise AssertionError(f"ScreenHeadings parse looks wrong: {sorted(entries)}")
    return entries


def _csharp_not_injected() -> set[str]:
    source = _csharp_source()
    start = source.index("NotInjectedHeadings = new")
    end = source.index("};", start)
    return set(_NOT_INJECTED_ENTRY.findall(source[start:end]))


class SceneGuidanceAlignmentTests(unittest.TestCase):
    def test_screen_to_heading_mapping_is_identical(self) -> None:
        csharp = _csharp_screen_headings()

        only_csharp = sorted(set(csharp) - set(SCREEN_HEADINGS))
        self.assertEqual(
            [],
            only_csharp,
            "screens the C# mapping injects guidance for and the Python mapping does not: "
            + ", ".join(only_csharp),
        )

        only_python = sorted(set(SCREEN_HEADINGS) - set(csharp))
        self.assertEqual(
            [],
            only_python,
            "screens the Python mapping injects guidance for and the C# mapping does not: "
            + ", ".join(only_python),
        )

        mismatched = {
            screen: {"csharp": csharp[screen], "python": SCREEN_HEADINGS[screen]}
            for screen in sorted(set(csharp) & set(SCREEN_HEADINGS))
            if csharp[screen] != SCREEN_HEADINGS[screen]
        }
        self.assertEqual(
            {},
            mismatched,
            "the two sides map these screens to different headings, so the same tool answers "
            f"differently per surface: {mismatched}",
        )

    def test_deliberate_omissions_match(self) -> None:
        self.assertEqual(
            sorted(_csharp_not_injected()),
            sorted(NOT_INJECTED_HEADINGS),
            "the headings declared as deliberately not injected differ between the two sides",
        )

    def test_the_character_cap_matches(self) -> None:
        match = _MAX_CHARACTERS.search(_csharp_source())
        self.assertIsNotNone(match, "PlaybookSections.MaxInjectedCharacters is no longer declared")
        self.assertEqual(
            int(match.group(1)),
            MAX_GUIDANCE_CHARACTERS,
            "the two sides cap the injected guidance at different lengths",
        )


if __name__ == "__main__":
    unittest.main()
