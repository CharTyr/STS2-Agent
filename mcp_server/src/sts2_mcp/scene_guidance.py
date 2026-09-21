"""Scene-scoped guidance for the current screen: the strategy rules, plus event option risk.

Answers one question — "what does this screen need to know" — so an external agent does not have to
read the whole strategy reference to find the one section that applies, and so the per-option event
risk index built offline in `docs/game-knowledge/events.md` is actually consumed by something.

Two sources, one contract:

- **strategy sections** come from `skills/sts2-mcp-player/references/strategy.md`, sliced by the
  screen-to-heading mapping. The mod embeds the same file and injects the same sections into its own
  loop, so this half is identical on both MCP surfaces.
- **event option risk** comes from the generated index, which lives in the repository and is not
  shipped inside the mod. This is the one place the two surfaces answer differently, and it is
  reported rather than hidden: the native tool returns strategy only.

The mapping here mirrors `STS2AIAgent/Agent/PlaybookSections.cs`, and
`tests/test_scene_guidance_alignment.py` keeps the two equal.
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any

# Mirrors PlaybookSections.MaxInjectedCharacters.
MAX_GUIDANCE_CHARACTERS = 2400

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
    if root is None:
        return None
    path = root / "skills" / "sts2-mcp-player" / "references" / "strategy.md"
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


def strategy_for_screen(markdown: str, screen: str | None) -> str:
    """The strategy sections this screen needs, or an empty string."""
    if not screen or not screen.strip():
        return ""
    wanted = SCREEN_HEADINGS.get(screen.strip())
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

    `guidance` is the same text the mod injects into its own loop; `event_options` is the extra the
    repository index can add, and is empty on every screen but `EVENT`.
    """
    root = root if root is not None else repo_root()
    screen = state.get("screen") if isinstance(state, dict) else None
    screen_text = screen.strip() if isinstance(screen, str) else None

    path = _strategy_path(root)
    markdown = path.read_text(encoding="utf-8") if path is not None else ""

    event_id = None
    if isinstance(state, dict):
        event = state.get("event")
        if isinstance(event, dict):
            candidate = event.get("event_id")
            event_id = candidate if isinstance(candidate, str) else None

    return {
        "screen": screen_text,
        "guidance": strategy_for_screen(markdown, screen_text),
        "event_id": event_id,
        "event_options": event_option_risk(root, event_id),
        "guidance_source": "strategy.md" if markdown else None,
    }
