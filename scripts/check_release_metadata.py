"""Validate versioned release metadata without installed game assemblies."""

import json
import re
import tomllib
from pathlib import Path


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    manifest = json.loads((root / "STS2AIAgent/mod_manifest.json").read_text(encoding="utf-8"))
    mod_id = json.loads((root / "STS2AIAgent/mod_id.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[\w.]+)?", version):
        raise ValueError("Invalid mod version")
    project = tomllib.loads((root / "mcp_server/pyproject.toml").read_text(encoding="utf-8"))
    lock = tomllib.loads((root / "mcp_server/uv.lock").read_text(encoding="utf-8"))
    package = next(item for item in lock["package"] if item["name"] == "sts2-ai-agent-mcp")
    router = (root / "STS2AIAgent/Server/Router.cs").read_text(encoding="utf-8")
    if mod_id["version"] != version or project["project"]["version"] != version or package["version"] != version:
        raise ValueError("Manifest, Mod metadata, MCP package and lockfile versions must agree")
    if f'private const string ModVersion = "{version}";' not in router:
        raise ValueError("HTTP API version does not match the manifest")
    print(f"Release metadata consistent: {version}")


if __name__ == "__main__":
    main()
