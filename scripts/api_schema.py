#!/usr/bin/env python3
"""Generate and verify the STS2 AI Agent's machine-readable HTTP contract.

This module is deliberately a *deep module*: callers only build, write, or check the
OpenAPI document, while the C# source parsing, type mapping, route accounting, canonical
rendering, and source-shape failure modes stay here.  It has no runtime dependency and
never imports the mod, so it is safe in a fresh checkout and in the release preflight.

It does not try to pretend every response is static.  The game data exports, compact
agent view, and native MCP JSON-RPC payloads are genuinely open-ended; their schemas say
so instead of inventing a false model.  The ordinary HTTP route paths and the state/action
wire records, by contrast, are extracted directly from their C# owners.
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

DEFAULT_OUTPUT = Path("docs/openapi.json")
OPENAPI_VERSION = "3.1.0"

ROUTER_PATH = Path("STS2AIAgent/Server/Router.cs")
NATIVE_MCP_PATH = Path("STS2AIAgent/Server/NativeMcpServer.cs")
PAYLOADS_PATH = Path("STS2AIAgent/Game/GameStateService.Payloads.cs")
ACTION_SERVICE_PATH = Path("STS2AIAgent/Game/GameActionService.cs")
TEAM_INTENT_PATH = Path("STS2AIAgent/Multiplayer/TeamIntent.cs")
DECISION_LOG_PATH = Path("STS2AIAgent/Agent/DecisionLog.cs")
EVENT_SERVICE_PATH = Path("STS2AIAgent/Server/GameEventService.cs")
DATA_SCHEMA_PATH = Path("STS2AIAgent/Agent/GameDataExportSchema.cs")
HTTP_SERVER_PATH = Path("STS2AIAgent/Server/HttpServer.cs")
MANIFEST_PATH = Path("STS2AIAgent/mod_manifest.json")
API_DOC_PATH = Path("docs/api.md")

# A leading @ is C#'s escape for a keyword that remains a snake_case JSON property.  C# payload
# declarations intentionally use only simple wire types; a new generic or unfamiliar wire type is
# an error here rather than a quietly weak `object` schema.
PUBLIC_PROPERTY = re.compile(
    r"^\s*public\s+(?P<type>[^\r\n{]+?)\s+@?(?P<name>[a-z][a-z0-9_]*)\s*"
    r"\{\s*get;\s*(?:init|set);",
    re.MULTILINE,
)
CLASS_DECLARATION = re.compile(r"\binternal\s+(?:sealed\s+)?class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b")
RECORD_DECLARATION = re.compile(
    r"\binternal\s+(?:sealed\s+)?record\s+(?:struct\s+)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\("
)
RECORD_PARAMETER = re.compile(r"^(?P<type>[A-Za-z_][A-Za-z0-9_]*(?:\[\])*(?:\?)?)\s+(?P<name>@?[a-z][a-z0-9_]*)$")
ACTION_SWITCH = re.compile(r'^\s*"(?P<name>[a-z][a-z0-9_]*)"\s*=>', re.MULTILINE)
ROUTE_LITERAL = re.compile(r'"(?P<path>/[a-z0-9/_.-]*)"')
METHOD_EQUALS = re.compile(r'request\.HttpMethod\.Equals\(\s*"(?P<method>[A-Z]+)"')
METHOD_EQUALS_SHORT = re.compile(r'request\.HttpMethod\s*==\s*"(?P<method>[A-Z]+)"')
MCP_METHOD = re.compile(r'method\s*(?:==|!=)\s*"(?P<method>[A-Z]+)"')
EXPORT_COLLECTION = re.compile(r'^\s*\["(?P<name>[a-z]+)"\]\s*=\s*new\[\]', re.MULTILINE)
DOC_ERROR_ROW = re.compile(r"^\|\s*`(?P<code>[a-z_]+)`[^|]*\|\s*(?P<status>\d+)\s*\|", re.MULTILINE)
DOC_SCREEN_ROW = re.compile(r"^\|\s*`(?P<name>[A-Z][A-Z0-9_]*)`", re.MULTILINE)
DOC_EVENT_ROW = re.compile(r"^\|\s*`(?P<first>[a-z_]+)`(?:\s*/\s*`(?P<second>[a-z_]+)`)?\s*\|", re.MULTILINE)
DOC_ACTION_STATUS_ROW = re.compile(r"^\|\s*`(?P<name>[a-z_]+)`\s*\|", re.MULTILINE)
DEFAULT_PORT = re.compile(r"private\s+const\s+int\s+DefaultPort\s*=\s*(?P<port>\d+)\s*;")
HEALTH_KEY = re.compile(r"^\s*(?P<name>[a-z][a-z0-9_]*)\s*=", re.MULTILINE)
TEAM_INTENT_CONSTANT = re.compile(r'public\s+const\s+string\s+[A-Za-z][A-Za-z0-9_]*\s*=\s*"(?P<value>[a-z_]+)"\s*;')
ROUTER_OUTER_IF = re.compile(r"^\s{12}if\s*\(", re.MULTILINE)
ANONYMOUS_DATA = re.compile(r"\bdata\s*=\s*new\s*\{")
ANONYMOUS_FIELD = re.compile(r"^\s*(?P<name>[a-z][a-z0-9_]*)\s*(?:=|,|$)", re.MULTILINE)

# These response objects are anonymous C# projections rather than payload records. Their interface
# is intentionally short, but a new/removed field is still a wire-contract change; this table makes
# the generator fail until its schema type can be chosen deliberately rather than guessing from an
# expression in Router.HandleAsync.
ROUTE_RESPONSE_FIELDS = {
    "/session/control": ("phase", "play_running", "play_phase"),
    "/teammate/control": ("phase", "play_running", "play_phase", "companion_auto_play"),
    "/companion/control": ("phase",),
    "/companion/message": ("reply",),
}


class SchemaError(RuntimeError):
    """The source no longer has a shape this generator can truthfully describe."""


@dataclass(frozen=True)
class CSharpProperty:
    """One public C# wire property after the C# keyword escape is removed."""

    name: str
    type_name: str


