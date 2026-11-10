"""Offline CUDA engines for the two selected batch transcription profiles."""
from copy import deepcopy
import gc
from io import BytesIO
import logging
import os
import time

from asr import model_path, read_audio

FAST_MODEL_ID = "kinit/whisper-large-v3-turbo-sk"
PRECISE_MODEL_ID = "openai/whisper-large-v3"
MODELS = {
    FAST_MODEL_ID: "kinit-turbo",
    PRECISE_MODEL_ID: "whisper-large",
}
MODEL_LANGUAGES = {}
LOG = logging.getLogger("local-asr")


def resolve_request(model_id, language):
    aliases = {"kinit-turbo": FAST_MODEL_ID, "whisper-large": PRECISE_MODEL_ID}
    model_id = aliases.get(model_id, model_id)
    if model_id not in MODELS:
        raise ValueError("Unknown model; select one of the two models in /v1/models.")
    language = (language or "").strip().lower()
    language = {"slovak": "sk", "english": "en", "auto": None, "": None}.get(language, language)
    if language not in (None, "sk", "en"):
        raise ValueError("Supported explicit languages are sk and en; omit language for automatic detection.")
    if model_id == FAST_MODEL_ID:
        if language == "en":
            raise ValueError("The fast Slovak profile does not support English; select the precise SK/EN profile.")
        language = "sk"
    if MODEL_LANGUAGES.get(model_id)==["en"]:
        if language not in (None,"en"):raise ValueError("This model supports English only.")
        language="en"
    return model_id, language


def generation_options(model_id, language):
    fast = model_id == FAST_MODEL_ID
    options = dict(language=language, task="transcribe", do_sample=False,
                   num_beams=1 if fast else 5, use_cache=True,
                   return_timestamps=not fast)
    if not fast:
        options["condition_on_prev_tokens"] = True
    if MODEL_LANGUAGES.get(model_id)==["en"]:
        options.pop("language",None)
        options.pop("task",None)
    return options


def split_audio(audio):
    """Use the same 20-29 second minimum-energy cuts as the measured fast profile."""
    import numpy as np
    cuts = [0]
    position = 0
    while len(audio) - position > 29 * 16000:
        candidates = range(position + 20 * 16000, position + 29 * 16000, 320)
        position = min(candidates, key=lambda c: float(np.mean(audio[c-1600:c+1600] ** 2)))
        cuts.append(position)
    cuts.append(len(audio))
    return [audio[a:b] for a, b in zip(cuts, cuts[1:])]


