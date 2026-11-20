import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
import bridge


@unittest.skipUnless(os.name == "nt", "Windows Bridge profile lock")
class BridgeRuntimeTests(unittest.TestCase):
    def test_managed_status_and_single_instance_until_control_pipe_closes(self):
        program = """
import sys
from pathlib import Path
import bridge

bridge.STATE = Path(sys.argv[1])
stop = bridge.managed_stop_event(sys.stdin.buffer)
def fake_server(config, stop, report):
    report("running")
    stop.wait(15)
bridge.serve_configured_bridge = fake_server
bridge.run_bridge({}, stop)
"""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bridge-runtime.json"
            args = [sys.executable, "-B", "-c", program, directory]
            env = {**os.environ, "PYTHONPATH": str(Path(bridge.__file__).parent),
                   "PALA_BRIDGE_MANAGED_OWNER": "synthetic-owner"}
            with subprocess.Popen(args, env=env, stdin=subprocess.PIPE,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE) as first:
                try:
                    deadline = time.monotonic() + 8
                    while time.monotonic() < deadline and first.poll() is None:
                        if path.exists():
                            with path.open(encoding="utf-8") as stream:
                                state = json.load(stream)
                            if state["State"] == "running":
                                break
                        time.sleep(0.05)
                    else:
                        self.fail("The managed Bridge did not report running.")
                    self.assertEqual(state["Owner"], "synthetic-owner")
                    self.assertEqual(state["Listeners"], [])
                    rival = subprocess.run(args, env=env, input=b"", capture_output=True, timeout=8)
                    self.assertNotEqual(rival.returncode, 0)
                    self.assertIn(b"already running", rival.stderr)
                finally:
                    first.communicate(input=b"", timeout=12)
                self.assertEqual(first.returncode, 0)
            self.assertEqual(json.loads(path.read_text(encoding="utf-8"))["State"], "stopped")
            restarted = subprocess.run(args, env=env, input=b"", capture_output=True, timeout=8)
            self.assertEqual(restarted.returncode, 0)
