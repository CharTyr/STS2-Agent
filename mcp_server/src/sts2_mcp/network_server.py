from __future__ import annotations

import argparse
import asyncio
import logging
import os
from dataclasses import dataclass

import uvicorn
from fastmcp import FastMCP
from fastmcp.server.auth import StaticTokenVerifier
from starlette.requests import Request
from starlette.responses import JSONResponse

from .client import Sts2ApiError, Sts2Client
from .server import create_server

logger = logging.getLogger("sts2_mcp.network")


@dataclass(frozen=True, slots=True)
class NetworkServerConfig:
    host: str = "127.0.0.1"
    port: int = 8765
    transport: str = "streamable-http"
    path: str = "/mcp"
    tool_profile: str = "guided"
    api_base_url: str = "http://127.0.0.1:8080"
    bearer_token: str = ""
    log_level: str = "info"
    json_response: bool = False
    stateless_http: bool = False

    @property
    def auth_enabled(self) -> bool:
        return bool(self.bearer_token)


def _env_flag(name: str, default: bool = False) -> bool:
    value = os.getenv(name, "")
    if not value:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def _normalize_path(path: str) -> str:
    stripped = (path or "").strip()
    if not stripped:
        return "/mcp"
    return "/" + stripped.strip("/")


def _build_auth_provider(config: NetworkServerConfig) -> StaticTokenVerifier | None:
    if not config.auth_enabled:
        return None

    return StaticTokenVerifier(
        tokens={
            config.bearer_token: {
                "client_id": "sts2-network-client",
                "scopes": [],
            }
        }
    )


def create_network_app(
    config: NetworkServerConfig,
) -> tuple[FastMCP, Sts2Client, object]:
    client = Sts2Client(base_url=config.api_base_url)
    health_client = Sts2Client(
        base_url=config.api_base_url,
        read_timeout=1.5,
        action_timeout=1.5,
        max_retries=0,
    )
    server = create_server(client=client, tool_profile=config.tool_profile)
    server.auth = _build_auth_provider(config)
    app = server.http_app(
        path=config.path,
        transport=config.transport,
        json_response=config.json_response,
        stateless_http=config.stateless_http,
    )

    async def root_endpoint(_: Request) -> JSONResponse:
        return JSONResponse(
            {
                "ok": True,
                "service": "sts2-network-mcp",
                "transport": config.transport,
                "mcp_path": config.path,
                "healthz_path": "/healthz",
                "auth_enabled": config.auth_enabled,
                "tool_profile": config.tool_profile,
            }
        )

    async def healthz_endpoint(_: Request) -> JSONResponse:
        payload = {
            "ok": True,
            "service": "sts2-network-mcp",
            "transport": config.transport,
            "mcp_path": config.path,
            "auth_enabled": config.auth_enabled,
            "tool_profile": config.tool_profile,
            "api_base_url": client.base_url,
        }
        try:
            payload["sts2"] = health_client.get_health()
            return JSONResponse(payload, status_code=200)
        except Sts2ApiError as exc:
            payload["ok"] = False
            payload["error"] = {
                "type": "sts2_api_error",
                "status_code": exc.status_code,
                "code": exc.code,
                "message": exc.message,
                "details": exc.details,
                "retryable": exc.retryable,
            }
            return JSONResponse(payload, status_code=503)
        except Exception as exc:  # pragma: no cover - defensive wrapper
            payload["ok"] = False
            payload["error"] = {
                "type": exc.__class__.__name__,
                "message": str(exc),
            }
            return JSONResponse(payload, status_code=500)

    app.add_route("/", root_endpoint, methods=["GET"])
    app.add_route("/healthz", healthz_endpoint, methods=["GET"])
    return server, client, app


async def run_network_server_async(config: NetworkServerConfig) -> None:
    _, _, app = create_network_app(config)
    logger.info(
        "Starting STS2 network MCP server on http://%s:%d%s transport=%s auth=%s api=%s",
        config.host,
        config.port,
        config.path,
        config.transport,
        "enabled" if config.auth_enabled else "disabled",
        config.api_base_url,
    )
    uvicorn_config = uvicorn.Config(
        app,
        host=config.host,
        port=config.port,
        timeout_graceful_shutdown=0,
        lifespan="on",
        ws="websockets-sansio",
        log_level=config.log_level.lower(),
    )
    server = uvicorn.Server(uvicorn_config)
    await server.serve()


def parse_args(argv: list[str] | None = None) -> NetworkServerConfig:
    parser = argparse.ArgumentParser(
        description="Expose the local STS2 MCP server over HTTP for remote clients."
    )
    parser.add_argument("--host", default=os.getenv("STS2_NETWORK_HOST", "127.0.0.1"))
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.getenv("STS2_NETWORK_PORT", "8765")),
    )
    parser.add_argument(
        "--transport",
        choices=("streamable-http", "http", "sse"),
        default=os.getenv("STS2_NETWORK_TRANSPORT", "streamable-http"),
    )
    parser.add_argument(
        "--path",
        default=os.getenv("STS2_NETWORK_PATH", "/mcp"),
    )
    parser.add_argument(
        "--tool-profile",
        default=os.getenv("STS2_MCP_TOOL_PROFILE", "guided"),
    )
    parser.add_argument(
        "--api-base-url",
        default=os.getenv("STS2_API_BASE_URL", "http://127.0.0.1:8080"),
    )
    parser.add_argument(
        "--bearer-token",
        default=os.getenv("STS2_NETWORK_BEARER_TOKEN", ""),
    )
    parser.add_argument(
        "--log-level",
        default=os.getenv("STS2_NETWORK_LOG_LEVEL", "info"),
    )
    parser.add_argument(
        "--json-response",
        action="store_true",
        default=_env_flag("STS2_NETWORK_JSON_RESPONSE", False),
    )
    parser.add_argument(
        "--stateless-http",
        action="store_true",
        default=_env_flag("STS2_NETWORK_STATELESS_HTTP", False),
    )
    args = parser.parse_args(argv)

    path = _normalize_path(args.path)
    if args.transport == "sse" and args.stateless_http:
        parser.error("SSE transport does not support --stateless-http")

    return NetworkServerConfig(
        host=args.host,
        port=args.port,
        transport=args.transport,
        path=path,
        tool_profile=args.tool_profile,
        api_base_url=args.api_base_url,
        bearer_token=args.bearer_token.strip(),
        log_level=args.log_level,
        json_response=bool(args.json_response),
        stateless_http=bool(args.stateless_http),
    )


def main(argv: list[str] | None = None) -> None:
    logging.basicConfig(level=logging.INFO)
    asyncio.run(run_network_server_async(parse_args(argv)))


if __name__ == "__main__":
    main()