class WhisperEngine:
    """Release GPU weights per request; keep the fast FP16 model in CPU RAM."""

    def __init__(self, threads=8):
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
        import torch
        self.torch = torch
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA is unavailable. These profiles require the NVIDIA GPU.")
        torch.set_num_threads(threads)
        torch.manual_seed(0)
        torch.backends.cuda.matmul.allow_tf32 = False
        torch.backends.cudnn.allow_tf32 = False
        self.model = None
        self.processor = None
        self.loaded_model_id = None
        self.runtime_ready = False
        self.runtime_warmup_seconds = None
        self._discard_fast_cache()

    def prepare_runtime(self):
        """Prepare CUDA and the fast CPU cache before readiness; no idle GPU weights."""
        if self.runtime_ready:
            return self.runtime_warmup_seconds
        start = time.perf_counter()
        LOG.info("Preparing transcription libraries, CUDA and fast CPU cache...")
        # Resolve lazy model/processor imports now, rather than on first dictation.
        from transformers import (AutoModelForSpeechSeq2Seq, AutoProcessor,
                                  WhisperForConditionalGeneration, WhisperProcessor)
        import av
        LOG.info("Transcription libraries ready; seconds=%.3f", time.perf_counter() - start)
        try:
            self.torch.cuda.init()
            self.torch.cuda.set_per_process_memory_fraction(0.90)
            self._prime_cuda()
        finally:
            # Release unused temporary allocations before reporting readiness.
            self.unload()
        self._prepare_fast_cpu_cache()
        self.runtime_warmup_seconds = round(time.perf_counter() - start, 3)
        self.runtime_ready = True
        LOG.info("CUDA runtime ready; fast weights cached on CPU, no GPU weights; preparation_seconds=%.3f", self.runtime_warmup_seconds)
        return self.runtime_warmup_seconds

    def _prime_cuda(self):
        """Initialize FP16 BLAS, convolution and attention with tiny tensors."""
        torch = self.torch
        with torch.inference_mode():
            matrix = torch.ones((128, 128), device="cuda:0", dtype=torch.float16)
            product = matrix @ matrix
            features = torch.ones((1, 128, 32), device="cuda:0", dtype=torch.float16)
            weights = torch.ones((64, 128, 3), device="cuda:0", dtype=torch.float16)
            convolution = torch.nn.functional.conv1d(features, weights, padding=1)
            query = torch.ones((1, 2, 32, 64), device="cuda:0", dtype=torch.float16)
            attention = torch.nn.functional.scaled_dot_product_attention(query, query, query)
            torch.cuda.synchronize()

    def status(self):
        initialized = self.torch.cuda.is_initialized()
        cached = bool(getattr(self, "_fast_cache_on_cpu", False))
        return {"ready": self.runtime_ready, "runtime_ready": self.runtime_ready,
                "runtime_warmup_seconds": self.runtime_warmup_seconds,
                "cuda_initialized": initialized, "model": self.loaded_model_id,
                "model_loaded": self.model is not None,
                "memory_policy": "release_gpu_keep_fast_cpu",
                "cpu_cached_models": [FAST_MODEL_ID] if cached else [],
                "cpu_cached_weight_mib": getattr(self, "_fast_cache_weight_mib", 0.0) if cached else 0.0,
                "device": "cuda:0", "dtype": "float16", "profiles": list(MODELS),
                "cuda_allocated_mib": self.torch.cuda.memory_allocated() / 2**20 if initialized else 0.0,
                "cuda_reserved_mib": self.torch.cuda.memory_reserved() / 2**20 if initialized else 0.0}

    def _discard_fast_cache(self):
        self._fast_model = None
        self._fast_processor = None
        self._fast_cache_on_cpu = False
        self._fast_cache_weight_mib = 0.0

    @staticmethod
    def _normalize_eos(model):
        eos = model.generation_config.eos_token_id
        if isinstance(eos, list) and len(eos) == 1:
            model.generation_config.eos_token_id = eos[0]

    def _prepare_fast_cpu_cache(self):
        """Load original weights as CPU FP16 once; never serialize converted weights."""
        if getattr(self, "_fast_model", None) is not None:
            return
        from transformers import AutoModelForSpeechSeq2Seq, AutoProcessor
        folder = model_path(MODELS[FAST_MODEL_ID])
        processor = AutoProcessor.from_pretrained(folder, local_files_only=True, trust_remote_code=False)
        model = AutoModelForSpeechSeq2Seq.from_pretrained(
            folder, local_files_only=True, use_safetensors=True, trust_remote_code=False,
            dtype=self.torch.float16, low_cpu_mem_usage=True, attn_implementation="sdpa",
            device_map={"": "cpu"}).eval()
        if any(p.dtype != self.torch.float16 or p.device.type != "cpu"
               for p in model.parameters()):
            raise RuntimeError("Expected fast cached parameters to use CPU FP16.")
        self._normalize_eos(model)
        self._fast_cache_weight_mib = sum(
            p.numel() * p.element_size() for p in model.parameters()) / 2**20
        self._fast_model, self._fast_processor = model, processor
        self._fast_cache_on_cpu = True
        LOG.info("Fast CPU FP16 cache ready; weight_mib=%.2f", self._fast_cache_weight_mib)

    def unload(self):
        """Drop active refs; park fast weights on CPU and fully release precise weights."""
        previous = self.loaded_model_id
        model = self.model
        park_fast = (previous == FAST_MODEL_ID and model is not None
                     and model is getattr(self, "_fast_model", None))
        successful = getattr(self, "_last_request_succeeded", False)
        self._last_request_succeeded = False
        self.loaded_model_id = None
        self.model = None
        self.processor = None
        parking_error = None
        try:
            if park_fast:
                self._fast_cache_on_cpu = False
                try:
                    model.to("cpu")
                    if any(p.dtype != self.torch.float16 or p.device.type != "cpu"
                           for p in model.parameters()):
                        raise RuntimeError("Fast model did not return to CPU FP16.")
                    self._fast_cache_on_cpu = True
                except Exception as exc:
                    # Keep only text. The original traceback owns tensors/model
                    # frames and must leave scope before the allocator flush.
                    parking_error = f"{type(exc).__name__}: {exc}"
                    self._discard_fast_cache()
        finally:
            model = None
            # A successful parked model remains alive on CPU. There are no
            # discarded model/hook cycles to collect in that hot path.
            if not park_fast or not successful or parking_error is not None:
                gc.collect()
            if self.torch.cuda.is_initialized():
                self.torch.cuda.synchronize()
                self.torch.cuda.empty_cache()
        if parking_error is not None:
            raise RuntimeError("GPU model cleanup failed: " + parking_error) from None
        if previous is not None:
            LOG.info("GPU model released: %s", previous)

    def _load_model(self, model_id):
        if model_id == self.loaded_model_id and self.model is not None:
            return 0.0
        start = time.perf_counter()
        if self.model is not None or self.loaded_model_id is not None:
            self.unload()
        self.torch.cuda.set_per_process_memory_fraction(0.90)
        if model_id == FAST_MODEL_ID:
            self._prepare_fast_cpu_cache()
            self.model, self.processor = self._fast_model, self._fast_processor
            self.loaded_model_id = model_id
            self._fast_cache_on_cpu = False
            self.model.to("cuda:0")
            if any(p.dtype != self.torch.float16 or p.device.type != "cuda"
                   for p in self.model.parameters()):
                raise RuntimeError("Expected active fast parameters to use CUDA FP16.")
        else:
            from transformers import AutoModelForSpeechSeq2Seq, AutoProcessor
            from accelerate import cpu_offload
            folder = model_path(MODELS[model_id])
            processor = AutoProcessor.from_pretrained(folder, local_files_only=True, trust_remote_code=False)
            model = AutoModelForSpeechSeq2Seq.from_pretrained(
                folder, local_files_only=True, use_safetensors=True, trust_remote_code=False,
                dtype=self.torch.float16, low_cpu_mem_usage=True, attn_implementation="sdpa",
                device_map={"": "cuda:0"}).eval()
            if any(p.dtype != self.torch.float16 or p.device.type != "cuda"
                   for p in model.parameters()):
                raise RuntimeError("Expected precise parameters to load as CUDA FP16.")
            self._normalize_eos(model)
            # Keep the encoder stem on CUDA so Whisper's logical device stays CUDA.
            # The decoder and its KV cache stay on GPU; encoder layers execute there
            # one at a time with CPU-backed weights.
            cpu_offload(model.model.encoder.layers, execution_device=self.torch.device("cuda:0"),
                        offload_buffers=True, preload_module_classes=["WhisperEncoderLayer"])
            if model.device.type != "cuda":
                raise RuntimeError("The precise model logical device must remain CUDA.")
            self.processor, self.model = processor, model
            self.loaded_model_id = model_id
        self.torch.cuda.synchronize()
        elapsed = time.perf_counter() - start
        LOG.info("Model activated: %s; load_seconds=%.3f", model_id, elapsed)
        return elapsed

    def transcribe(self, data, prompt, model_id, language, cancel_event=None):
        self._last_request_succeeded = False
        model_id, language = resolve_request(model_id, language)
        try:
            audio = read_audio(BytesIO(data))
        except Exception as exc:
            raise ValueError("Cannot decode the uploaded audio.") from exc
        if not len(audio):
            raise ValueError("Audio is empty.")
        load_seconds = self._load_model(model_id)
        fast = model_id == FAST_MODEL_ID
        clips = split_audio(audio) if fast else [audio]
        prepared = [self.processor(clip, sampling_rate=16000, return_tensors="pt",
                    return_attention_mask=True, truncation=False, padding="max_length").to(
                    device="cuda:0", dtype=self.torch.float16) for clip in clips]
        config = deepcopy(self.model.generation_config)
        # Never inherit a language or forced prefix from another request.
        config.language = None
        config.forced_decoder_ids = None
        prompt_ids = None
        if prompt.strip():
            prompt_ids = self.processor.get_prompt_ids(prompt.strip(), return_tensors="pt")
            if prompt_ids.numel() > 180:
                raise ValueError("Vocabulary prompt is too long; use at most 180 tokens.")
            prompt_ids = prompt_ids.to("cuda:0")
        self.torch.cuda.synchronize()
        start = time.perf_counter()
        with self.torch.inference_mode():
            if language is None:
                language_id = int(self.model.detect_language(
                    input_features=prepared[0].input_features,
                    generation_config=config)[0].item())
                by_id = {value: token[2:-2] for token, value in config.lang_to_id.items()}
                language = by_id[language_id]
            options = generation_options(model_id, language)
            if prompt_ids is not None:
                options["prompt_ids"] = prompt_ids
                if not fast:
                    options["prompt_condition_type"] = "all-segments"
            decoded = []
            if cancel_event is not None:
                from transformers import StoppingCriteria, StoppingCriteriaList
                class Cancellation(StoppingCriteria):
                    def __call__(self, input_ids, scores, **kwargs):
                        return cancel_event.is_set()
                options["stopping_criteria"] = StoppingCriteriaList([Cancellation()])
            for inputs in prepared:
                if cancel_event is not None and cancel_event.is_set():
                    raise RuntimeError("Transcription cancelled.")
                tokens = self.model.generate(**inputs, generation_config=config, **options)
                decoded.append(self.processor.batch_decode(tokens, skip_special_tokens=True)[0].strip())
        if cancel_event is not None and cancel_event.is_set():
            raise RuntimeError("Transcription cancelled.")
        self.torch.cuda.synchronize()
        elapsed = time.perf_counter() - start
        self._last_request_succeeded = True
        return {"text": " ".join(decoded).strip(), "language": language,
                "duration": len(audio) / 16000, "model": model_id,
                "inference_seconds": round(elapsed, 3), "load_seconds": round(load_seconds, 3)}
