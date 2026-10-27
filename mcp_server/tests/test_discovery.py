import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from sts2_mcp.discovery import _process_alive, discover_base_url
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
                    "sts2_mcp.discovery.request.urlopen", side_effect=health), patch(
                    "sts2_mcp.discovery._process_alive", return_value=True):
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
                    "sts2_mcp.discovery.request.urlopen", side_effect=health), patch(
                    "sts2_mcp.discovery._process_alive", return_value=True):
                with self.assertRaisesRegex(ValueError, "Multiple"):
                    discover_base_url()

    def test_malformed_hints_do_not_override_legacy_default(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            Path(directory, "sts2-ai-agent-instance-1.json").write_text("null")
            with patch("sts2_mcp.discovery.tempfile.gettempdir", return_value=directory):
                self.assertEqual(discover_base_url(), "http://127.0.0.1:8080")

    def test_hints_of_exited_processes_are_skipped_without_a_probe(self):
        # A crashed or killed game leaves its hint behind. Enough of them used to push discovery past
        # its deadline, so a machine with no game running failed with "timed out" instead of
        # falling back to the default URL.
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            for pid in range(100, 160):
                Path(directory, f"sts2-ai-agent-instance-{pid}.json").write_text(json.dumps(
                    {"process_id": pid, "instance_role": "human", "api_port": 19000}))

            with patch("sts2_mcp.discovery.tempfile.gettempdir", return_value=directory), patch(
                    "sts2_mcp.discovery._process_alive", return_value=False), patch(
                    "sts2_mcp.discovery.request.urlopen") as urlopen:
                self.assertEqual(discover_base_url(), "http://127.0.0.1:8080")
            urlopen.assert_not_called()

    def test_process_alive_tells_a_running_process_from_an_exited_one(self):
        self.assertTrue(_process_alive(os.getpid()))
        # Far above any real pid limit, so no live process can hold it.
        self.assertFalse(_process_alive(2**31 - 2))
        self.assertFalse(_process_alive(2**40))
