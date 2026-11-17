"""Verify retained original audio against immutable, already received primary text."""
import asyncio
from io import BytesIO
import json
import re
import threading
import wave

from starlette.responses import JSONResponse
from language_policy import normalize_language, preferred_for_model
from .catalog import canonical_model_id
from .comparison import compare, comparison_identity
from .installation import supports_language
from .sessions import BusyError
from .uploads import read_multipart
from .workers import run_worker, join


TEXT_LIMIT = 64 * 1024


def retained_duration(audio, config):
    try:
        with wave.open(BytesIO(audio), "rb") as reader:
            frames = reader.getnframes()
            if (reader.getnchannels(), reader.getsampwidth(), reader.getframerate(), reader.getcomptype()) != (1, 2, 16000, "NONE"):
                raise ValueError("Expected original mono 16 kHz PCM16 WAV audio.")
            if not 0 < frames <= config.max_seconds * config.sample_rate:
                raise ValueError("Audio is empty or exceeds the recording limit.")
            if len(reader.readframes(frames)) != frames * 2:
                raise ValueError("The WAV recording is incomplete.")
            return frames / config.sample_rate
    except (wave.Error, EOFError):
        raise ValueError("Expected a complete WAV recording.") from None


async def transcribe_connected(request, engine, arguments, cancelled, timeout):
    async def disconnect():
        while True:
            message = await request.receive()
            if message["type"] == "http.disconnect":
                cancelled.set()
                return

    work = asyncio.create_task(run_worker(engine.transcribe, *arguments, cancelled=cancelled, timeout=timeout))
    connection = asyncio.create_task(disconnect())
    try:
        await asyncio.wait((work, connection), return_when=asyncio.FIRST_COMPLETED)
        return await work
    finally:
        connection.cancel()
        await asyncio.gather(connection, return_exceptions=True)
        if not work.done():
            cancelled.set()
            try:
                await join(work)
            except Exception:
                pass


def create_verification_endpoint(config, engine, catalog, admission, ready, error):
    async def verification(request):
        cancelled = threading.Event()
        try:
            if not ready():
                raise BusyError("The API is preparing or unavailable.")
            async with admission.own("verification", cancelled):
                needs_cleanup = False
                try:
                    fields, audio = await read_multipart(request, {
                        "model": 256, "language": 64, "preferred_languages": 16384,
                        "primary": TEXT_LIMIT, "primary_model": 256, "primary_revision": 128})
                    primary = fields.get("primary", "")
                    source = canonical_model_id(fields.get("primary_model", ""))
                    if not primary.strip():
                        raise ValueError("Existing primary text is required for verification.")
                    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*(?:/[A-Za-z0-9][A-Za-z0-9_.-]*)?", source):
                        raise ValueError("The original primary model ID is required.")
                    source_revision = fields.get("primary_revision") or None
                    if source_revision and any(ord(character) < 32 for character in source_revision):
                        raise ValueError("Invalid primary model revision.")
                    model = catalog.require(fields.get("model", ""))
                    if source == model["id"]:
                        raise ValueError("Choose a different verification model.")
                    if not model.get("runtime_tested") or model.get("compatible") is False:
                        raise ValueError("Test this verification model with an audio file before using it.")
                    requested = normalize_language(fields.get("language", "auto")) or "auto"
                    language = supports_language(model, requested)
                    try:
                        preferences = json.loads(fields["preferred_languages"]) if "preferred_languages" in fields else None
                    except ValueError:
                        raise ValueError("preferred_languages must be a JSON array of language codes.") from None
                    preferred = preferred_for_model(model["languages"], preferences, requested == "auto")
                    duration = retained_duration(audio, config)
                    revision = model.get("installed_revision")
                    arguments = (audio, "", model["id"], language, cancelled)
                    if language == "auto" and preferred is not None:
                        arguments += (preferred,)
                    if cancelled.is_set():
                        raise BusyError("Verification was cancelled. Your primary text is preserved.")
                    needs_cleanup = True
                    result = await transcribe_connected(request, engine, arguments, cancelled, config.inference_timeout_seconds)
                    if cancelled.is_set():
                        raise BusyError("Verification was cancelled. Your primary text is preserved.")
                    text = result["text"]
                    if not isinstance(text, str) or len(text.encode("utf-8")) > TEXT_LIMIT:
                        raise RuntimeError("Invalid verification text.")
                    pieces = await run_worker(compare, primary, text, cancelled=cancelled,
                                              timeout=config.inference_timeout_seconds)
                    if cancelled.is_set():
                        raise BusyError("Verification was cancelled. Your primary text is preserved.")
                    return JSONResponse({"primary": primary, "verification": text, "pieces": pieces,
                        "primary_model": source, "primary_revision": source_revision,
                        "verification_model": model["id"], "verification_revision": revision,
                        "comparison_id": comparison_identity(source, source_revision, model["id"], revision, primary, text),
                        "language": result.get("language", None if language == "auto" else language),
                        "language_mode": result.get("language_mode", "model_fixed" if len([code for code in model["languages"] if code != "auto"]) == 1 else "forced" if language != "auto" else None),
                        "duration": duration})
                finally:
                    if needs_cleanup:
                        try:
                            await run_worker(engine.unload, cancelled=cancelled)
                        except Exception:
                            admission.unavailable = True
                            raise RuntimeError("Model cleanup failed.") from None
        except BusyError as exc:
            return error(str(exc), 409)
        except ValueError as exc:
            return error(str(exc))
        except Exception:
            if cancelled.is_set() and not admission.unavailable:
                return error("Verification was cancelled. Your primary text is preserved.", 409)
            return error("Verification failed. Your primary text is preserved.", 500)

    return verification
