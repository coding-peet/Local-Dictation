"""Benchmark-only original-FP16 profiles. No production engine source changes."""
from copy import deepcopy
from pathlib import Path
import sys
import time
from io import BytesIO

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "server"))
import model_engine as original
from asr import read_audio

TURBO_ID = "openai/whisper-large-v3-turbo"
MODEL_IDS = [original.FAST_MODEL_ID, original.PRECISE_MODEL_ID, TURBO_ID]
MODEL_PATHS = {
    original.FAST_MODEL_ID: Path(r"C:\AI\PalaNote-ASR\models\kinit-turbo"),
    original.PRECISE_MODEL_ID: Path(r"C:\AI\PalaNote-ASR\models\whisper-large"),
    TURBO_ID: Path(r"C:\AI\LocalDictation\models\db16fd52f1ad72a2-41f01f3fe87f28c78e2fbf8b568835947dd65ed9"),
}
REVISIONS = {
    original.FAST_MODEL_ID: "eb2c35bf0971f836fbf045ebe6e897826172d5d7",
    original.PRECISE_MODEL_ID: "06f233fe06e710322aca913c1bc4249a0d71fce1",
    TURBO_ID: "41f01f3fe87f28c78e2fbf8b568835947dd65ed9",
}
# Absolute paths isolate this diagnostic from runtime default model roots.
original.MODELS.update({key: str(value) for key, value in MODEL_PATHS.items()})

def profile_metadata(model_id):
    greedy = model_id == original.FAST_MODEL_ID
    return {
        "model_id": model_id, "path": str(MODEL_PATHS[model_id]),
        "revision": REVISIONS[model_id], "dtype": "float16",
        "quantization": None, "attention": "sdpa", "manual_seed_each_generate": 0,
        "device_policy": "encoder_layer_cpu_offload" if model_id == original.PRECISE_MODEL_ID else "full_cuda",
        "audio_policy": "minimum_energy_20_to_29_second_chunks" if greedy else "native_longform",
        "generation_options": generation_options(model_id, "sk"),
    }

def generation_options(model_id, language):
    if model_id == TURBO_ID:
        return dict(language=language, task="transcribe", do_sample=False,
                    num_beams=1, use_cache=True, return_timestamps=True,
                    condition_on_prev_tokens=False,
                    temperature=(0.0, 0.2, 0.4, 0.6, 0.8, 1.0),
                    compression_ratio_threshold=1.35)
    return original.generation_options(model_id, language)

class BenchmarkEngine(original.WhisperEngine):
    def _load_model(self, model_id):
        if model_id != TURBO_ID:
            return super()._load_model(model_id)
        if self.loaded_model_id == model_id and self.model is not None:
            return 0.0
        start = time.perf_counter()
        if self.model is not None or self.loaded_model_id is not None:
            self.unload()
        from transformers import AutoModelForSpeechSeq2Seq, AutoProcessor
        self.torch.cuda.set_per_process_memory_fraction(0.90)
        folder = MODEL_PATHS[model_id]
        processor = AutoProcessor.from_pretrained(folder, local_files_only=True, trust_remote_code=False)
        model = AutoModelForSpeechSeq2Seq.from_pretrained(
            folder, local_files_only=True, use_safetensors=True, trust_remote_code=False,
            dtype=self.torch.float16, low_cpu_mem_usage=True, attn_implementation="sdpa",
            device_map={"": "cuda:0"}).eval()
        if any(p.dtype != self.torch.float16 or p.device.type != "cuda" for p in model.parameters()):
            raise RuntimeError("Expected Turbo original FP16 parameters entirely on CUDA.")
        self._normalize_eos(model)
        self.model, self.processor, self.loaded_model_id = model, processor, model_id
        self.torch.cuda.synchronize()
        return time.perf_counter() - start

    def transcribe(self, data, prompt, model_id, language, cancel_event=None):
        self._last_request_succeeded = False
        if model_id not in MODEL_IDS:
            raise ValueError("Unknown benchmark profile.")
        if model_id == TURBO_ID:
            language = language or "sk"
        else:
            model_id, language = original.resolve_request(model_id, language)
        audio = read_audio(BytesIO(data))
        if not len(audio):
            raise ValueError("Audio is empty.")
        load_seconds = self._load_model(model_id)
        greedy = model_id == original.FAST_MODEL_ID
        clips = original.split_audio(audio) if greedy else [audio]
        prepared = [self.processor(
            clip, sampling_rate=16000, return_tensors="pt",
            return_attention_mask=True, truncation=False, padding="max_length"
        ).to(device="cuda:0", dtype=self.torch.float16) for clip in clips]
        config = deepcopy(self.model.generation_config)
        config.language = None
        config.forced_decoder_ids = None
        options = generation_options(model_id, language)
        if prompt.strip():
            prompt_ids = self.processor.get_prompt_ids(prompt.strip(), return_tensors="pt")
            if prompt_ids.numel() > 180:
                raise ValueError("Vocabulary prompt exceeds 180 tokens.")
            options["prompt_ids"] = prompt_ids.to("cuda:0")
            if not greedy:
                options["prompt_condition_type"] = "all-segments"
        if cancel_event is not None:
            from transformers import StoppingCriteria, StoppingCriteriaList
            class Cancellation(StoppingCriteria):
                def __call__(self, input_ids, scores, **kwargs):
                    return cancel_event.is_set()
            options["stopping_criteria"] = StoppingCriteriaList([Cancellation()])
        self.torch.cuda.synchronize()
        start = time.perf_counter()
        decoded = []
        with self.torch.inference_mode():
            for inputs in prepared:
                if cancel_event is not None and cancel_event.is_set():
                    raise RuntimeError("Transcription cancelled.")
                self.torch.manual_seed(0)
                tokens = self.model.generate(**inputs, generation_config=config, **options)
                decoded.append(self.processor.batch_decode(tokens, skip_special_tokens=True)[0].strip())
        self.torch.cuda.synchronize()
        self._last_request_succeeded = True
        return {"text": " ".join(decoded).strip(), "language": language,
                "duration": len(audio) / 16000, "model": model_id,
                "inference_seconds": round(time.perf_counter() - start, 3),
                "load_seconds": round(load_seconds, 3), "decode_calls": len(prepared)}

def make_engine():
    return BenchmarkEngine()

