import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from sts2_mcp.discovery import discover_base_url
from sts2_mcp.client import Sts2Client


class DiscoveryTests(unittest.TestCase):
    def test_live_identity_and_role_are_checked_and_explicit_url_wins(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            for pid, role, port in [(1, "human", 19000), (2, "companion", 29000), (3, "human", 39000)]:
                Path(directory, f"sts2-ai-agent-instance-{pid}.json").write_text(json.dumps(
                    {"process_id": pid, "instance_role": role, "api_port": port}))

            def health(url, timeout):
                port = int(url.split(":")[2].split("/")[0])
                # Stale file 3 now points to a different process.
                pid, role = {19000: (1, "human"), 29000: (2, "companion"), 39000: (4, "human")}[port]
                return io.BytesIO(json.dumps({"ok": True, "data": {"service": "sts2-ai-agent", "status": "ready",
                    "process_id": pid, "instance_role": role, "api_port": port}}).encode())

            with patch("sts2_mcp.discovery.tempfile.gettempdir", return_value=directory), patch(
                    "sts2_mcp.discovery.request.urlopen", side_effect=health):
                self.assertEqual(discover_base_url(), "http://127.0.0.1:19000")
                with patch.dict(os.environ, {"STS2_API_INSTANCE": "companion"}):
                    self.assertEqual(discover_base_url(), "http://127.0.0.1:29000")
                with patch.dict(os.environ, {"STS2_API_INSTANCE": "999"}):
                    with self.assertRaises(ValueError):
                        discover_base_url()
                    self.assertEqual(Sts2Client(base_url="http://localhost:18000").base_url, "http://localhost:18000")

    def test_ambiguous_hosts_are_not_silently_selected(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            for pid in (1, 2):
                Path(directory, f"sts2-ai-agent-instance-{pid}.json").write_text(json.dumps(
                    {"process_id": pid, "instance_role": "human", "api_port": 19000 + pid}))

            def health(url, timeout):
                port = int(url.split(":")[2].split("/")[0])
                return io.BytesIO(json.dumps({"ok": True, "data": {"service": "sts2-ai-agent", "status": "degraded",
                    "process_id": port - 19000, "instance_role": "human", "api_port": port}}).encode())

            with patch("sts2_mcp.discovery.tempfile.gettempdir", return_value=directory), patch(
                    "sts2_mcp.discovery.request.urlopen", side_effect=health):
                with self.assertRaisesRegex(ValueError, "Multiple"):
                    discover_base_url()

    def test_malformed_hints_do_not_override_legacy_default(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            Path(directory, "sts2-ai-agent-instance-1.json").write_text("null")
            with patch("sts2_mcp.discovery.tempfile.gettempdir", return_value=directory):
                self.assertEqual(discover_base_url(), "http://127.0.0.1:8080")
