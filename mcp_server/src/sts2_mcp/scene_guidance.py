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

**The event-index join is canonical, not case-sensitive.** A live event arrives as the model's id
entry (`NEOW`) and a live option as the full localization key
(`NEOW.pages.INITIAL.options.ARCANE_SCROLL`), while the generator prints a readable class name
(`Neow`) and the page-relative key (`INITIAL.options.ARCANE_SCROLL`). `canonical_event_id` /
`canonical_option_key` are the one definition of that join, and they are the game's own algorithm:
`StringHelper.Slugify` (`([A-Za-z0-9]|\G(?!^))([A-Z])` -> `$1_$2`, uppercased, non-`[A-Z0-9_]`
dropped), mirrored in PowerShell as `ConvertTo-Slug` and here as `slugify`. Nothing fuzzy is used —
no prefix, substring, or similarity match — so two distinct events can never be merged into one
answer; an id the index does not carry returns no rows rather than a near miss.
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

# The event ids and keys are joined through one canonical form. INDEX_SCHEME_VERSION moves whenever
# the shape of `docs/game-knowledge/events.md` changes, so a stale checkout is reported instead of
# silently producing empty answers.
INDEX_SCHEME_VERSION = 2

# The game's own slug algorithm (StringHelper.Slugify in the decompiled sources):
# `([A-Za-z0-9]|\G(?!^))([A-Z])` -> `$1_$2`, uppercased, whitespace -> `_`, `[^A-Z0-9_]` dropped.
# The boundary pass is a scan rather than a regex because Python's `re` has no `\G` anchor, and
# `\G(?!^)` is load-bearing: it is a *zero-width* match that parts consecutive capitals, which is
# why the game's own `Slugify("ABC")` is `A_B_C`. A pattern like `([A-Za-z0-9])([A-Z])` would give
# `AB_C` and silently disagree with the ids the game reports. `_insert_camel_boundaries` reproduces
# the two rules below and is proven against the real .NET output in the tests.
_ALNUM = frozenset("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
_UPPER = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZ")
# The alphabet `StringHelper.SpecialCharRegex` (`[^A-Z0-9_]`) leaves behind, i.e. the shape of an id
# the game can report. An id already in it is a fixed point of `canonical_event_id`.
_ID_ALPHABET = re.compile(r"[A-Z0-9_]+")
_WHITESPACE = re.compile(r"\s+")
_SPECIAL_CHARS = re.compile(r"[^A-Z0-9_]")

# `docs/game-knowledge/events.md` heads each table with a metadata row, so the columns behind this
# row are the ones to read by position: Event | Option | Handler | Effect | Cost | Risk | Continuation.
_EVENT_RISK_HEADERS = ("Event", "Option", "Handler", "Effect", "Cost", "Risk", "Continuation")
# A table row: the leading and trailing pipe are the row's own, so the cells are everything between
# them. `ConvertTo-MarkdownTable` writes exactly one leading and one trailing pipe and no padding.
_TABLE_ROW = re.compile(r"^\|(?P<cells>.*)\|$", re.MULTILINE)
_TABLE_SEPARATOR_CELL = re.compile(r"^:?-{2,}:?$")
# The "Event Index" table's second column: a single model class name (`EventModel`,
# `AncientEventModel`), or the literal `Custom` a couple of events declare as their base.
_INDEX_TABLE_BASE_TYPE = re.compile(r"[A-Za-z_]\w*Model|Custom")


def repo_root() -> Path | None:
    """The checkout this package is running from, or None when it was installed elsewhere."""
    candidate = Path(__file__).resolve().parents[3]
    if (candidate / "docs" / "game-knowledge").is_dir():
        return candidate
    return None


def slugify(value: str | None) -> str:
    """The game's `StringHelper.Slugify`, ported exactly: CamelCase to the upper-case id form.

    This is what `ModelId`/`ModelDb.GetEntry` run over a model class name to make the entry a live
    payload reports, and what `EventModel.OptionKey` runs over an event's own type name to spell the
    first segment of every option key — so `Neow` -> `NEOW`,
    `DoorsOfLightAndDark` -> `DOORS_OF_LIGHT_AND_DARK`, `PunchOff` -> `PUNCH_OFF`.

    It is faithfully *not* idempotent, because the game's is not: a run of capitals is parted, so
    `Slugify("NEOW")` is `N_EO_W`. That is a property of the real function, and the tests pin it
    against the observed .NET output. Callers that need to compare an already-slugged value with a
    class name must go through :func:`canonical_event_id`, which is the idempotent form built on
    top of this one; feeding one side's output back into `slugify` is the bug this distinction
    exists to prevent.
    """
    if not value or not value.strip():
        return ""
    snake = _insert_camel_boundaries(value.strip())
    spaced = _WHITESPACE.sub("_", snake.upper())
    return _SPECIAL_CHARS.sub("", spaced)


def canonical_event_id(event_id: str | None) -> str:
    """An event id in the one form both sides are compared by.

    Its defining property is that it is a *fixed point*: canonicalising an already-canonical id
    returns it unchanged, so the generated index and a live payload can each canonicalise their own
    spelling and compare equal. The live payload reports `NEOW` (the model's id entry); the
    generator prints `Neow` (the class name). Both canonicalise to `NEOW`.

    `slugify` alone cannot do this, because the game's slug algorithm re-parts a value that is
    already an id: `slugify("NEOW")` is `N_EO_W`, which would no longer match the live payload it
    came from. So a value already in the id alphabet (`[A-Z0-9_]` only — the alphabet
    `StringHelper.SpecialCharRegex` leaves behind) is taken as already canonical and returned
    unchanged; anything else goes through the slug pass, which is what turns a class name like
    `Neow` into that alphabet. Comparisons use `casefold`, so a difference in case alone — and only
    that — is ignored, while two ids that differ by an underscore stay two different ids.
    """
    if not event_id or not event_id.strip():
        return ""
    stripped = event_id.strip()
    if _ID_ALPHABET.fullmatch(stripped):
        return stripped.upper()
    return slugify(stripped).upper()


def canonical_option_key(text_key: str | None, event_id: str | None = None) -> str:
    """An option key in the page-relative form the index's `Option` column is keyed by.

    The live payload carries the full localization key
    (`NEOW.pages.INITIAL.options.ARCANE_SCROLL`) and the index carries it without the leading
    `<EVENT>.pages.` segment (`INITIAL.options.ARCANE_SCROLL`), so the join strips that segment —
    and only that segment. The event's canonical id must be followed by `.pages.` for the prefix to
    be removed, which is what keeps an option literally named after its event intact.

    What is left is *not* re-slugged. `EventModel.OptionKey` builds the key as
    `$"{Slugify(GetType().Name)}.pages.{pageName}.options.{optionName}"` — only the event segment
    goes through the slug algorithm; the page and option names are written verbatim by each event's
    own `RelicOption<T>("INITIAL", ...)` / `InitialOptionKey("...")` call. Re-slugging them here
    would rewrite the source's own spelling (`ARCANE_SCROLL` -> `A_R_C_A_N_E_S_C_R_O_L_L`) and turn
    an exact join into a fuzzy one, which is the failure this seam exists to avoid.
    """
    if not text_key or not text_key.strip():
        return ""
    return _strip_event_prefix(text_key.strip(), event_id)


def canonical_option_text_key(event_id: str | None, option_key: str | None) -> str:
    """The full canonical localization key for one event plus one page-relative option key.

    The inverse of :func:`canonical_option_key`, and the form the live payload reports:
    `("NEOW", "INITIAL.options.ARCANE_SCROLL")` -> `NEOW.pages.INITIAL.options.ARCANE_SCROLL`.
    """
    canonical = canonical_event_id(event_id)
    key = canonical_option_key(option_key)
    if not canonical or not key:
        return ""
    return f"{canonical}.pages.{key}"


def _strip_event_prefix(key: str, event_id: str | None) -> str:
    """Drop a leading `<EVENT>.pages.` segment, or return the key unchanged.

    Only the first dot-separated segment is slugified, because only that segment is produced by the
    slug algorithm and only that segment can be spelled differently by the two sides (`NEOW` in a
    live payload, `Neow` in the generated index). Every later segment is kept verbatim. The
    comparison is on the canonical forms, so a live key and a generated row agree whichever of the
    two spellings each side carries.
    """
    canonical = canonical_event_id(event_id).casefold()
    if not canonical:
        return key
    head, separator, tail = key.partition(".")
    if separator and canonical_event_id(head).casefold() == canonical and tail.startswith("pages."):
        return tail[len("pages."):]
    return key


def _insert_camel_boundaries(value: str) -> str:
    """The `([A-Za-z0-9]|\\G(?!^))([A-Z])` -> `$1_$2` pass, without the .NET-only `\\G` anchor.

    The pattern is applied as one left-to-right `.NET Regex.Replace` scan, and `\\G` is what makes it
    more than the usual CamelCase split. There are two rules, and both were read off the real
    `StringHelper.Slugify`, not guessed (see `tests/test_scene_guidance.py`):

    1. an alphanumeric immediately followed by an upper-case letter gains an underscore — the
       ordinary `AbyssalBaths` -> `Abyssal_Baths` boundary; and
    2. `\\G(?!^)` matches zero-width at the end of the previous match, but not at the string start,
       before an upper-case letter. A match that starts on the previous match's end position
       consumes only its upper-case letter, so the scan resumes on that same position — which is why
       a run of capitals is parted: `ABC` -> `A_B_C`, `NEOW` -> `N_EO_W`, `ABc` -> `A_BC`.

    A punctuation character ends rule 2: after the match that ends on it, the next position is not
    the end of the match, so the following segment starts fresh — `INITIAL.options` is one pass
    (`_` only where an alphanumeric precedes a capital), and the punctuation itself is never
    rewritten. Because it ends a match, punctuation needs no special case here: a match that stops
    just before a non-alphanumeric never sets up the zero-width rule across it.
    """
    out: list[str] = []
    for index, char in enumerate(value):
        following = value[index + 1] if index + 1 < len(value) else ""
        if char in _ALNUM and following in _UPPER:
            out.append(char)
            out.append("_")
            continue
        if char in _UPPER and following in _UPPER:
            # The zero-width `\\G` rule: a capital directly before another capital is a boundary.
            out.append(char)
            out.append("_")
            continue
        out.append(char)
    return "".join(out)


def _table_rows(markdown: str) -> list[list[str]]:
    """Every generated-table row, with Markdown escapes inverted and whitespace trimmed.

    The header and its `---` separator row are skipped. Rows are split on unescaped `|` only, so a
    cell that itself contains a pipe (the generator writes `\\|`) stays one cell.
    """
    rows: list[list[str]] = []
    for match in _TABLE_ROW.finditer(markdown):
        cells = [cell.replace("\\|", "|").strip() for cell in match.group("cells").split("|")]
        if len(cells) != len(_EVENT_RISK_HEADERS):
            continue
        if cells == list(_EVENT_RISK_HEADERS):
            continue
        if all(_TABLE_SEPARATOR_CELL.match(cell) for cell in cells):
            continue
        rows.append(cells)
    return rows


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


def parse_event_option_index(markdown: str) -> tuple[dict[str, tuple[dict[str, str], ...]], frozenset[str]]:
    """Parse the generated `Option Risk Details` table into canonical event id -> option rows.

    Two outputs, because the join needs both halves: the rows, keyed by the *canonical* event id
    rather than the class name the file displays, and the set of canonical ids whose rows could not
    be attributed safely (see :func:`_ambiguous_canonical_ids`). A malformed file raises rather than
    returning an empty index, so a generator change that breaks parsing is reported as breakage
    instead of answering "this event has no options" — the one wrong answer this module must not
    give.
    """
    rows: dict[str, list[dict[str, str]]] = {}
    canonical_to_displayed: dict[str, set[str]] = {}
    seen: set[tuple[str, tuple[str, ...]]] = set()

    for cells in _table_rows(markdown):
        displayed, option, handler, effect, cost, risk, continuation = cells
        canonical = canonical_event_id(displayed)
        if not canonical:
            continue
        # The generator already collapses an option it builds more than once, but a repeated row is
        # deduplicated here too: two byte-identical rows are one option, and returning it twice
        # would double it for every caller that counts rows per event.
        if (canonical, tuple(cells)) in seen:
            continue
        seen.add((canonical, tuple(cells)))
        # The generated file holds two tables, and only the "Option Risk Details" one carries
        # options. The "Event Index" table's second column is a base type (`EventModel`,
        # `AncientEventModel`, `Custom`), and taking its rows would file a base type as an option of
        # the event. A row whose option could not be resolved (`unknown`) IS an option row and is
        # kept, so the row set stays the one the file lists.
        if _INDEX_TABLE_BASE_TYPE.fullmatch(option):
            continue

        rows.setdefault(canonical, []).append(
            {
                "option": option,
                "handler": handler,
                "effect": effect,
                "cost": cost,
                "risk": risk,
                "continuation": continuation,
            }
        )
        canonical_to_displayed.setdefault(canonical, set()).add(displayed)

    ambiguous = frozenset(
        canonical for canonical, displayed in canonical_to_displayed.items() if len(displayed) > 1
    )
    return {name: tuple(values) for name, values in rows.items()}, ambiguous


def _ambiguous_canonical_ids(markdown: str) -> frozenset[str]:
    """Canonical ids the index spells with more than one displayed event name.

    Canonicalising is not necessarily injective: `N_E_O_W` and `NEW` are two ids the game can
    report, and a looser scheme (dropping underscores, case-folding a display name) could fold a
    pair of genuinely different events onto one key. Rather than pick one of the two, the id is
    dropped from the lookup, so an ambiguous name answers "nothing" instead of an answer that
    belongs to a different event.
    """
    return parse_event_option_index(markdown)[1]


def event_option_risk(root: Path | None, event_id: str | None) -> list[dict[str, str]]:
    """The generated risk rows for one event, in source order.

    The rows come from the offline index, which grades each option from the decompiled handler. They
    are returned in the order the event builds them, so a caller can line them up with the live
    `event.options[]`; nothing here guesses which live index a row belongs to, because the live
    payload and the source can disagree about order and a wrong pairing would be worse than none.

    The lookup is exact after canonicalisation: the live `NEOW` and the index's `Neow` are the same
    key, but an id the index does not carry returns nothing. A near miss — a prefix, a shared word, a
    differing option — is never substituted, and an id whose canonical form is ambiguous for the
    index returns nothing as well.
    """
    if root is None or not isinstance(event_id, str) or not event_id.strip():
        return []

    path = root / "docs" / "game-knowledge" / "events.md"
    if not path.is_file():
        return []

    index, ambiguous = parse_event_option_index(path.read_text(encoding="utf-8"))
    canonical = canonical_event_id(event_id)
    if not canonical or canonical in ambiguous:
        return []
    return [dict(row) for row in index.get(canonical, ())]


def scene_guidance(state: Any, root: Path | None = None) -> dict[str, Any]:
    """The guidance block for one `/state` payload.

    ``screen``, ``scene``, ``guidance``, and ``playbook`` are the four keys both MCP surfaces always
    answer with; the native tool stops there. ``event_id``, ``event_options``, and
    ``guidance_source`` are the extras only this side can add, because they come from the generated
    index in the repository that the mod does not ship.

    ``event_id`` is the value the payload reported, unchanged — the interface is the live state's,
    and the canonical form is an internal keying detail, not a rewrite of what the caller sent. The
    index lookup canonicalises it; a caller that wants the canonical spelling itself can call
    :func:`canonical_event_id`. ``guidance`` is the strategy text the mod injects into its own loop
    and is empty on a screen with no strategic choice; ``playbook`` is the per-screen action
    sequence and is never empty.
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
