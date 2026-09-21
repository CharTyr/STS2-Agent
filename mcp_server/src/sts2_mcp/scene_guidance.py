"""Scene-scoped guidance for the current screen: the strategy rules, the playbook, and event risk.

Answers one question — "what does this screen need to know" — so an external agent does not have to
read the whole strategy reference to find the one section that applies, and so the per-option event
risk index built offline in `docs/game-knowledge/events.md` is actually consumed by something.

Three sources, one contract:

- **strategy sections** come from `skills/sts2-mcp-player/references/strategy.md`, sliced by the
  screen-to-heading mapping. The mod embeds the same file and injects the same sections into its own
  loop, so this half is identical on both MCP surfaces.
- **playbook sections** come from `skills/sts2-mcp-player/references/screen-playbooks.md`, sliced by
  the same rule and never empty: a screen with no section of its own gets the index of the sections,
  because the playbook is how a screen is driven and "there is nothing to know" is the one answer it
  must not give. The mod embeds that file too and answers the same key.
- **event option risk** comes from the generated index, which lives in the repository and is not
  shipped inside the mod. This is the one place the two surfaces answer differently, and it is
  reported rather than hidden: the native tool returns the four shared keys only.

The mappings here mirror `STS2AIAgent/Agent/PlaybookSections.cs`, and
`tests/test_scene_guidance_alignment.py` keeps the two equal — including the set of keys both
surfaces promise (`SHARED_KEYS`).
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any

from .game_data import _detect_scene_from_screen

# Mirrors PlaybookSections.MaxInjectedCharacters.
MAX_GUIDANCE_CHARACTERS = 2400

# The keys every guidance answer carries, on both surfaces. The sidecar adds event_id,
# event_options, and guidance_source on top; those are the offline index it uniquely ships.
SHARED_KEYS: tuple[str, ...] = ("screen", "scene", "guidance", "playbook")

# Screen -> strategy headings, in injection order. Mirrors PlaybookSections.ScreenHeadings.
SCREEN_HEADINGS: dict[str, list[str]] = {
    "COMBAT": ["Combat: what to prioritise", "Potions: when to drink"],
    "MAP": ["Route: which node to enter"],
    "REST": ["Rest site: heal or upgrade"],
    "SHOP": ["Shop: what to buy"],
    "FAKE_MERCHANT": ["Shop: what to buy"],
    "EVENT": ["Event options: how to choose"],
}

# Headings deliberately never injected per screen, with the reason. Mirrors the C# declaration.
NOT_INJECTED_HEADINGS: dict[str, str] = {
    "Co-op: dividing the work": (
        "written for a client coordinating two instances; the in-game loop drives one local player"
    ),
    "Where these rules come from": "provenance note for a reader, not instruction for a decision",
}

# The heading the in-run pause pages share: they ride one container, so they share one exit rule.
# Mirrors PlaybookSections.InRunMenuPagesHeading.
IN_RUN_MENU_PAGES_HEADING = "In-Run Menu Pages (PAUSE_MENU, SETTINGS, COMPENDIUM, ...)"

# Screen -> playbook headings, in injection order. Mirrors PlaybookSections.Playbooks.
PLAYBOOK_HEADINGS: dict[str, list[str]] = {
    "MAIN_MENU": ["MAIN_MENU and Timeline"],
    "TIMELINE": ["MAIN_MENU and Timeline"],
    "CHARACTER_SELECT": ["CHARACTER_SELECT"],
    "BUNDLE_SELECTION": ["BUNDLE_SELECTION"],
    "CAPSTONE_SELECTION": ["CAPSTONE_SELECTION"],
    "MAP": ["MAP"],
    "COMBAT": ["COMBAT", "Potion Targeting"],
    "CARD_SELECTION": ["CARD_SELECTION"],
    "REWARD": ["REWARD"],
    "SHOP": ["SHOP"],
    "FAKE_MERCHANT": ["FAKE_MERCHANT", "SHOP"],
    "REST": ["REST"],
    "CHEST": ["CHEST"],
    "EVENT": ["EVENT"],
    "CRYSTAL_SPHERE": ["CRYSTAL_SPHERE"],
    "MODAL": ["MODAL, GAME_OVER, and UNLOCK"],
    "GAME_OVER": ["MODAL, GAME_OVER, and UNLOCK"],
    "UNLOCK": ["MODAL, GAME_OVER, and UNLOCK"],
    "PATCH_NOTES": ["PATCH_NOTES"],
    "CARDS_VIEW": ["CARD_INSPECT and RELIC_INSPECT"],
    "CARD_PILE": ["CARD_INSPECT and RELIC_INSPECT"],
    "CARD_INSPECT": ["CARD_INSPECT and RELIC_INSPECT"],
    "RELIC_INSPECT": ["CARD_INSPECT and RELIC_INSPECT"],
    "FEEDBACK": ["FEEDBACK"],
    "PAUSE_MENU": [IN_RUN_MENU_PAGES_HEADING],
    "SETTINGS": [IN_RUN_MENU_PAGES_HEADING],
    "COMPENDIUM": [IN_RUN_MENU_PAGES_HEADING],
    "CARD_LIBRARY": [IN_RUN_MENU_PAGES_HEADING],
    "RELIC_COLLECTION": [IN_RUN_MENU_PAGES_HEADING],
    "POTION_LAB": [IN_RUN_MENU_PAGES_HEADING],
    "BESTIARY": [IN_RUN_MENU_PAGES_HEADING],
    "STATS": [IN_RUN_MENU_PAGES_HEADING],
    "RUN_HISTORY": [IN_RUN_MENU_PAGES_HEADING],
}

# Screens the game can report that the playbook deliberately has no section for, and which therefore
# receive the index fallback. Mirrors PlaybookSections.PlaybookIndexFallbackScreens.
PLAYBOOK_INDEX_FALLBACK_SCREENS: dict[str, str] = {
    "UNKNOWN": "the resolver's own name for a screen it could not identify; there is nothing to look up",
    "MULTIPLAYER_LOBBY": "the lobby is driven by the shared contract's routing rules, not by an action sequence",
    "MULTIPLAYER_LOAD": "a loading transition, not a screen the agent can act on",
}

_RUNS_LEVEL_HEADINGS: tuple[str, ...] = ()

_SECTION = re.compile(r"^## (.+)$", re.MULTILINE)
# One row of the generated `Option Risk Details` table:
# | AbyssalBaths | INITIAL.options.IMMERSE | Immerse | Gain 2 Max HP | Lose 3 HP; ... | lethal-possible | next page |
_RISK_ROW = re.compile(
    r"^\|\s*([A-Za-z0-9_]+)\s*\|\s*([^|]+?)\s*\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|\s*$",
    re.MULTILINE,
)


def repo_root() -> Path | None:
    """The checkout this package is running from, or None when it was installed elsewhere."""
    candidate = Path(__file__).resolve().parents[3]
    if (candidate / "docs" / "game-knowledge").is_dir():
        return candidate
    return None


def _strategy_path(root: Path | None) -> Path | None:
    return _reference_path(root, "strategy.md")


def _playbook_path(root: Path | None) -> Path | None:
    return _reference_path(root, "screen-playbooks.md")


def _reference_path(root: Path | None, name: str) -> Path | None:
    if root is None:
        return None
    path = root / "skills" / "sts2-mcp-player" / "references" / name
    return path if path.is_file() else None


def headings(markdown: str) -> list[str]:
    return [match.strip() for match in _SECTION.findall(markdown)]


def sections(markdown: str) -> dict[str, str]:
    """Heading -> body for every `##` section."""
    parts = _SECTION.split(markdown)
    # split() yields [preamble, heading1, body1, heading2, body2, ...]
    result: dict[str, str] = {}
    for index in range(1, len(parts) - 1, 2):
        result[parts[index].strip()] = parts[index + 1].strip()
    return result


def cap(text: str, limit: int = MAX_GUIDANCE_CHARACTERS) -> str:
    """Cut at a paragraph boundary so a capped answer never ends mid-sentence."""
    if len(text) <= limit:
        return text
    window = text[:limit]
    last_break = window.rfind("\n\n")
    return (window[:last_break] if last_break > 0 else window).rstrip()


def _slice(markdown: str, mapping: dict[str, list[str]], screen: str | None) -> str:
    """The listed sections for one screen, or an empty string when it has none.

    Shared by both references so the two cannot drift on the mechanics that matter: the headings are
    injected in mapping order, a heading that no longer exists in the Markdown is skipped rather than
    silently truncated, and the result is capped at a paragraph boundary.
    """
    if not screen or not screen.strip():
        return ""
    wanted = mapping.get(screen.strip())
    if not wanted:
        return ""

    available = sections(markdown)
    pieces = []
    for heading in wanted:
        body = available.get(heading)
        if body is None:
            # A renamed heading must not silently remove the guidance.
            continue
        pieces.append("## " + heading + "\n\n" + body)

    if not pieces:
        return ""
    return cap("\n\n".join(pieces))


def strategy_for_screen(markdown: str, screen: str | None) -> str:
    """The strategy sections this screen needs, or an empty string.

    Empty is the honest answer here: only a few screens have a real strategic choice, and a reward
    screen is not made better by being handed the route rules.
    """
    return _slice(markdown, SCREEN_HEADINGS, screen)


def playbook_for_screen(markdown: str, screen: str | None) -> str:
    """How to drive this screen, never empty.

    A screen the mapping does not name gets the index of the sections instead. The playbook is what
    turns "the state shows a screen" into "here is the action sequence", so an empty answer would
    read as "there is nothing to know here" rather than "this mapping missed the screen".
    """
    sliced = _slice(markdown, PLAYBOOK_HEADINGS, screen)
    return sliced if sliced else playbook_index(markdown, screen)


def playbook_index(markdown: str, screen: str | None) -> str:
    """The list of sections a screen that has none of its own could read, capped like a slice."""
    label = screen.strip() if isinstance(screen, str) and screen.strip() else "unknown"
    lines = [
        f"No section of this playbook matches the current screen ({label}). The sections it carries are:"
    ]
    lines.extend(f"- {heading}" for heading in headings(markdown))
    lines.append(
        "Drive this screen from the shared play contract and the live state, and re-read state after "
        "every action instead of assuming an action sequence."
    )
    return cap("\n".join(lines))


def event_option_risk(root: Path | None, event_id: str | None) -> list[dict[str, str]]:
    """The generated risk rows for one event, in source order.

    The rows come from the offline index, which grades each option from the decompiled handler. They
    are returned in the order the event builds them, so a caller can line them up with the live
    `event.options[]`; nothing here guesses which live index a row belongs to, because the live
    payload and the source can disagree about order and a wrong pairing would be worse than none.
    """
    if root is None or not event_id:
        return []

    path = root / "docs" / "game-knowledge" / "events.md"
    if not path.is_file():
        return []

    rows: list[dict[str, str]] = []
    for match in _RISK_ROW.finditer(path.read_text(encoding="utf-8")):
        name, option, handler, effect, cost, risk, continuation = match.groups()
        if name.strip() != event_id.strip():
            continue
        rows.append(
            {
                "option": option.strip(),
                "handler": handler.strip(),
                "effect": effect.strip(),
                "cost": cost.strip(),
                "risk": risk.strip(),
                "continuation": continuation.strip(),
            }
        )
    return rows


def scene_guidance(state: Any, root: Path | None = None) -> dict[str, Any]:
    """The guidance block for one `/state` payload.

    ``screen``, ``scene``, ``guidance``, and ``playbook`` are the four keys both MCP surfaces always
    answer with; the native tool stops there. ``event_id``, ``event_options``, and
    ``guidance_source`` are the extras only this side can add, because they come from the generated
    index in the repository that the mod does not ship.

    ``guidance`` is the strategy text the mod injects into its own loop and is empty on a screen with
    no strategic choice; ``playbook`` is the per-screen action sequence and is never empty.
    """
    root = root if root is not None else repo_root()
    screen = state.get("screen") if isinstance(state, dict) else None
    screen_text = screen.strip() if isinstance(screen, str) else None

    strategy_markdown = _read_reference(_strategy_path(root))
    playbook_markdown = _read_reference(_playbook_path(root))

    event_id = None
    if isinstance(state, dict):
        event = state.get("event")
        if isinstance(event, dict):
            candidate = event.get("event_id")
            event_id = candidate if isinstance(candidate, str) else None

    return {
        "screen": screen_text,
        "scene": _detect_scene_from_screen(screen_text or ""),
        "guidance": strategy_for_screen(strategy_markdown, screen_text),
        "playbook": playbook_for_screen(playbook_markdown, screen_text),
        "event_id": event_id,
        "event_options": event_option_risk(root, event_id),
        "guidance_source": "strategy.md" if strategy_markdown else None,
    }


def _read_reference(path: Path | None) -> str:
    return path.read_text(encoding="utf-8") if path is not None else ""