def read_text(repo_root: Path, relative: Path) -> str:
    path = repo_root / relative
    if not path.is_file():
        raise SchemaError(f"required schema source is missing: {relative.as_posix()}")
    return path.read_text(encoding="utf-8")


def brace_block(source: str, opening_brace: int, label: str) -> str:
    """The contents of the balanced brace block starting at *opening_brace*."""
    if opening_brace < 0 or opening_brace >= len(source) or source[opening_brace] != "{":
        raise SchemaError(f"{label} has no opening brace")
    depth = 0
    for index in range(opening_brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[opening_brace + 1 : index]
    raise SchemaError(f"{label} has an unterminated brace block")


def declaration_body(source: str, start: int, label: str) -> str:
    opening = source.find("{", start)
    if opening < 0:
        raise SchemaError(f"{label} has no body")
    return brace_block(source, opening, label)


def markdown_section(text: str, heading: str, source: str) -> str:
    start = text.find(heading)
    if start < 0:
        raise SchemaError(f"{source} no longer contains section {heading!r}")
    level = len(heading) - len(heading.lstrip("#"))
    rest = text[start + len(heading) :]
    following = re.search(r"^#{1,%d} " % level, rest, re.MULTILINE)
    return rest[: following.start()] if following else rest


def parse_class_properties(source: str, declaration: str | int, label: str) -> list[CSharpProperty]:
    start = declaration if isinstance(declaration, int) else source.find(declaration)
    if start < 0:
        raise SchemaError(f"{label} no longer declares {declaration!r}")
    body = declaration_body(source, start, label)
    properties: list[CSharpProperty] = []
    for match in PUBLIC_PROPERTY.finditer(body):
        type_name = " ".join(match.group("type").split())
        properties.append(CSharpProperty(match.group("name"), type_name))
    if not properties:
        raise SchemaError(f"{label} declaration {declaration!r} yielded no public wire properties")
    names = [item.name for item in properties]
    if len(names) != len(set(names)):
        raise SchemaError(f"{label} declaration {declaration!r} repeats a wire property")
    return properties


def parse_payload_classes(source: str) -> dict[str, list[CSharpProperty]]:
    """Every `*Payload` record plus the action descriptor in Payloads.cs."""
    schema_types: dict[str, list[CSharpProperty]] = {}
    for match in CLASS_DECLARATION.finditer(source):
        name = match.group("name")
        if not (name.endswith("Payload") or name == "ActionDescriptor"):
            continue
        schema_types[name] = parse_class_properties(source, match.start(), f"{PAYLOADS_PATH}:{name}")

    # Internal metadata records use PascalCase computed-property names and never cross the JSON
    # boundary. Only the lower snake_case wire-properties count, so skip them explicitly rather
    # than accidentally publishing a helper just because its class name ends in Payload.
    if len(schema_types) < 50:
        raise SchemaError(
            f"{PAYLOADS_PATH} yielded {len(schema_types)} payload schemas, below the 50 expected; "
            "the parser no longer matches the source"
        )
    return schema_types


def parse_record_properties(source: str, record_name: str, label: str) -> list[CSharpProperty]:
    match = next((item for item in RECORD_DECLARATION.finditer(source) if item.group("name") == record_name), None)
    if match is None:
        raise SchemaError(f"{label} no longer declares positional record {record_name}")
    # The declaration regex ends immediately after the opening parenthesis.  Nesting is permitted
    # internally even though the current record parameters are simple, so a later useful C# type
    # cannot be truncated by the first close parenthesis.
    opening = source.find("(", match.start())
    depth = 0
    closing = -1
    for index in range(opening, len(source)):
        if source[index] == "(":
            depth += 1
        elif source[index] == ")":
            depth -= 1
            if depth == 0:
                closing = index
                break
    if closing < 0:
        raise SchemaError(f"{label}:{record_name} has an unterminated parameter list")
    fields: list[CSharpProperty] = []
    for raw in source[opening + 1 : closing].split(","):
        parameter = " ".join(raw.split())
        parsed = RECORD_PARAMETER.match(parameter)
        if parsed is None:
            raise SchemaError(f"{label}:{record_name} has unsupported record parameter {parameter!r}")
        fields.append(CSharpProperty(parsed.group("name").lstrip("@"), parsed.group("type")))
    if not fields:
        raise SchemaError(f"{label}:{record_name} yielded no record properties")
    return fields


def schema_for_type(type_name: str, known_components: set[str]) -> dict[str, Any]:
    """Map the deliberately limited C# wire type dialect to OpenAPI 3.1 JSON Schema."""
    compact = type_name.replace(" ", "")
    nullable = compact.endswith("?")
    if nullable:
        compact = compact[:-1]

    dimensions = 0
    while compact.endswith("[]"):
        dimensions += 1
        compact = compact[:-2]

    primitive: dict[str, dict[str, Any]] = {
        "string": {"type": "string"},
        "int": {"type": "integer", "format": "int32"},
        "long": {"type": "integer", "format": "int64"},
        "decimal": {"type": "number"},
        "double": {"type": "number", "format": "double"},
        "float": {"type": "number", "format": "float"},
        "bool": {"type": "boolean"},
    }
    if compact == "object":
        # `object?` is intentionally free-form.  Adding `type: object` would reject the string,
        # array, and scalar data that the actual JSON serializer accepts for agent_view/capstone.
        result: dict[str, Any] = {"description": "Free-form JSON value."}
    elif compact in primitive:
        result = dict(primitive[compact])
    elif compact in known_components:
        result = {"$ref": f"#/components/schemas/{compact}"}
    else:
        raise SchemaError(
            f"unsupported C# wire type {type_name!r}; add an intentional mapping to schema_for_type "
            "instead of silently publishing an untyped field"
        )

    for _ in range(dimensions):
        result = {"type": "array", "items": result}

    if not nullable or compact == "object":
        return result
    if "$ref" in result:
        return {"anyOf": [result, {"type": "null"}]}
    # OpenAPI 3.1 uses native JSON Schema multi-types rather than OpenAPI 3.0's nullable flag.
    # Arrays keep their `items`; rewriting `type` into [array, null] preserves that item contract.
    scalar_type = result.get("type")
    if isinstance(scalar_type, str):
        result["type"] = [scalar_type, "null"]
        return result
    return {"anyOf": [result, {"type": "null"}]}


def object_schema(properties: Iterable[CSharpProperty], known_components: set[str], *, required: bool = False) -> dict[str, Any]:
    fields = list(properties)
    result: dict[str, Any] = {
        "type": "object",
        "properties": {field.name: schema_for_type(field.type_name, known_components) for field in fields},
        # The public wire contract is additive. Rejecting a field added by a newer mod would make a
        # generated client less compatible than the existing JSON readers, so these schemas model
        # known fields without closing the object. C# nullability is type information, not a
        # truthful promise about key presence, so only request bodies make a required-key promise.
        "additionalProperties": True,
    }
    if required:
        result["required"] = [field.name for field in fields]
    return result


def parse_action_names(action_source: str) -> list[str]:
    marker = action_source.find("return actionName switch")
    if marker < 0:
        raise SchemaError(f"{ACTION_SERVICE_PATH} no longer declares the action switch")
    opening = action_source.find("{", marker)
    body = brace_block(action_source, opening, f"{ACTION_SERVICE_PATH}: action switch")
    names = ACTION_SWITCH.findall(body)
    if len(names) < 50:
        raise SchemaError(
            f"{ACTION_SERVICE_PATH} action switch yielded {len(names)} actions, below the 50 expected; "
            "the parser no longer matches the source"
        )
    if len(names) != len(set(names)):
        raise SchemaError(f"{ACTION_SERVICE_PATH} action switch repeats an action name")
    return names


def parse_data_collections(source: str) -> list[str]:
    names = EXPORT_COLLECTION.findall(source)
    if len(names) < 7:
        raise SchemaError(f"{DATA_SCHEMA_PATH} yielded {len(names)} collections, below the 7 expected")
    if len(names) != len(set(names)):
        raise SchemaError(f"{DATA_SCHEMA_PATH} repeats a collection name")
    return names


def parse_default_port(source: str) -> int:
    match = DEFAULT_PORT.search(source)
    if match is None:
        raise SchemaError(f"{HTTP_SERVER_PATH} no longer declares DefaultPort")
    return int(match.group("port"))


def parse_doc_error_codes(api_doc: str) -> list[str]:
    section = markdown_section(api_doc, "## 错误码", API_DOC_PATH.as_posix())
    codes = DOC_ERROR_ROW.findall(section)
    if len(codes) < 15:
        raise SchemaError(f"{API_DOC_PATH} error table yielded {len(codes)} codes, below the 15 expected")
    return [code for code, _ in codes]


def parse_doc_screens(api_doc: str) -> list[str]:
    section = markdown_section(api_doc, "## Screen 枚举", API_DOC_PATH.as_posix())
    screens = DOC_SCREEN_ROW.findall(section)
    if len(screens) < 20:
        raise SchemaError(f"{API_DOC_PATH} Screen enum yielded {len(screens)} entries, below the 20 expected")
    return screens


def parse_doc_events(api_doc: str) -> list[str]:
    section = markdown_section(api_doc, "## `GET /events/stream`", API_DOC_PATH.as_posix())
    events: list[str] = []
    for first, second in DOC_EVENT_ROW.findall(section):
        events.append(first)
        if second:
            events.append(second)
    if len(events) < 8:
        raise SchemaError(f"{API_DOC_PATH} event table yielded {len(events)} entries, below the 8 expected")
    return events


def parse_doc_action_statuses(api_doc: str) -> list[str]:
    section = markdown_section(api_doc, "## Action Status", API_DOC_PATH.as_posix())
    statuses = DOC_ACTION_STATUS_ROW.findall(section)
    expected = {"completed", "pending", "failed"}
    if set(statuses) != expected:
        raise SchemaError(
            f"{API_DOC_PATH} Action Status table must list exactly {sorted(expected)}, got {sorted(set(statuses))}"
        )
    return statuses


def parse_health_keys(router_source: str) -> list[str]:
    signature = "internal static object BuildHealthData()"
    start = router_source.find(signature)
    if start < 0:
        raise SchemaError(f"{ROUTER_PATH} no longer declares {signature}")
    body = declaration_body(router_source, start, f"{ROUTER_PATH}: BuildHealthData")
    keys = HEALTH_KEY.findall(body)
    if len(keys) < 18:
        raise SchemaError(f"{ROUTER_PATH}: BuildHealthData yielded {len(keys)} keys, below the 18 expected")
    return keys


def top_level_initializer_fields(body: str, label: str) -> tuple[str, ...]:
    """The JSON keys of a C# anonymous-object initializer body.

    Route response objects are deliberately small anonymous projections, so making a new record for
    each would be ceremony rather than depth. This parser gives them the same drift protection as a
    record: split only on top-level commas and accept C#'s shorthand `phase` alongside `name = value`.
    It does not interpret expressions -- that belongs to C# -- only the wire key at the left edge.
    """
    parts: list[str] = []
    start = 0
    brace = paren = bracket = 0
    quote: str | None = None
    escaped = False
    for index, char in enumerate(body):
        if quote is not None:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            continue
        if char in ('"', "'"):
            quote = char
        elif char == "{":
            brace += 1
        elif char == "}":
            brace -= 1
        elif char == "(":
            paren += 1
        elif char == ")":
            paren -= 1
        elif char == "[":
            bracket += 1
        elif char == "]":
            bracket -= 1
        elif char == "," and brace == paren == bracket == 0:
            parts.append(body[start:index])
            start = index + 1
    parts.append(body[start:])

    fields: list[str] = []
    for raw in parts:
        # Strip line comments, then C# object initializer values are either `key = expression` or
        # the simple property inference form `key`.
        candidate = raw.split("//", 1)[0].strip()
        if not candidate:
            continue
        match = re.match(r"(?P<name>[a-z][a-z0-9_]*)\s*(?:=|$)", candidate)
        if match is None:
            raise SchemaError(f"{label} has an unsupported anonymous-object member {candidate!r}")
        fields.append(match.group("name"))
    if not fields:
        raise SchemaError(f"{label} yielded no response fields")
    if len(fields) != len(set(fields)):
        raise SchemaError(f"{label} repeats a response field")
    return tuple(fields)


def parse_router_response_fields(router_source: str) -> None:
    """Ensure each schema override still names exactly Router's anonymous response fields."""
    markers = {
        "/session/control": 'request.Url?.AbsolutePath == "/session/control"',
        "/teammate/control": 'request.Url?.AbsolutePath == "/teammate/control"',
        "/companion/control": 'if (request.Url.AbsolutePath == "/companion/control")',
        "/companion/message": "var reply = await AgentRuntime.Instance.ReplyToTeammateAsync",
    }
    for route, marker in markers.items():
        start = router_source.find(marker)
        if start < 0:
            raise SchemaError(f"{ROUTER_PATH} no longer contains the {route} response marker")
        data_start = router_source.find("data = new", start)
        if data_start < 0:
            raise SchemaError(f"{ROUTER_PATH} {route} no longer serializes an anonymous data response")
        opening = router_source.find("{", data_start)
        actual = top_level_initializer_fields(
            brace_block(router_source, opening, f"{ROUTER_PATH} {route} response"),
            f"{ROUTER_PATH} {route} response",
        )
        expected = ROUTE_RESPONSE_FIELDS[route]
        if actual != expected:
            raise SchemaError(
                f"{ROUTER_PATH} {route} response fields are {list(actual)}, but the OpenAPI override has "
                f"{list(expected)}. Update the endpoint schema deliberately, then regenerate docs/openapi.json."
            )


def parse_team_intent_types(source: str) -> list[str]:
    values = TEAM_INTENT_CONSTANT.findall(source)
    expected = {"focus_fire", "target_announce", "potion_ownership"}
    if set(values) != expected:
        raise SchemaError(
            f"{TEAM_INTENT_PATH} intent constants are {sorted(set(values))}, expected {sorted(expected)}. "
            "Update the typed signal schema deliberately rather than publishing a stale enum."
        )
    return values


def source_routes(router_source: str, native_mcp_source: str) -> tuple[set[str], dict[str, set[str]], set[str]]:
    """Read paths and ordinary HTTP methods from their C# dispatch owners.

    Paths are normalised to OpenAPI templates.  The Router method styles deliberately differ
    (`==` in companion routes, case-insensitive `.Equals` elsewhere), so the per-condition scan
    refuses to infer a method when it cannot find one rather than silently putting a route under
    GET.
    """
    discovered: set[str] = set()
    methods: dict[str, set[str]] = {}

    for literal in ROUTE_LITERAL.finditer(router_source):
        raw = literal.group("path")
        if raw == "/":
            continue
        path = "/data/{collection}" if raw == "/data/" else raw.rstrip("/") or "/"
        discovered.add(path)
        condition_start = router_source.rfind("if (", 0, literal.start())
        condition_end = router_source.find("{", literal.end())
        condition = router_source[condition_start:condition_end] if condition_start >= 0 and condition_end >= 0 else ""
        match = METHOD_EQUALS.search(condition) or METHOD_EQUALS_SHORT.search(condition)
        if match is not None:
            methods.setdefault(path, set()).add(match.group("method"))

    # Router hands /mcp to NativeMcpServer, whose operation method surface is genuinely separate.
    if "/mcp" not in discovered:
        raise SchemaError(f"{ROUTER_PATH} no longer delegates /mcp to NativeMcpServer")
    mcp_methods = set(MCP_METHOD.findall(native_mcp_source))
    required_mcp_methods = {"OPTIONS", "GET", "DELETE", "POST"}
    if not required_mcp_methods <= mcp_methods:
        raise SchemaError(
            f"{NATIVE_MCP_PATH} no longer exposes the expected method branches "
            f"{sorted(required_mcp_methods)} (found {sorted(mcp_methods)})"
        )
    return discovered, methods, mcp_methods


def ref(name: str) -> dict[str, str]:
    return {"$ref": f"#/components/schemas/{name}"}


def nullable_ref(name: str) -> dict[str, Any]:
    return {"anyOf": [ref(name), {"type": "null"}]}


def success_response(data_schema: dict[str, Any], description: str = "Successful response.") -> dict[str, Any]:
    return {
        "description": description,
        "content": {
            "application/json": {
                "schema": {
                    "allOf": [
                        ref("ApiSuccessEnvelope"),
                        {"type": "object", "properties": {"data": data_schema}},
                    ]
                }
            }
        },
    }


def error_response(description: str = "The normal HTTP error envelope.") -> dict[str, Any]:
    return {"description": description, "content": {"application/json": {"schema": ref("ApiErrorEnvelope")}}}


def ordinary_operation(
    summary: str,
    data_schema: dict[str, Any],
    *,
    parameters: list[dict[str, Any]] | None = None,
    request_body: dict[str, Any] | None = None,
) -> dict[str, Any]:
    operation: dict[str, Any] = {
        "summary": summary,
        "responses": {"200": success_response(data_schema), "default": error_response()},
    }
    if parameters:
        operation["parameters"] = parameters
    if request_body is not None:
        operation["requestBody"] = request_body
    return operation


def parameter(name: str, location: str, schema: dict[str, Any], description: str, *, required: bool = False) -> dict[str, Any]:
    return {"name": name, "in": location, "required": required, "description": description, "schema": schema}


def build_paths(
    data_collections: list[str],
    expected_paths: set[str],
    ordinary_methods: dict[str, set[str]],
) -> dict[str, Any]:
    """The small, explicit endpoint table over the source-derived route set.

    The table owns endpoint-specific request and response wiring.  The generator separately proves
    that its paths and methods still match Router/NatveMcpServer, so this is useful metadata, not a
    second source of truth for what the server serves.
    """
    json_body = lambda schema: {
        "required": True,
        "content": {"application/json": {"schema": schema}},
    }
    companion_header = parameter(
        "X-STS2-Companion-Session", "header", {"type": "string"},
        "Required active local companion session token.", required=True,
    )
    paths: dict[str, Any] = {
        "/health": {"get": ordinary_operation("Read mod health and API discovery data.", ref("HealthData"))},
        "/state": {"get": ordinary_operation("Read the complete game-state snapshot.", ref("GameStatePayload"))},
        "/decision-snapshot": {
            "get": ordinary_operation(
                "Read one decision's state and the action descriptors its own state build enumerated.",
                ref("DecisionSnapshotPayload"),
            )
        },
        "/actions/available": {
            "get": ordinary_operation("Read actions available in the current state.", ref("AvailableActionsPayload"))
        },
        "/action": {
            "post": ordinary_operation(
                "Execute one game action.", ref("ActionResponsePayload"), request_body=json_body(ref("ActionRequest"))
            )
        },
        "/session/control": {
            "post": ordinary_operation(
                "Start or pause local autoplay.", ref("SessionControlResponseData"),
                request_body=json_body(ref("SessionControlRequest")),
            )
        },
        "/teammate/control": {
            "post": ordinary_operation(
                "Start or pause the host's AI teammate.", ref("TeammateControlResponseData"),
                request_body=json_body(ref("SessionControlRequest")),
            )
        },
        "/companion/control": {
            "post": ordinary_operation(
                "Control the companion instance itself.", ref("CompanionControlResponseData"),
                parameters=[companion_header], request_body=json_body(ref("SessionControlRequest")),
            )
        },
        "/companion/message": {
            "post": ordinary_operation(
                "Send a text message and optional typed signal to the companion.", ref("CompanionMessageResponseData"),
                parameters=[companion_header], request_body=json_body(ref("CompanionMessageRequest")),
            )
        },
        "/data/{collection}": {
            "get": ordinary_operation(
                "Export one game-data collection.", {"type": "array", "items": ref("GameDataItem")},
                parameters=[parameter("collection", "path", {"type": "string", "enum": data_collections},
                                      "Case-insensitive exported collection name.", required=True)],
            )
        },
        "/decisions": {
            "get": ordinary_operation(
                "Read accepted decisions, oldest first.", {"type": "array", "items": ref("DecisionLogEntry")},
                parameters=[parameter("limit", "query", {"type": "integer", "minimum": 1, "maximum": 200, "default": 50},
                                      "Number of visible log entries, clamped to 1..200.")],
            )
        },
        "/events/stream": {
            "get": {
                "summary": "Subscribe to the server-sent event stream.",
                "description": "The stream multiplexes event types. See docs/api.md for event payload meanings and reconnect rules.",
                "responses": {
                    "200": {
                        "description": "A text/event-stream sequence of GameEventEnvelope payloads.",
                        "content": {"text/event-stream": {"schema": ref("GameEventEnvelope")}},
                    }
                },
            }
        },
        "/mcp": {
            "x-sts2-aliases": ["/mcp/"],
            "options": {"summary": "Discover/prepare a native MCP Streamable HTTP session.", "responses": {"204": {"description": "No content."}}},
            "delete": {
                "summary": "Clear the native MCP Streamable HTTP session.",
                "responses": {"200": {"description": "Session cleared.", "content": {"application/json": {"schema": ref("McpSessionReset")}}}, "default": {"description": "Native MCP error or JSON-RPC error.", "content": {"application/json": {"schema": ref("McpOpaqueResponse")}}}},
            },
            "post": {
                "summary": "Call native MCP via Streamable HTTP JSON-RPC.",
                "description": "This operation has MCP/JSON-RPC envelopes, not the ordinary STS2 HTTP request_id envelope.",
                "requestBody": json_body(ref("McpOpaqueRequest")),
                "responses": {"200": {"description": "MCP JSON-RPC or SSE response.", "content": {"application/json": {"schema": ref("McpOpaqueResponse")}, "text/event-stream": {"schema": ref("McpOpaqueResponse")}}}, "default": {"description": "Native MCP error or JSON-RPC error.", "content": {"application/json": {"schema": ref("McpOpaqueResponse")}}}},
            },
        },
    }

    table_paths = set(paths)
    if expected_paths != table_paths:
        raise SchemaError(
            "OpenAPI endpoint table does not match source routes: "
            f"missing {sorted(expected_paths - table_paths)}, extra {sorted(table_paths - expected_paths)}"
        )
    expected_ordinary_methods = {path: methods for path, methods in ordinary_methods.items() if path != "/mcp"}
    actual_ordinary_methods = {
        path: {name.upper() for name in operations if name.lower() in {"get", "post", "put", "patch", "delete", "options", "head"}}
        for path, operations in paths.items() if path != "/mcp"
    }
    for path, methods in expected_ordinary_methods.items():
        actual = actual_ordinary_methods.get(path, set())
        if methods != actual:
            raise SchemaError(
                f"OpenAPI methods for {path} are {sorted(actual)}, but Router.HandleAsync serves {sorted(methods)}"
            )
    return paths


def build_components(
    payload_classes: dict[str, list[CSharpProperty]],
    action_request: list[CSharpProperty],
    action_response: list[CSharpProperty],
    decision_entry: list[CSharpProperty],
    event_envelope: list[CSharpProperty],
    health_keys: list[str],
    actions: list[str],
    screens: list[str],
    events: list[str],
    statuses: list[str],
    error_codes: list[str],
    team_intent_types: list[str],
) -> dict[str, Any]:
    component_names = set(payload_classes) | {
        "ActionRequest", "ActionResponsePayload", "DecisionLogEntry", "GameEventEnvelope",
        "HealthData", "ApiSuccessEnvelope", "ApiErrorEnvelope", "ApiError", "SessionControlRequest",
        "SessionControlResponseData", "TeammateControlResponseData", "CompanionControlResponseData",
        "CompanionMessageRequest", "CompanionMessageResponseData", "TeamIntent", "GameDataItem",
        "McpOpaqueRequest", "McpOpaqueResponse", "McpSessionReset",
    }
    schemas: dict[str, Any] = {
        name: object_schema(properties, component_names) for name, properties in sorted(payload_classes.items())
    }
    schemas["ActionRequest"] = object_schema(action_request, component_names, required=False)
    schemas["ActionRequest"]["properties"]["action"]["enum"] = actions
    schemas["ActionRequest"]["required"] = ["action"]
    schemas["ActionResponsePayload"] = object_schema(action_response, component_names)
    schemas["ActionResponsePayload"]["properties"]["status"]["enum"] = statuses
    schemas["DecisionLogEntry"] = object_schema(decision_entry, component_names)
    schemas["GameEventEnvelope"] = object_schema(event_envelope, component_names)
    schemas["GameEventEnvelope"]["properties"]["type"]["enum"] = events
    schemas["GameStatePayload"]["properties"]["screen"]["enum"] = screens

    schemas.update(
        {
            "HealthData": {
                "type": "object",
                "description": "Health fields emitted by Router.BuildHealthData. Nested diagnostics remain forward-compatible free-form JSON.",
                "properties": {name: {} for name in health_keys},
                "required": health_keys,
                "additionalProperties": True,
            },
            "ApiSuccessEnvelope": {
                "type": "object",
                "description": "The ordinary non-MCP success envelope. Each operation narrows data to its route-specific schema.",
                "properties": {"ok": {"const": True}, "request_id": {"type": "string"}, "data": {}},
                "required": ["ok", "request_id", "data"],
                "additionalProperties": True,
            },
            "ApiError": {
                "type": "object",
                "properties": {
                    "code": {"type": "string", "enum": error_codes},
                    "message": {"type": "string"},
                    "details": {"description": "Route-specific free-form JSON or null."},
                    "retryable": {"type": "boolean"},
                },
                "required": ["code", "message", "details", "retryable"],
                "additionalProperties": True,
            },
            "ApiErrorEnvelope": {
                "type": "object",
                "description": "The ordinary non-MCP failure envelope.",
                "properties": {"ok": {"const": False}, "request_id": {"type": "string"}, "error": ref("ApiError")},
                "required": ["ok", "request_id", "error"],
                "additionalProperties": True,
            },
            "SessionControlRequest": {
                "type": "object",
                "properties": {"running": {"type": "boolean"}},
                "required": ["running"],
                "additionalProperties": True,
            },
            "SessionControlResponseData": object_schema(
                [CSharpProperty("phase", "string"), CSharpProperty("play_running", "bool"), CSharpProperty("play_phase", "string")],
                component_names,
            ),
            "TeammateControlResponseData": object_schema(
                [CSharpProperty("phase", "string"), CSharpProperty("play_running", "bool"), CSharpProperty("play_phase", "string"), CSharpProperty("companion_auto_play", "bool")],
                component_names,
            ),
            "CompanionControlResponseData": object_schema([CSharpProperty("phase", "string")], component_names),
            "CompanionMessageResponseData": object_schema([CSharpProperty("reply", "string")], component_names),
            "TeamIntent": {
                "description": "Optional typed teammate signal. Unknown fields are intentionally ignored for forward compatibility.",
                "type": "object",
                "properties": {
                    "type": {"type": "string", "enum": team_intent_types},
                    "enemy_index": {"type": "integer", "format": "int32"},
                    "player_id": {"type": "string"},
                    "potion_index": {"type": "integer", "format": "int32"},
                    "potion_id": {"type": "string"},
                },
                "required": ["type"],
                "additionalProperties": True,
                "oneOf": [
                    {"properties": {"type": {"const": "focus_fire"}}, "required": ["type", "enemy_index"]},
                    {"properties": {"type": {"const": "target_announce"}}, "required": ["type", "enemy_index"]},
                    {"properties": {"type": {"const": "potion_ownership"}}, "required": ["type", "potion_index"]},
                ],
            },
            "CompanionMessageRequest": {
                "type": "object",
                "properties": {
                    "message": {"type": "string", "minLength": 1, "maxLength": 2000},
                    "intent": nullable_ref("TeamIntent"),
                },
                "required": ["message"],
                "additionalProperties": True,
            },
            "GameDataItem": {"type": "object", "description": "Collection-specific exported game-data object.", "additionalProperties": True},
            "McpOpaqueRequest": {"description": "MCP Streamable HTTP JSON-RPC request. Native MCP owns its schema.", "additionalProperties": True},
            "McpOpaqueResponse": {"description": "MCP JSON-RPC or native MCP error response.", "additionalProperties": True},
            "McpSessionReset": {
                "type": "object", "properties": {"ok": {"const": True}}, "required": ["ok"], "additionalProperties": True
            },
        }
    )
    return {"schemas": dict(sorted(schemas.items()))}


def build_spec(repo_root: Path) -> dict[str, Any]:
    """Build a complete OpenAPI 3.1 document from the source-owned HTTP contract."""
    repo_root = repo_root.resolve()
    payload_source = read_text(repo_root, PAYLOADS_PATH)
    action_source = read_text(repo_root, ACTION_SERVICE_PATH)
    router_source = read_text(repo_root, ROUTER_PATH)
    native_mcp_source = read_text(repo_root, NATIVE_MCP_PATH)
    api_doc = read_text(repo_root, API_DOC_PATH)

    payload_classes = parse_payload_classes(payload_source)
    action_request = parse_class_properties(action_source, "internal sealed class ActionRequest", ACTION_SERVICE_PATH.as_posix())
    action_response = parse_class_properties(action_source, "internal sealed class ActionResponsePayload", ACTION_SERVICE_PATH.as_posix())
    decision_entry = parse_record_properties(read_text(repo_root, DECISION_LOG_PATH), "DecisionLogEntry", DECISION_LOG_PATH.as_posix())
    event_envelope = parse_class_properties(read_text(repo_root, EVENT_SERVICE_PATH), "internal sealed class GameEventEnvelope", EVENT_SERVICE_PATH.as_posix())
    parse_router_response_fields(router_source)
    team_intent_types = parse_team_intent_types(read_text(repo_root, TEAM_INTENT_PATH))
    actions = parse_action_names(action_source)
    data_collections = parse_data_collections(read_text(repo_root, DATA_SCHEMA_PATH))
    screens = parse_doc_screens(api_doc)
    events = parse_doc_events(api_doc)
    statuses = parse_doc_action_statuses(api_doc)
    error_codes = parse_doc_error_codes(api_doc)
    health_keys = parse_health_keys(router_source)
    routes, ordinary_methods, _ = source_routes(router_source, native_mcp_source)
    paths = build_paths(data_collections, routes, ordinary_methods)
    components = build_components(
        payload_classes, action_request, action_response, decision_entry, event_envelope, health_keys,
        actions, screens, events, statuses, error_codes, team_intent_types,
    )
    manifest = json.loads(read_text(repo_root, MANIFEST_PATH))
    version = manifest.get("version")
    if not isinstance(version, str) or not version:
        raise SchemaError(f"{MANIFEST_PATH} contains no usable version")

    return {
        "openapi": OPENAPI_VERSION,
        "jsonSchemaDialect": "https://json-schema.org/draft/2020-12/schema",
        "info": {
            "title": "STS2 AI Agent Mod HTTP API",
            "version": version,
            "description": "Generated from Router routes and C# wire payload declarations. Human guidance remains in docs/api.md.",
        },
        "servers": [{"url": f"http://127.0.0.1:{parse_default_port(read_text(repo_root, HTTP_SERVER_PATH))}", "description": "Default loopback address; discover the actual port through GET /health."}],
        "paths": paths,
        "components": components,
    }


def render_spec(spec: dict[str, Any]) -> str:
    """The one canonical spelling on disk, stable across platforms and Python runs."""
    return json.dumps(spec, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def write_spec(repo_root: Path, output: Path = DEFAULT_OUTPUT) -> str:
    """Build and write the generated artifact, returning its repository-relative path."""
    repo_root = repo_root.resolve()
    destination = output if output.is_absolute() else repo_root / output
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(render_spec(build_spec(repo_root)), encoding="utf-8", newline="\n")
    return destination.relative_to(repo_root).as_posix()


def check_spec(repo_root: Path, output: Path = DEFAULT_OUTPUT) -> list[str]:
    """Refuse a missing, malformed, or stale generated document without modifying it."""
    repo_root = repo_root.resolve()
    destination = output if output.is_absolute() else repo_root / output
    generated = render_spec(build_spec(repo_root))
    if not destination.is_file():
        raise SchemaError(f"{output.as_posix()} is missing. Run 'python scripts/api_schema.py' to generate it.")
    try:
        existing = destination.read_text(encoding="utf-8")
        json.loads(existing)
    except (OSError, json.JSONDecodeError) as exc:
        raise SchemaError(f"{output.as_posix()} is not valid JSON: {exc}") from exc
    if existing != generated:
        diff = "".join(
            difflib.unified_diff(
                existing.splitlines(keepends=True), generated.splitlines(keepends=True),
                fromfile=f"{output.as_posix()} (committed)", tofile=f"{output.as_posix()} (generated)", n=2,
            )
        )
        preview = diff[:4000] + ("\n... diff truncated" if len(diff) > 4000 else "")
        raise SchemaError(
            f"{output.as_posix()} is out of date. Run 'python scripts/api_schema.py' to regenerate it.\n{preview}"
        )
    return [
        f"{output.as_posix()} matches C# route/payload sources and docs-derived vocabularies",
        f"OpenAPI {OPENAPI_VERSION} contains {len(build_spec(repo_root)['paths'])} path(s)",
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate or verify the STS2 AI Agent OpenAPI 3.1 document.")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true", help="verify the committed document without rewriting it")
    args = parser.parse_args()
    try:
        if args.check:
            for note in check_spec(args.repo_root, args.output):
                print(f"[api-schema] {note}")
        else:
            print(f"[api-schema] wrote {write_spec(args.repo_root, args.output)}")
    except SchemaError as exc:
        print(f"api-schema failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
