"""Bounded CPU-only Lili synthesis for the loopback candidate."""
import asyncio
import io
import json
from pathlib import Path
import subprocess
import threading
import wave

from starlette.responses import Response

from .workers import run_worker


MAX_TEXT = 500
MAX_REQUEST_BYTES = 8192
SYNTHESIS_TIMEOUT = 35


class PiperCpuSpeech:
    def __init__(self, profile):
        configured = json.loads(Path(profile).read_text(encoding="utf-8-sig"))
        self.runtime = Path(configured["python"])
        self.model = Path(configured["model"])
        self.adapter = (Path(__file__).resolve().parents[2] / "devices" / "kubo-note"
                        / "mac-bridge" / "speech_piper.py")
        if (not self.runtime.is_absolute() or not self.runtime.is_file()
                or not self.model.is_absolute() or self.model.name != "sk_SK-lili-medium.onnx"
                or not self.model.is_file() or not self.model.with_suffix(".onnx.json").is_file()):
            raise ValueError("The local Piper runtime and Slovak Lili model are required.")

    def __call__(self, text):
        audio = subprocess.run([str(self.runtime), "-I", "-B", str(self.adapter), str(self.model), "--stdout"],
                               input=text.encode("utf-8"), capture_output=True, check=True,
                               timeout=SYNTHESIS_TIMEOUT,
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)).stdout
        with wave.open(io.BytesIO(audio), "rb") as wav:
            if (wav.getparams()[:3] != (1, 2, 24000) or not wav.getnframes()
                    or len(wav.readframes(wav.getnframes())) != 2 * wav.getnframes()):
                raise ValueError("The voice produced an invalid WAV.")
        return audio


def make_speech_endpoint(synthesize, error):
    admission = asyncio.Lock()

    async def speech(request):
        data = bytearray()
        async for chunk in request.stream():
            if len(data) + len(chunk) > MAX_REQUEST_BYTES:
                return error("The speech request is too large.", 413)
            data.extend(chunk)
        try:
            options = json.loads(data)
            if not isinstance(options, dict) or options.keys() - {"input", "language", "voice", "compute"}:
                raise ValueError
            text = options.get("input")
            if not isinstance(text, str) or not text.strip() or len(text) > MAX_TEXT:
                return error(f"Enter speech text of 1 to {MAX_TEXT} characters.")
            text.encode("utf-8")
        except (ValueError, UnicodeError):
            return error("Send a valid JSON speech request.")
        if (options.get("language", "sk") not in ("sk", "sk-SK") or options.get("voice", "lili") != "lili"
                or options.get("compute", "cpu") != "cpu"):
            return error("This candidate supports Slovak Lili on CPU.")
        if admission.locked():
            return error("Speech synthesis is already running.", 409)
        async with admission:
            try:
                wav = await run_worker(synthesize, text.strip(), cancelled=threading.Event(),
                                       timeout=SYNTHESIS_TIMEOUT + 2)
            except TimeoutError:
                return error("Local speech synthesis timed out.", 504)
            except (OSError, ValueError, wave.Error, subprocess.SubprocessError):
                return error("Local Piper synthesis failed.", 503)
        return Response(wav, media_type="audio/wav", headers={"X-Speech-Backend": "cpu", "Cache-Control": "no-store"})

    return speech
