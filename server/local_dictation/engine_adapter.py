"""Reusable PCM adapter around the measured original-weight engine."""
from io import BytesIO
import wave

class EngineAdapter:
    def __init__(self, engine):
        self.engine = engine
        self.catalog = None

    def register(self, model):
        if self.catalog:
            import model_engine
            model_engine.MODELS[model] = self.catalog.require(model)["path"]

    def transcribe_pcm(self, pcm, model, language, cancelled):
        if cancelled.is_set():
            raise RuntimeError("Cancelled.")
        stream = BytesIO()
        with wave.open(stream, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(16000)
            wav.writeframes(pcm)
        self.register(model)
        return self.engine.transcribe(stream.getvalue(), "", model, language, cancel_event=cancelled)

    def transcribe(self, data, prompt, model, language, cancelled):
        self.register(model)
        return self.engine.transcribe(data, prompt, model, language, cancel_event=cancelled)

    def unload(self):
        self.engine.unload()

    def status(self):
        return self.engine.status()
