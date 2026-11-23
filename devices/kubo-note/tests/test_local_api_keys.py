import os
from pathlib import Path
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))

import bridge


class BridgeApiKeyTest(unittest.TestCase):
    def test_installed_key_stays_on_the_default_loopback_address(self):
        with TemporaryDirectory() as folder:
            credentials = Path(folder) / "api-keys"
            credentials.mkdir()
            key = "installed_" + "x" * 36
            (credentials / "bridge.key").write_text(key + "\n", encoding="ascii")
            with patch.dict(os.environ, {"LOCAL_DICTATION_HOME": folder}, clear=True):
                self.assertEqual(bridge.local_dictation_application_key("127.0.0.1", 48125), key)
                self.assertIsNone(bridge.local_dictation_application_key("127.0.0.1", 48126))

    def test_candidate_uses_the_bridge_credential_and_its_loopback_port(self):
        with TemporaryDirectory() as folder:
            state = Path(folder)
            credential = state / "bridge.key"
            key = "bridge_" + "x" * 36
            credential.write_text(key + "\n", encoding="ascii")
            with patch.dict(os.environ, {"PALA_BRIDGE_LOCAL_API_ENDPOINT": "http://127.0.0.1:48126",
                                         "PALA_BRIDGE_LOCAL_API_KEY_FILE": str(credential),
                                         "LOCAL_DICTATION_HOME": str(state)}), \
                 patch.object(bridge, "local_dictation_health"), \
                 patch.object(bridge.http.client, "HTTPConnection") as connect:
                response = connect.return_value.getresponse.return_value
                response.status = 200
                response.read.return_value = b'{"text":"Test"}'
                self.assertEqual(bridge.Bridge(state, {}).transcribe_local(b"wav"), {"text": "Test"})
                connect.assert_called_once_with("127.0.0.1", 48126, timeout=100)
                method, route, _, headers = connect.return_value.request.call_args.args
                self.assertEqual((method, route), ("POST", "/v1/audio/transcriptions"))
                self.assertEqual(headers["Authorization"], "Bearer " + key)
