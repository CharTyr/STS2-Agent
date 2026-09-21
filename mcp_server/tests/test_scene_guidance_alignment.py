"""Cross-language contract: the C# and Python "screen -> strategy headings" tables.

The same mapping is written twice: once in `STS2AIAgent/Agent/PlaybookSections.cs` for the in-game
loop and the native MCP surface, once in `mcp_server/src/sts2_mcp/scene_guidance.py` for the
sidecar. Nothing compared the two, and the failure mode is quiet in both directions: a screen added
to one side only means the same `get_scene_guidance` call answers differently depending on which
surface served it, and a heading mapped on one side only means one surface silently injects nothing.

C# is the reference side, as it is for the scene field sets: when the two disagree, Python follows
C#. The comparison reads both as source text, so no game and no C# build are needed.

The same file also pins the *result* contract both surfaces answer with: the four shared keys and the
playbook slice that is never empty.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from sts2_mcp.scene_guidance import (
    MAX_GUIDANCE_CHARACTERS,
    NOT_INJECTED_HEADINGS,
    PLAYBOOK_HEADINGS,
    PLAYBOOK_INDEX_FALLBACK_SCREENS,
    SCREEN_HEADINGS,
    SHARED_KEYS,
    playbook_for_screen,
    scene_guidance,
)

_CSHARP_SECTIONS = "STS2AIAgent/Agent/PlaybookSections.cs"
_NATIVE_TOOLS = "STS2AIAgent/Server/NativeMcpServer.Tools.cs"
_PLAYBOOKS_MD = "skills/sts2-mcp-player/references/screen-playbooks.md"

# ["COMBAT"] = new[] { "Combat: what to prioritise", "Potions: when to drink" },
_SCREEN_ENTRY = re.compile(r'\["([A-Za-z_]+)"\]\s*=\s*new\[\]\s*\{([^}]*)\}')
_QUOTED = re.compile(r'"([^"]*)"')
# ["PAUSE_MENU"] = new[] { InRunMenuPagesHeading },
_ENTRY_TOKEN = re.compile(r'"([^"]*)"|([A-Za-z_][A-Za-z0-9_]*)')
# private const string InRunMenuPagesHeading = "In-Run Menu Pages (PAUSE_MENU, ...)";
_CONST = re.compile(r'private\s+const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"([^"]*)"')

# public const int MaxInjectedCharacters = 2400;
_MAX_CHARACTERS = re.compile(r"MaxInjectedCharacters\s*=\s*(\d+)")

# The NotInjectedHeadings dictionary: ["Co-op: dividing the work"] = "..." entries.
_NOT_INJECTED_ENTRY = re.compile(r'\["([^"]+)"\]\s*=')
# The fallback dictionary: ["UNKNOWN"] = "the resolver's own name ...",
_FALLBACK_ENTRY = re.compile(r'\["([A-Z_]+)"\]\s*=\s*\n?\s*"([^"]*)"')

# The guidance object both surfaces build. NativeMcpServer.Tools.cs shapes it once:
#     private static object BuildSceneGuidance(string? screen) => new { screen, scene = ..., ... };
_BUILD_GUIDANCE = re.compile(
    r"private\s+static\s+object\s+BuildSceneGuidance\([^)]*\)\s*=>\s*new\s*\{(.*?)\};",
    re.DOTALL,
)
_GUIDANCE_KEY = re.compile(r"(?:^|,)\s*([a-z_][a-z0-9_]*)\s*(?==|,|$)")


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


def _csharp_playbook_headings() -> dict[str, list[str]]:
    """The `Playbooks` table, with its shared heading constant resolved.

    The in-run menu pages ride one container, so their entry names a `const` instead of repeating the
    string eight times. A parser that only reads literals would see eight screens mapped to nothing,
    which reads exactly like the mapping having been deleted.
    """
    source = _csharp_source()
    constants = dict(_CONST.findall(source))

    start = source.index("Playbooks { get; } = new(")
    end = source.index("};", start)
    table = source[start:end]

    entries: dict[str, list[str]] = {}
    for match in _SCREEN_ENTRY.finditer(table):
        headings: list[str] = []
        for quoted, identifier in _ENTRY_TOKEN.findall(match.group(2)):
            if quoted:
                headings.append(quoted)
            elif identifier in constants:
                headings.append(constants[identifier])
            else:
                raise AssertionError(
                    f"Playbooks entry {match.group(1)} names {identifier!r}, which is not a const in "
                    "PlaybookSections.cs; the Python side has no way to resolve it"
                )
        entries[match.group(1)] = headings

    if len(entries) < 25:
        raise AssertionError(f"Playbooks parse looks wrong: {sorted(entries)}")
    return entries


def _csharp_fallback_screens() -> set[str]:
    source = _csharp_source()
    start = source.index("PlaybookIndexFallbackScreens { get; }")
    end = source.index("};", start)
    return {name for name, _ in _FALLBACK_ENTRY.findall(source[start:end])}


def _csharp_guidance_keys() -> set[str]:
    """The keys the native guidance object is built from.

    Line comments are stripped first: the builder documents why the playbook is never empty, and a
    comment between two properties otherwise hides the property behind it from the key pattern.
    """
    source = (_find_source_root() / _NATIVE_TOOLS).read_text(encoding="utf-8")
    match = _BUILD_GUIDANCE.search(source)
    if match is None:
        raise AssertionError("NativeMcpServer.Tools.cs no longer has a BuildSceneGuidance builder")
    body = re.sub(r"//[^\n]*", "", match.group(1))
    keys = {name for name in _GUIDANCE_KEY.findall(body) if name}
    if not keys:
        raise AssertionError("BuildSceneGuidance parse produced no keys")
    return keys


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

    def test_playbook_mapping_is_identical(self) -> None:
        csharp = _csharp_playbook_headings()

        only_csharp = sorted(set(csharp) - set(PLAYBOOK_HEADINGS))
        self.assertEqual(
            [],
            only_csharp,
            "screens whose playbook the C# mapping injects and the Python mapping does not: "
            + ", ".join(only_csharp),
        )

        only_python = sorted(set(PLAYBOOK_HEADINGS) - set(csharp))
        self.assertEqual(
            [],
            only_python,
            "screens whose playbook the Python mapping injects and the C# mapping does not: "
            + ", ".join(only_python),
        )

        mismatched = {
            screen: {"csharp": csharp[screen], "python": PLAYBOOK_HEADINGS[screen]}
            for screen in sorted(set(csharp) & set(PLAYBOOK_HEADINGS))
            if csharp[screen] != PLAYBOOK_HEADINGS[screen]
        }
        self.assertEqual(
            {},
            mismatched,
            f"the two sides slice different playbook sections per screen: {mismatched}",
        )

    def test_playbook_fallback_screens_are_identical(self) -> None:
        self.assertEqual(
            sorted(_csharp_fallback_screens()),
            sorted(PLAYBOOK_INDEX_FALLBACK_SCREENS),
            "the screens declared as getting the playbook index differ between the two surfaces",
        )

    def test_every_mapped_playbook_heading_exists(self) -> None:
        markdown = (_find_source_root() / _PLAYBOOKS_MD).read_text(encoding="utf-8")
        available = {match.strip() for match in re.findall(r"^## (.+)$", markdown, re.MULTILINE)}

        for screen, wanted in PLAYBOOK_HEADINGS.items():
            for heading in wanted:
                with self.subTest(screen=screen, heading=heading):
                    self.assertIn(
                        heading,
                        available,
                        f"the mapping names {heading!r}, which is not a heading in screen-playbooks.md",
                    )

    def test_both_surfaces_answer_the_shared_keys(self) -> None:
        python_keys = set(scene_guidance({"screen": "COMBAT"}, root=None))
        self.assertTrue(
            set(SHARED_KEYS).issubset(python_keys),
            "the Python guidance answer is missing a shared key: "
            + ", ".join(sorted(set(SHARED_KEYS) - python_keys)),
        )

        native_keys = _csharp_guidance_keys()
        self.assertTrue(
            set(SHARED_KEYS).issubset(native_keys),
            "the native guidance object is missing a shared key: "
            + ", ".join(sorted(set(SHARED_KEYS) - native_keys)),
        )

    def test_the_playbook_answer_is_never_empty(self) -> None:
        markdown = (_find_source_root() / _PLAYBOOKS_MD).read_text(encoding="utf-8")

        for screen in ("COMBAT", "SHOP", "MAIN_MENU", "NOT_A_SCREEN", "UNKNOWN", None, "  "):
            with self.subTest(screen=screen):
                self.assertTrue(
                    playbook_for_screen(markdown, screen).strip(),
                    "an empty playbook answer reads as 'there is nothing to know here'",
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
