"""Cross-language contract: the C# and Python "action -> payload path" tables.

`ActIndexValidator.OptionPaths` says where an action's index lives in the compact state, and the
same fact is written again in `sts2_mcp/action_results.py` as `INDEX_PATHS_BY_ACTION`, where it
becomes the `valid_field` a rejected index reports. Nothing compared the two, and the failure mode is
quiet in both directions: an action mapped on one side only means the two surfaces disagree about
what a client should re-read after a rejection, and a path that drifted means one surface sends the
model to a field the other never looks at.

C# is the reference side, as it is for the scene field sets and the screen-to-heading tables: when
the two disagree, Python follows C#. The comparison reads both as source text, so no game and no C#
build are needed.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from sts2_mcp.action_results import INDEX_FIELDS, INDEX_PATHS_BY_ACTION

_VALIDATOR = "STS2AIAgent/Agent/ActIndexValidator.cs"

# ["play_card"] = new[] { new[] { "combat", "hand" } },
_OPTION_ENTRY = re.compile(r'^\s*\["([a-z_]+)"\]\s*=\s*new\[\]\s*\{(.*)$', re.MULTILINE)
# The first path of the entry. Most are two segments; `choose_bundle` addresses a top-level array.
_FIRST_PATH = re.compile(r'\{\s*"([^"]+)"\s*(?:,\s*"([^"]+)")?\s*\}')


def _find_source_root() -> Path:
    for candidate in (Path(__file__).resolve().parents[2], Path.cwd()):
        if (candidate / _VALIDATOR).is_file():
            return candidate
    raise AssertionError(f"Could not locate {_VALIDATOR} from the test's working directory")


def _csharp_option_paths() -> dict[str, str]:
    """The first payload path of every `OptionPaths` entry, as a dotted name."""
    source = (_find_source_root() / _VALIDATOR).read_text(encoding="utf-8")
    start = source.index("OptionPaths = new")
    end = source.index("};", start)
    table = source[start:end]

    entries: dict[str, str] = {}
    for match in _OPTION_ENTRY.finditer(table):
        path = _FIRST_PATH.search(match.group(2))
        if path is None:
            raise AssertionError(
                f"OptionPaths entry {match.group(1)} has no readable first path: {match.group(2)}"
            )
        entries[match.group(1)] = ".".join(segment for segment in path.groups() if segment)

    if len(entries) < 15:
        raise AssertionError(f"OptionPaths parse looks wrong: {sorted(entries)}")
    return entries


class IndexErrorAlignmentTests(unittest.TestCase):
    def test_action_paths_are_identical(self) -> None:
        csharp = _csharp_option_paths()

        only_csharp = sorted(set(csharp) - set(INDEX_PATHS_BY_ACTION))
        self.assertEqual(
            [],
            only_csharp,
            "actions whose index path is known to the C# validator and not to the Python surface: "
            + ", ".join(only_csharp),
        )

        only_python = sorted(set(INDEX_PATHS_BY_ACTION) - set(csharp))
        self.assertEqual(
            [],
            only_python,
            "actions the Python surface maps to a payload path the C# validator never reads: "
            + ", ".join(only_python),
        )

        mismatched = {
            action: {"csharp": csharp[action], "python": INDEX_PATHS_BY_ACTION[action]}
            for action in sorted(set(csharp) & set(INDEX_PATHS_BY_ACTION))
            if csharp[action] != INDEX_PATHS_BY_ACTION[action]
        }
        self.assertEqual(
            {},
            mismatched,
            "the two sides send a rejected index to different payload paths: " + str(mismatched),
        )

    def test_play_card_is_the_card_index_action(self) -> None:
        # The one action whose index is not `option_index`; both sides have to agree, or the wrong
        # field is named in every combat rejection.
        self.assertEqual("combat.hand", INDEX_PATHS_BY_ACTION["play_card"])
        self.assertIn("card_index", INDEX_FIELDS)


if __name__ == "__main__":
    unittest.main()
