from __future__ import annotations

import asyncio
import contextlib
import io
import json
import os
import unittest
from contextlib import contextmanager
from typing import Any, Iterator
from unittest.mock import patch

from fastmcp import FastMCP
from fastmcp.server.auth import StaticTokenVerifier

from sts2_mcp.client import Sts2ApiError
from sts2_mcp.network_server import (
    NetworkServerConfig,
    _build_auth_provider,
    _env_flag,
    _normalize_path,
    create_network_app,
    parse_args,
)


# Every environment variable that parse_args / NetworkServerConfig reads. The helper below
# clears them first so an ambient value from the developer shell cannot make a test lie.
NETWORK_ENV_KEYS = (
    "STS2_NETWORK_HOST",
    "STS2_NETWORK_PORT",
    "STS2_NETWORK_TRANSPORT",
    "STS2_NETWORK_PATH",
    "STS2_MCP_TOOL_PROFILE",
    "STS2_API_BASE_URL",
    "STS2_NETWORK_BEARER_TOKEN",
    "STS2_NETWORK_LOG_LEVEL",
    "STS2_NETWORK_JSON_RESPONSE",
    "STS2_NETWORK_STATELESS_HTTP",
)


@contextmanager
def network_env(**overrides: str) -> Iterator[None]:
    """Snapshot os.environ, drop network keys, then apply the requested overrides."""
    with patch.dict(os.environ, {}, clear=False):
        for key in NETWORK_ENV_KEYS:
            os.environ.pop(key, None)
        os.environ.update(overrides)
        yield


class FakeSts2Client:
    """Stand-in for Sts2Client: records construction kwargs and replays a canned health body."""

    def __init__(
        self,
        base_url: str | None = None,
        read_timeout: float | None = None,
        action_timeout: float | None = None,
        max_retries: int | None = None,
    ) -> None:
        self.base_url = (base_url or "http://127.0.0.1:8080").rstrip("/")
        self.read_timeout = read_timeout
        self.action_timeout = action_timeout
        self.max_retries = max_retries
        self.health_response: Any = {"ok": True, "mod_loaded": True}
        self.calls: list[str] = []

    def get_health(self) -> dict[str, Any]:
        self.calls.append("get_health")
        if isinstance(self.health_response, BaseException):
            raise self.health_response
        return self.health_response

    def get_state(self) -> dict[str, Any]:
        self.calls.append("get_state")
        return {"screen": "MAIN_MENU"}

    def get_available_actions(self) -> list[dict[str, Any]]:
        return []


def build_network_app(config: NetworkServerConfig) -> tuple[FastMCP, FakeSts2Client, Any, list[FakeSts2Client]]:
    """Build the network app with an injected client factory instead of a real HTTP client."""
    created: list[FakeSts2Client] = []

    def factory(**kwargs: Any) -> FakeSts2Client:
        instance = FakeSts2Client(**kwargs)
        created.append(instance)
        return instance

    with patch("sts2_mcp.network_server.Sts2Client", side_effect=factory):
        server, client, app = create_network_app(config)
    return server, client, app, created


def find_route(app: Any, path: str) -> Any:
    for route in app.routes:
        if getattr(route, "path", None) == path:
            return route
    raise AssertionError(f"route {path!r} is not registered")


def call_route_endpoint(app: Any, path: str) -> Any:
    route = find_route(app, path)
    return asyncio.run(route.endpoint(None))


def response_payload(response: Any) -> dict[str, Any]:
    return json.loads(response.body.decode("utf-8"))


class NetworkServerConfigTests(unittest.TestCase):
    def test_defaults_are_loopback_and_unauthenticated(self) -> None:
        config = NetworkServerConfig()

        self.assertEqual(config.host, "127.0.0.1")
        self.assertEqual(config.port, 8765)
        self.assertEqual(config.transport, "streamable-http")
        self.assertEqual(config.path, "/mcp")
        self.assertEqual(config.tool_profile, "guided")
        self.assertEqual(config.api_base_url, "http://127.0.0.1:8080")
        self.assertEqual(config.bearer_token, "")
        self.assertEqual(config.log_level, "info")
        self.assertFalse(config.json_response)
        self.assertFalse(config.stateless_http)
        self.assertFalse(config.auth_enabled)

    def test_auth_enabled_tracks_bearer_token(self) -> None:
        self.assertTrue(NetworkServerConfig(bearer_token="shared-secret").auth_enabled)


