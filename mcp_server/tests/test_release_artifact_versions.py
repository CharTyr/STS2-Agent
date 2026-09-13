"""The release artifact must prove its own version consistency.

The packaging check reads the artifact, never the repository, so a zip handed to
someone else has to be verifiable on its own. It used to require only that
mod/mod_id.json carried a non-empty version: a build that copied one stale file
could ship a mod folder and an MCP package that disagree about which release they
are, and nothing noticed. This pins the check that closes that gap, in both
directions: a consistent artifact passes, and one whose files disagree is rejected
with the conflicting values named.
"""

from __future__ import annotations

import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def _load_package_checker():
    spec = importlib.util.spec_from_file_location(
        "check_release_package", ROOT / "scripts" / "check_release_package.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReleaseArtifactVersionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.checker = _load_package_checker()
        self.tmp = Path(tempfile.mkdtemp(prefix="sts2-artifact-"))
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self._build_artifact()

    def _build_artifact(self) -> None:
        """Assemble the shipped layout from the repository, minus the game binaries."""
        # The packaged documents are stubbed: packaging rewrites their relative links to absolute
        # URLs before the artifact exists, and link handling has its own gate. This test is about
        # the version files agreeing, so the documents only need to satisfy the player-facing
        # keywords the artifact check looks for.
        documents = {
            "README.md": "Copy the `mod/` folder into `mods/`, start with Play with Mods, then press Test Connection.\n",
            "README.zh-CN.md": "把 `mod/` 目录放进 `mods/`，用「带 Mod 启动」进入游戏，然后点「测试连接」。\n",
            "mcp_server/README.md": "Run the sidecar MCP server next to the mod.\n",
        }
        for relative in self.checker.ARTIFACT_FILES:
            target = self.tmp / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            if relative in documents:
                target.write_text(documents[relative], encoding="utf-8")
                continue
            source = ROOT / relative
            if relative.startswith("mod/") and source.suffix in {".dll", ".pck"}:
                # The real payload needs the game assemblies to build; presence is all this
                # check asks of these two, and this test is about the version files.
                target.write_bytes(b"fixture")
                continue
            if relative == "mod/mod_id.json":
                source = ROOT / "STS2AIAgent" / "mod_id.json"
            shutil.copyfile(source, target)

    def test_consistent_artifact_passes(self) -> None:
        self.checker._check_artifact(self.tmp)

    def test_mod_folder_disagreeing_with_the_mcp_package_is_rejected(self) -> None:
        path = self.tmp / "mod" / "mod_id.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["version"] = "0.0.1"
        path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

        with self.assertRaises(self.checker.PackageCheckError) as caught:
            self.checker._check_artifact(self.tmp)

        message = str(caught.exception)
        self.assertIn("0.0.1", message)
        self.assertIn("mod/mod_id.json", message)
        self.assertIn("mcp_server/pyproject.toml", message)

    def test_missing_mcp_version_entry_is_rejected(self) -> None:
        path = self.tmp / "mcp_server" / "pyproject.toml"
        path.write_text("[project]\nname = \"sts2-ai-agent-mcp\"\n", encoding="utf-8")

        with self.assertRaises(self.checker.PackageCheckError) as caught:
            self.checker._check_artifact(self.tmp)

        self.assertIn("pyproject.toml", str(caught.exception))


if __name__ == "__main__":
    unittest.main()
