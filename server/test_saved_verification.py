"""Saved-audio verification contracts; synthetic WAV/catalog only, no inference."""
import asyncio
from io import BytesIO
import json
from pathlib import Path
import tempfile
import threading
import unittest
import wave

import httpx
from starlette.requests import Request
from starlette.testclient import TestClient

from local_dictation.application import create_app
from local_dictation.catalog import BUILTINS, Catalog
from local_dictation.config import RuntimeConfig
from test_model_files import make_model


VERIFIER = "openai/whisper-large-v3"
PRIMARY = "archived/original-model"
SOURCE = "  Ahoj svet.\n"
VERIFIED = "  Ahoj, svet.\n"
ROUTE = "/v1/audio/verifications"


def wav_bytes(seconds=1):
    stream = BytesIO()
    with wave.open(stream, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(16000)
        writer.writeframes(b"\0\x10" * (16000 * seconds))
    return stream.getvalue()


class SyntheticEngine:
    def __init__(self):
        self.calls = []
        self.cleaned = 0
        self.ready = True
        self.failure = None
        self.cleanup_failure = False
        self.started = threading.Event()
        self.release = None
        self.running = False
        self.cleanup_overlap = []

    def status(self):
        return {"ready": self.ready}

    def transcribe(self, data, prompt, model, language, cancelled, preferred_languages=None):
        self.calls.append((data, prompt, model, language, preferred_languages))
        self.running = True
        self.started.set()
        try:
            if self.release is not None:
                while not self.release.wait(0.01):
                    if cancelled.is_set():
                        raise RuntimeError("Cancelled private detail")
            if self.failure:
                raise self.failure
            return {"text": VERIFIED, "duration": 1.0, "language": "sk",
                    "language_mode": "detected" if language == "auto" else "forced"}
        finally:
            self.running = False

    def unload(self):
        self.cleanup_overlap.append(self.running)
        self.cleaned += 1
        if self.cleanup_failure:
            raise RuntimeError("Private cleanup detail")


class Fixture:
    def __init__(self, directory, **options):
        root = Path(directory)
        self.config = RuntimeConfig(root, root / "data", **options)
        builtin = next(model for model in BUILTINS if model["id"] == VERIFIER)
        self.folder = make_model(root / builtin["folder"], VERIFIER, builtin["revision"])
        self.catalog = Catalog(self.config)
        self.engine = SyntheticEngine()
        self.app = create_app(self.config, self.engine, self.catalog)


class SavedVerificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.fixture = Fixture(self.directory.name)
        self.client = self.enterContext(TestClient(self.fixture.app, base_url="http://127.0.0.1"))

    def post(self, audio=None, **fields):
        data = {"model": VERIFIER, "primary": SOURCE, "primary_model": PRIMARY,
                "primary_revision": "a" * 40, "language": "sk", **fields}
        return self.client.post(ROUTE, data=data,
                                files={"file": ("retained.wav", wav_bytes() if audio is None else audio, "audio/wav")})

    def test_only_verifier_reads_original_wav_and_comparison_preserves_source_exactly(self):
        audio = wav_bytes()
        response = self.post(audio)
        self.assertEqual(response.status_code, 200, response.text)
        result = response.json()
        self.assertEqual(result["primary"], SOURCE)
        self.assertEqual(result["verification"], VERIFIED)
        self.assertEqual("".join(piece["primary"] for piece in result["pieces"]), SOURCE)
        self.assertEqual("".join(piece["verification"] for piece in result["pieces"]), VERIFIED)
        differences = [piece for piece in result["pieces"] if piece["different"]]
        self.assertEqual([(piece["primary"], piece["verification"]) for piece in differences], [("", ",")])
        self.assertEqual(result["primary_model"], PRIMARY)
        self.assertEqual(result["primary_revision"], "a" * 40)
        self.assertEqual(result["verification_model"], VERIFIER)
        self.assertEqual(result["verification_revision"], "06f233fe06e710322aca913c1bc4249a0d71fce1")
        self.assertEqual((result["language"], result["language_mode"], result["duration"]), ("sk", "forced", 1.0))
        self.assertEqual(self.fixture.engine.calls, [(audio, "", VERIFIER, "sk", None)])
        self.assertEqual(self.fixture.engine.cleanup_overlap, [False])
        self.assertFalse(self.client.get("/health").json()["busy"])
        self.assertFalse(self.fixture.config.data_root.exists())

    def test_comparison_identity_changes_with_source_revision_but_not_pieces(self):
        first = self.post().json()
        second = self.post(primary_revision="b" * 40).json()
        self.assertRegex(first["comparison_id"], r"^[0-9a-f]{64}$")
        self.assertNotEqual(first["comparison_id"], second["comparison_id"])
        self.assertEqual(first["pieces"], second["pieces"])

    def test_source_revision_can_be_unknown_without_substituting_current_catalog_revision(self):
        response = self.post(primary_revision="")
        self.assertEqual(response.status_code, 200, response.text)
        self.assertIsNone(response.json()["primary_revision"])

    def test_same_model_is_rejected_even_when_source_uses_legacy_alias(self):
        for source in (VERIFIER, "whisper-large"):
            with self.subTest(source=source):
                response = self.post(primary_model=source)
                self.assertEqual(response.status_code, 400, response.text)
        self.assertEqual(self.fixture.engine.calls, [])

    def test_uninstalled_or_untested_verifier_is_rejected_before_loading(self):
        self.fixture.catalog.entries[VERIFIER]["runtime_tested"] = False
        self.assertEqual(self.post().status_code, 400)
        self.fixture.catalog.entries[VERIFIER]["runtime_tested"] = True
        (self.fixture.folder / "model.safetensors").unlink()
        self.assertEqual(self.post().status_code, 400)
        self.assertEqual(self.fixture.engine.calls, [])

    def test_unsupported_language_and_invalid_preferences_are_rejected_before_loading(self):
        for fields in ({"language": "es"}, {"language": "auto", "preferred_languages": '["es"]'},
                       {"preferred_languages": "not-json"}, {"primary": ""}, {"primary_model": ""}):
            with self.subTest(fields=fields):
                self.assertEqual(self.post(**fields).status_code, 400)
        self.assertEqual(self.fixture.engine.calls, [])

    def test_auto_passes_only_supported_preferences_to_second_model(self):
        response = self.post(language="auto", preferred_languages='["en","sk","es"]')
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(self.fixture.engine.calls[0][2:], (VERIFIER, "auto", ["en", "sk"]))
        self.assertEqual(response.json()["language_mode"], "detected")

    def test_oversized_or_duplicate_source_text_is_rejected(self):
        response = self.post(primary="á" * 32769)
        self.assertEqual(response.status_code, 400)
        request = httpx.Request("POST", "http://127.0.0.1" + ROUTE,
            data={"model": VERIFIER, "primary_model": PRIMARY},
            files=[("file", ("retained.wav", wav_bytes(), "audio/wav")),
                   ("primary", (None, SOURCE)), ("primary", (None, "another"))])
        response = self.client.send(request)
        self.assertEqual(response.status_code, 400)
        self.assertEqual(self.fixture.engine.calls, [])

    def test_invalid_or_over_duration_wav_is_rejected_before_loading(self):
        for audio in (b"not a wav", wav_bytes()[:-2]):
            self.assertEqual(self.post(audio).status_code, 400)
        with tempfile.TemporaryDirectory() as directory:
            fixture = Fixture(directory, max_seconds=1)
            with TestClient(fixture.app, base_url="http://127.0.0.1") as client:
                response = client.post(ROUTE, data={"model": VERIFIER, "primary": SOURCE,
                    "primary_model": PRIMARY}, files={"file": ("long.wav", wav_bytes(2))})
                self.assertEqual(response.status_code, 400)
                self.assertEqual(fixture.engine.calls, [])

    def test_preparing_rejects_verification_without_unloading_or_inference(self):
        self.fixture.engine.ready = False
        self.assertEqual(self.post().status_code, 409)
        self.assertEqual(self.fixture.engine.calls, [])
        self.assertEqual(self.fixture.engine.cleaned, 0)

    def test_malformed_or_unbounded_upload_is_rejected_before_inference(self):
        for headers in ({"Content-Length": str(26 * 1024 * 1024 + 1)},
                        {"Content-Length": "invalid"}, {"Content-Length": "0"},
                        {"Transfer-Encoding": "chunked"}):
            with self.subTest(headers=headers):
                response = self.client.post(ROUTE, content=b"synthetic", headers=headers)
                self.assertEqual(response.status_code, 400)
        self.assertEqual(self.fixture.engine.calls, [])
        self.assertEqual(self.fixture.engine.cleaned, 0)

    def test_new_recording_preempts_optional_verification_after_joined_cleanup(self):
        self.fixture.engine.release = threading.Event()
        responses = []
        thread = threading.Thread(target=lambda: responses.append(self.post()))
        thread.start()
        try:
            self.assertTrue(self.fixture.engine.started.wait(2))
            with self.client.websocket_connect("ws://127.0.0.1/v1/audio/sessions") as socket:
                socket.send_json({"type": "start", "protocol_version": 1,
                                  "model": VERIFIER, "language": "sk", "live": False})
                self.assertEqual(socket.receive_json()["type"], "session.started")
                socket.send_json({"type": "cancel"})
                self.assertEqual(socket.receive_json()["type"], "session.cancelled")
        finally:
            self.fixture.engine.release.set()
            thread.join(3)
        self.assertFalse(thread.is_alive())
        self.assertEqual(responses[0].status_code, 409)
        self.assertEqual(self.fixture.engine.cleanup_overlap, [False])
        self.assertFalse(self.client.get("/health").json()["busy"])

    def test_busy_rejects_second_job_and_health_remains_responsive(self):
        self.fixture.engine.release = threading.Event()
        responses = []
        thread = threading.Thread(target=lambda: responses.append(self.post()))
        thread.start()
        try:
            self.assertTrue(self.fixture.engine.started.wait(2))
            health = self.client.get("/health").json()
            self.assertTrue(health["busy"])
            self.assertEqual(health["phase"], "verification")
            self.assertEqual(self.post().status_code, 409)
            self.assertEqual(self.fixture.engine.cleaned, 0)
        finally:
            self.fixture.engine.release.set()
            thread.join(3)
        self.assertFalse(thread.is_alive())
        self.assertEqual(responses[0].status_code, 200)
        self.assertEqual(self.fixture.engine.cleaned, 1)

    def test_inference_failure_returns_safe_error_after_model_cleanup(self):
        self.fixture.engine.failure = RuntimeError("Private inference detail " + SOURCE)
        response = self.post()
        self.assertEqual(response.status_code, 500)
        self.assertNotIn("Private inference", response.text)
        self.assertNotIn("Ahoj", response.text)
        self.assertEqual(self.fixture.engine.cleaned, 1)
        self.assertFalse(self.client.get("/health").json()["busy"])

    def test_failed_cleanup_marks_api_unavailable_and_does_not_return_comparison(self):
        self.fixture.engine.cleanup_failure = True
        response = self.post()
        self.assertEqual(response.status_code, 500)
        self.assertNotIn("verification", response.json())
        health = self.client.get("/health").json()
        self.assertFalse(health["ready"])
        self.assertFalse(health["busy"])
        self.assertEqual(self.post().status_code, 409)


class DisconnectTests(unittest.IsolatedAsyncioTestCase):
    async def test_cancelled_upload_does_not_activate_the_second_model(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = Fixture(directory)
            wire = httpx.Request("POST", "http://127.0.0.1" + ROUTE,
                data={"model": VERIFIER, "primary": SOURCE, "primary_model": PRIMARY},
                files={"file": ("retained.wav", wav_bytes())})
            body = wire.read()
            received = False
            async def receive():
                nonlocal received
                if received:
                    await asyncio.Future()
                received = True
                fixture.app.app.state.admission.cancelled.set()
                return {"type": "http.request", "body": body, "more_body": False}
            scope = {"type": "http", "method": "POST", "path": ROUTE,
                     "headers": [(name.lower(), value) for name, value in wire.headers.raw],
                     "scheme": "http", "query_string": b"", "server": ("127.0.0.1", 80)}
            route = next(route for route in fixture.app.app.routes if route.path == ROUTE)
            response = await route.endpoint(Request(scope, receive))
            self.assertEqual(response.status_code, 409)
            self.assertEqual(fixture.engine.calls, [])
            self.assertEqual(fixture.engine.cleaned, 0)

    async def test_disconnect_cancels_and_joins_inference_before_cleanup_and_releasing_admission(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = Fixture(directory)
            fixture.engine.release = threading.Event()
            wire = httpx.Request("POST", "http://127.0.0.1" + ROUTE,
                data={"model": VERIFIER, "primary": SOURCE, "primary_model": PRIMARY, "language": "sk"},
                files={"file": ("retained.wav", wav_bytes())})
            body = wire.read()
            received = False
            async def receive():
                nonlocal received
                if not received:
                    received = True
                    return {"type": "http.request", "body": body, "more_body": False}
                await asyncio.to_thread(fixture.engine.started.wait, 2)
                return {"type": "http.disconnect"}
            scope = {"type": "http", "method": "POST", "path": ROUTE,
                     "headers": [(name.lower(), value) for name, value in wire.headers.raw],
                     "scheme": "http", "query_string": b"",
                     "server": ("127.0.0.1", 80)}
            route = next((route for route in fixture.app.app.routes if route.path == ROUTE), None)
            self.assertIsNotNone(route, "Saved-audio verification route is missing")
            response = await asyncio.wait_for(route.endpoint(Request(scope, receive)), 3)
            self.assertEqual(response.status_code, 409)
            self.assertEqual(fixture.engine.cleanup_overlap, [False])
            self.assertFalse(fixture.app.app.state.admission.lock.locked())


if __name__ == "__main__":
    unittest.main()