class EnvFlagTests(unittest.TestCase):
    def test_truth_table(self) -> None:
        cases = (
            ("1", True),
            ("true", True),
            ("True", True),
            ("TRUE", True),
            ("  yes  ", True),
            ("on", True),
            ("0", False),
            ("false", False),
            ("no", False),
            ("2", False),
            ("enabled", False),
            ("", False),
            ("   ", False),
        )
        for raw, expected in cases:
            with self.subTest(raw=raw):
                with network_env():
                    os.environ["STS2_PROBE_FLAG"] = raw
                    self.assertEqual(_env_flag("STS2_PROBE_FLAG"), expected)

    def test_default_is_used_when_unset_or_empty(self) -> None:
        with network_env():
            self.assertFalse(_env_flag("STS2_PROBE_FLAG"))
            self.assertTrue(_env_flag("STS2_PROBE_FLAG", True))

        with network_env():
            os.environ["STS2_PROBE_FLAG"] = ""
            self.assertTrue(_env_flag("STS2_PROBE_FLAG", True))
            self.assertFalse(_env_flag("STS2_PROBE_FLAG", False))

    def test_whitespace_only_value_is_not_treated_as_unset(self) -> None:
        # Only a truly empty value falls back to the default; whitespace is an explicit
        # non-truthy value, so the default does not apply.
        with network_env():
            os.environ["STS2_PROBE_FLAG"] = "   "
            self.assertFalse(_env_flag("STS2_PROBE_FLAG", True))


class NormalizePathTests(unittest.TestCase):
    def test_edge_cases(self) -> None:
        cases = (
            ("mcp", "/mcp"),
            ("/mcp", "/mcp"),
            ("  /mcp  ", "/mcp"),
            ("/mcp/", "/mcp"),
            ("//mcp", "/mcp"),
            ("//mcp//", "/mcp"),
            ("custom/path", "/custom/path"),
            ("/custom/path/", "/custom/path"),
            ("", "/mcp"),
            ("   ", "/mcp"),
        )
        for raw, expected in cases:
            with self.subTest(raw=raw):
                self.assertEqual(_normalize_path(raw), expected)


class AuthProviderTests(unittest.TestCase):
    def test_empty_token_returns_no_provider(self) -> None:
        self.assertIsNone(_build_auth_provider(NetworkServerConfig()))

    def test_token_returns_static_token_verifier(self) -> None:
        provider = _build_auth_provider(NetworkServerConfig(bearer_token="shared-secret"))

        self.assertIsInstance(provider, StaticTokenVerifier)
        self.assertIn("shared-secret", provider.tokens)


