#!/usr/bin/env python3
"""Offline contracts for scripts/api_schema.py.

The PowerShell verification-gate self-test proves generated-byte drift is rejected in a copy of the
repository. This compact suite tests semantic facts that are easier to inspect as Python objects:
all source routes, C# wire schemas, nullable arrays, the special typed team signal, and the native
MCP envelope exception.
"""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "scripts" / "api_schema.py"
SPEC = importlib.util.spec_from_file_location("sts2_api_schema_test", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
api_schema = importlib.util.module_from_spec(SPEC)
# `dataclasses` resolves postponed annotations through sys.modules as a normal import would.
import sys
sys.modules[SPEC.name] = api_schema
SPEC.loader.exec_module(api_schema)


class ApiSchemaTests(unittest.TestCase):
    def setUp(self) -> None:
        self.spec = api_schema.build_spec(ROOT)

    def test_generated_artifact_is_canonical_and_current(self) -> None:
        self.assertEqual(
            api_schema.render_spec(self.spec),
            (ROOT / "docs" / "openapi.json").read_text(encoding="utf-8"),
        )
        self.assertEqual(json.loads(api_schema.render_spec(self.spec)), self.spec)
        self.assertEqual(api_schema.check_spec(ROOT)[1], "OpenAPI 3.1.0 contains 18 path(s)")

    def test_source_owned_routes_and_methods_have_one_operation_each(self) -> None:
        paths = self.spec["paths"]
        self.assertEqual(
            set(paths),
            {
                "/health", "/vision/screenshot", "/state", "/decision-snapshot", "/actions/available", "/action",
                "/session/control", "/mcp/control", "/teammate/control", "/companion/control", "/companion/jev", "/companion/message",
                "/data/{collection}", "/decisions", "/diagnostics", "/events/stream", "/mcp", "/strategy",
            },
        )
        self.assertEqual(set(paths["/mcp"]) & {"options", "delete", "post"}, {"options", "delete", "post"})
        # /strategy is the one route with two ordinary methods (read the strategy, write it back).
        self.assertEqual(set(paths["/strategy"]) & {"get", "post"}, {"get", "post"})
        for ordinary_path, method in (
            ("/health", "get"), ("/vision/screenshot", "get"), ("/state", "get"), ("/decision-snapshot", "get"),
            ("/actions/available", "get"),
            ("/action", "post"), ("/session/control", "post"), ("/mcp/control", "post"), ("/teammate/control", "post"),
            ("/companion/control", "post"), ("/companion/jev", "get"), ("/companion/message", "post"),
            ("/data/{collection}", "get"), ("/decisions", "get"), ("/diagnostics", "get"), ("/events/stream", "get"),
        ):
            self.assertIn(method, paths[ordinary_path], f"{ordinary_path} must retain {method}")

    def test_the_decision_snapshot_carries_the_compact_state_and_the_descriptors(self) -> None:
        """One route, both halves: the descriptor schema is the one `/actions/available` publishes."""
        schemas = self.spec["components"]["schemas"]
        snapshot = schemas["DecisionSnapshotPayload"]

        self.assertEqual(
            set(snapshot["properties"]),
            {"state", "available_actions"},
        )
        self.assertEqual(snapshot["properties"]["state"], {"description": "Free-form JSON value."})
        self.assertEqual(
            snapshot["properties"]["available_actions"],
            {"type": "array", "items": {"$ref": "#/components/schemas/ActionDescriptor"}},
        )
        self.assertEqual(
            snapshot["properties"]["available_actions"]["items"],
            schemas["AvailableActionsPayload"]["properties"]["actions"]["items"],
        )
        self.assertEqual(
            self.spec["paths"]["/decision-snapshot"]["get"]["responses"]["200"]["content"]
            ["application/json"]["schema"]["allOf"][1]["properties"]["data"],
            {"$ref": "#/components/schemas/DecisionSnapshotPayload"},
        )

    def test_csharp_payloads_preserve_nested_and_nullable_shapes(self) -> None:
        schemas = self.spec["components"]["schemas"]
        state = schemas["GameStatePayload"]
        self.assertNotIn("required", state, "response keys are not falsely claimed required")
        self.assertEqual(state["properties"]["combat"], {
            "anyOf": [{"$ref": "#/components/schemas/CombatPayload"}, {"type": "null"}]
        })
        self.assertEqual(
            state["properties"]["bundles"],
            {"type": ["array", "null"], "items": {"$ref": "#/components/schemas/BundlePayload"}},
        )
        self.assertEqual(schemas["ActionRequest"]["required"], ["action"])
        self.assertGreaterEqual(len([name for name in schemas if name.endswith("Payload")]), 56)

    def test_shared_vocabulary_and_typed_team_intent_are_carried_over(self) -> None:
        schemas = self.spec["components"]["schemas"]
        self.assertGreaterEqual(len(schemas["ActionRequest"]["properties"]["action"]["enum"]), 57)
        self.assertIn("COMBAT", schemas["GameStatePayload"]["properties"]["screen"]["enum"])
        self.assertIn("decision_made", schemas["GameEventEnvelope"]["properties"]["type"]["enum"])
        self.assertIn("invalid_action", schemas["ApiError"]["properties"]["code"]["enum"])
        intent = schemas["TeamIntent"]
        self.assertEqual(len(intent["oneOf"]), 3)
        self.assertEqual(intent["oneOf"][0]["required"], ["type", "enemy_index"])
        self.assertEqual(intent["oneOf"][2]["required"], ["type", "potion_index"])
        message = schemas["CompanionMessageRequest"]
        self.assertEqual(message["required"], ["message"])
        self.assertEqual(message["properties"]["message"]["maxLength"], 2000)

    def test_mcp_and_dynamic_exports_do_not_claim_ordinary_static_envelopes(self) -> None:
        paths = self.spec["paths"]
        mcp_post = paths["/mcp"]["post"]
        self.assertIn("JSON-RPC", mcp_post["description"])
        self.assertEqual(
            paths["/data/{collection}"]["get"]["responses"]["200"]["content"]["application/json"]["schema"]
            ["allOf"][1]["properties"]["data"]["items"],
            {"$ref": "#/components/schemas/GameDataItem"},
        )
        self.assertTrue(self.spec["components"]["schemas"]["GameDataItem"]["additionalProperties"])
        self.assertTrue(
            self.spec["components"]["schemas"]["GameStatePayload"]["additionalProperties"],
            "the public state contract is additive, so an older generated client must accept a newer field",
        )

    def test_check_mode_refuses_even_one_stale_byte(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "openapi.json"
            output.write_text(api_schema.render_spec(self.spec) + " ", encoding="utf-8")
            with self.assertRaisesRegex(api_schema.SchemaError, "out of date"):
                api_schema.check_spec(ROOT, output)


if __name__ == "__main__":
    unittest.main(verbosity=2)
