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
            entry=self.catalog.require(model)
            model_engine.MODELS[model] = entry["path"]
            model_engine.MODEL_LANGUAGES[model] = entry["languages"]

    def _call(self, data, prompt, model, language, cancelled, preferred_languages=None):
        failure=None
        result=None
        try:
            options={"cancel_event":cancelled}
            if preferred_languages is not None:options["preferred_languages"]=preferred_languages
            result=self.engine.transcribe(data,prompt,model,language,**options)
        except ValueError as exc:
            failure=(ValueError,str(exc))
        except Exception:
            failure=(RuntimeError,"Local model inference failed or was cancelled.")
        # Leave the original exception scope before raising across the worker
        # boundary. Its traceback may own CUDA tensors from generation frames.
        if failure:
            raise failure[0](failure[1]) from None
        return result

    def transcribe_pcm(self, pcm, model, language, cancelled, preferred_languages=None):
        if cancelled.is_set():
            raise RuntimeError("Cancelled.")
        stream = BytesIO()
        with wave.open(stream, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(16000)
            wav.writeframes(pcm)
        self.register(model)
        return self._call(stream.getvalue(),"",model,language,cancelled,preferred_languages)

    def transcribe(self, data, prompt, model, language, cancelled, preferred_languages=None):
        self.register(model)
        return self._call(data,prompt,model,language,cancelled,preferred_languages)

    def unload(self):
        self.engine.unload()

    def forget_model(self,model):
        import model_engine
        self.engine.forget_model(model)
        model_engine.MODELS.pop(model,None)
        model_engine.MODEL_LANGUAGES.pop(model,None)

    def status(self):
        return self.engine.status()