class ParseArgsTests(unittest.TestCase):
    def test_builtin_defaults(self) -> None:
        with network_env():
            config = parse_args([])

        self.assertEqual(config.host, "127.0.0.1")
        self.assertEqual(config.port, 8765)
        self.assertEqual(config.transport, "streamable-http")
        self.assertEqual(config.path, "/mcp")
        self.assertEqual(config.tool_profile, "guided")
        self.assertEqual(config.api_base_url, "http://127.0.0.1:8080")
        self.assertEqual(config.bearer_token, "")
        self.assertEqual(config.log_level, "info")
        self.assertFalse(config.json_response)
        self.assertFalse(config.stateless_http)

    def test_defaults_come_from_environment(self) -> None:
        with network_env(
            STS2_NETWORK_HOST="0.0.0.0",
            STS2_NETWORK_PORT="9100",
            STS2_NETWORK_TRANSPORT="http",
            STS2_NETWORK_PATH="/remote/mcp/",
            STS2_MCP_TOOL_PROFILE="layered",
            STS2_API_BASE_URL="http://127.0.0.1:9000",
            STS2_NETWORK_BEARER_TOKEN="  env-token  ",
            STS2_NETWORK_LOG_LEVEL="debug",
            STS2_NETWORK_JSON_RESPONSE="yes",
            STS2_NETWORK_STATELESS_HTTP="1",
        ):
            config = parse_args([])

        self.assertEqual(config.host, "0.0.0.0")
        self.assertEqual(config.port, 9100)
        self.assertEqual(config.transport, "http")
        self.assertEqual(config.path, "/remote/mcp")
        self.assertEqual(config.tool_profile, "layered")
        self.assertEqual(config.api_base_url, "http://127.0.0.1:9000")
        self.assertEqual(config.bearer_token, "env-token")
        self.assertTrue(config.auth_enabled)
        self.assertEqual(config.log_level, "debug")
        self.assertTrue(config.json_response)
        self.assertTrue(config.stateless_http)

    def test_cli_overrides_environment(self) -> None:
        with network_env(STS2_NETWORK_PORT="9100", STS2_NETWORK_HOST="10.0.0.1"):
            config = parse_args(["--port", "9200", "--host", "127.0.0.2"])

        self.assertEqual(config.host, "127.0.0.2")
        self.assertEqual(config.port, 9200)

    def test_path_argument_is_normalized(self) -> None:
        with network_env():
            config = parse_args(["--path", "custom/mcp/"])

        self.assertEqual(config.path, "/custom/mcp")

    def test_bearer_token_argument_is_stripped(self) -> None:
        with network_env():
            config = parse_args(["--bearer-token", "  abc  "])

        self.assertEqual(config.bearer_token, "abc")
        self.assertTrue(config.auth_enabled)

    def test_boolean_flags(self) -> None:
        with network_env():
            config = parse_args(["--json-response", "--stateless-http"])

        self.assertTrue(config.json_response)
        self.assertTrue(config.stateless_http)

    def test_sse_without_stateless_http_is_allowed(self) -> None:
        with network_env():
            config = parse_args(["--transport", "sse"])

        self.assertEqual(config.transport, "sse")
        self.assertFalse(config.stateless_http)

    def test_sse_with_stateless_http_exits_with_code_2(self) -> None:
        with network_env(), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as ctx:
                parse_args(["--transport", "sse", "--stateless-http"])

        self.assertEqual(ctx.exception.code, 2)

    def test_environment_sse_with_stateless_http_exits_with_code_2(self) -> None:
        with (
            network_env(
                STS2_NETWORK_TRANSPORT="sse",
                STS2_NETWORK_STATELESS_HTTP="1",
            ),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            with self.assertRaises(SystemExit) as ctx:
                parse_args([])

        self.assertEqual(ctx.exception.code, 2)


class NetworkAppTests(unittest.TestCase):
    def test_create_network_app_returns_server_client_app_and_routes(self) -> None:
        config = NetworkServerConfig()
        server, client, app, created = build_network_app(config)

        self.assertIsInstance(server, FastMCP)
        self.assertIs(client, created[0])
        self.assertEqual(len(created), 2, "create_network_app builds a main and a health client")
        self.assertEqual(created[1].read_timeout, 1.5)
        self.assertEqual(created[1].action_timeout, 1.5)
        self.assertEqual(created[1].max_retries, 0)
        self.assertEqual(created[0].base_url, config.api_base_url)

        paths = {getattr(route, "path", None) for route in app.routes}
        self.assertIn("/", paths)
        self.assertIn("/healthz", paths)
        self.assertIn(config.path, paths)

    def test_root_endpoint_reports_transport_configuration(self) -> None:
        config = NetworkServerConfig(
            transport="sse",
            path="/custom/mcp",
            tool_profile="layered",
            bearer_token="shared-secret",
        )
        _, _, app, _ = build_network_app(config)

        response = call_route_endpoint(app, "/")
        payload = response_payload(response)

        self.assertEqual(response.status_code, 200)
        self.assertTrue(payload["ok"])
        self.assertEqual(payload["service"], "sts2-network-mcp")
        self.assertEqual(payload["transport"], "sse")
        self.assertEqual(payload["mcp_path"], "/custom/mcp")
        self.assertEqual(payload["healthz_path"], "/healthz")
        self.assertTrue(payload["auth_enabled"])
        self.assertEqual(payload["tool_profile"], "layered")

    def test_auth_provider_is_attached_when_token_configured(self) -> None:
        server, _, _, _ = build_network_app(NetworkServerConfig(bearer_token="shared-secret"))

        self.assertIsInstance(server.auth, StaticTokenVerifier)
        self.assertIn("shared-secret", server.auth.tokens)

    def test_healthz_success_returns_200_and_sts2_envelope(self) -> None:
        config = NetworkServerConfig()
        _, _, app, created = build_network_app(config)
        health_client = created[1]
        health_client.health_response = {"ok": True, "mod_loaded": True}

        response = call_route_endpoint(app, "/healthz")
        payload = response_payload(response)

        self.assertEqual(response.status_code, 200)
        self.assertTrue(payload["ok"])
        self.assertEqual(payload["sts2"], {"ok": True, "mod_loaded": True})
        self.assertEqual(payload["api_base_url"], config.api_base_url)
        self.assertEqual(health_client.calls, ["get_health"])

    def test_healthz_reports_sts2_api_error_with_503(self) -> None:
        _, _, app, created = build_network_app(NetworkServerConfig())
        health_client = created[1]
        health_client.health_response = Sts2ApiError(
            503,
            "mod_unavailable",
            "mod not reachable",
            retryable=True,
        )

        response = call_route_endpoint(app, "/healthz")
        payload = response_payload(response)

        self.assertEqual(response.status_code, 503)
        self.assertFalse(payload["ok"])
        self.assertEqual(payload["error"]["type"], "sts2_api_error")
        self.assertEqual(payload["error"]["status_code"], 503)
        self.assertEqual(payload["error"]["code"], "mod_unavailable")
        self.assertEqual(payload["error"]["message"], "mod not reachable")
        self.assertTrue(payload["error"]["retryable"])

    def test_healthz_wraps_unexpected_errors_with_500(self) -> None:
        _, _, app, created = build_network_app(NetworkServerConfig())
        health_client = created[1]
        health_client.health_response = RuntimeError("boom")

        response = call_route_endpoint(app, "/healthz")
        payload = response_payload(response)

        self.assertEqual(response.status_code, 500)
        self.assertFalse(payload["ok"])
        self.assertEqual(payload["error"]["type"], "RuntimeError")
        self.assertEqual(payload["error"]["message"], "boom")


if __name__ == "__main__":
    unittest.main()
