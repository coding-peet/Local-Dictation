"""Local, unquantized Whisper trial. All persistent data stays beside this file."""
import argparse
import json
import os
from pathlib import Path
import sys
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parent
os.environ['HF_HOME'] = str(ROOT / 'models' / '.hf')
os.environ['HF_HUB_DISABLE_IMPLICIT_TOKEN'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
os.environ['HF_HUB_DISABLE_XET'] = '1'
os.environ['DO_NOT_TRACK'] = '1'
MODELS = {
    'kinit-turbo': ('kinit/whisper-large-v3-turbo-sk', 'v2.0'),
    'whisper-large': ('openai/whisper-large-v3', 'main'),
}


def model_path(name):
    return Path(os.environ.get('LOCAL_DICTATION_MODEL_ROOT', str(ROOT / 'models'))) / name


def download(name):
    from huggingface_hub import HfApi, snapshot_download
    repo, tag = MODELS[name]
    info = HfApi(token=False).model_info(repo, revision=tag)
    print(f'Downloading {repo} at {info.sha}', flush=True)
    folder = model_path(name)
    snapshot_download(repo, revision=info.sha, local_dir=folder, token=False,
                      allow_patterns=['*.json', '*.safetensors', '*.txt', '*.model'],
                      ignore_patterns=['model.fp32*', '*.index.fp32.json'], max_workers=2)
    manifest = {'repo': repo, 'revision': info.sha, 'release': tag,
                'downloaded_utc': datetime.now(timezone.utc).isoformat()}
    (folder / 'local-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(f'Model ready: {folder}', flush=True)


def read_audio(path):
    import av
    import numpy as np
    chunks = []
    resampler = av.AudioResampler(format='fltp', layout='mono', rate=16000)
    with av.open(path if hasattr(path, 'read') else str(path)) as container:
        for frame in container.decode(audio=0):
            for converted in resampler.resample(frame):
                chunks.append(converted.to_ndarray()[0].copy())
        for converted in resampler.resample(None):
            chunks.append(converted.to_ndarray()[0].copy())
    if not chunks:
        raise ValueError('Audio file has no decodable audio samples.')
    audio = np.concatenate(chunks).astype(np.float32, copy=False)
    if not np.isfinite(audio).all():
        raise ValueError('Audio contains non-finite samples.')
    return audio


def choose_file():
    import tkinter as tk
    from tkinter import filedialog, simpledialog
    window = tk.Tk()
    window.withdraw()
    path = filedialog.askopenfilename(title='Vyber nahravku na lokalny prepis',
        initialdir=ROOT / 'samples',
        filetypes=[('Zvuk', '*.wav *.mp3 *.m4a *.flac *.ogg *.opus *.mp4 *.webm'), ('Vsetky subory', '*.*')])
    context = None
    if path:
        context = simpledialog.askstring('Kontext (volitelne)',
            'Technical terms, for example STM32, I2C, microfarad:', parent=window)
    window.destroy()
    return path, context or ''


def transcribe(args):
    if not args.audio:
        args.audio, entered = choose_file()
        if not args.audio:
            print('Cancelled.')
            return
        if not args.context:
            args.context = entered
    audio_path = Path(args.audio).resolve(strict=True)
    folder = model_path(args.model)
    manifest_path = folder / 'local-manifest.json'
    if not manifest_path.exists():
        raise FileNotFoundError(f'Model missing. Run: py asr.py download --model {args.model}')
    # Offline after explicit download. Neither audio nor prompt is sent anywhere.
    os.environ['HF_HUB_OFFLINE'] = '1'
    os.environ['TRANSFORMERS_OFFLINE'] = '1'
    import torch
    from transformers import AutoProcessor, AutoModelForSpeechSeq2Seq
    torch.set_num_threads(args.threads)
    torch.manual_seed(0)
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    audio = read_audio(audio_path)
    print(f'Audio: {len(audio) / 16000:.2f} s. Loading {args.model}, FP32, {args.mode}.', flush=True)
    start = time.perf_counter()
    processor = AutoProcessor.from_pretrained(folder, local_files_only=True)
    options = dict(local_files_only=True, use_safetensors=True, trust_remote_code=False,
                   dtype=torch.float32, low_cpu_mem_usage=True, attn_implementation='sdpa')
    if args.mode == 'hybrid':
        if not torch.cuda.is_available():
            raise RuntimeError('CUDA not available; choose --mode cpu.')
        # Keep encoder on GPU; Accelerate stages decoder weights from CPU to GPU.
        options['device_map'] = {'model.encoder': 0, 'model.decoder': 'cpu', 'proj_out': 'cpu'}
    model = AutoModelForSpeechSeq2Seq.from_pretrained(folder, **options).eval()
    if any(p.is_floating_point() and p.dtype != torch.float32 for p in model.parameters()):
        raise RuntimeError('Expected all floating model parameters to use FP32.')
    load_seconds = time.perf_counter() - start
    input_device = model.model.encoder.conv1.weight.device
    inputs = processor(audio, sampling_rate=16000, return_tensors='pt',
                       return_attention_mask=True, truncation=False, padding='max_length')
    inputs = inputs.to(input_device)
    generation = dict(language='slovak', task='transcribe', return_timestamps=True,
                      do_sample=False, num_beams=args.beams, condition_on_prev_tokens=True)
    if args.context:
        prompt = processor.get_prompt_ids(args.context, return_tensors='pt')
        if prompt.numel() > 180:
            raise ValueError('Context too long. Use a short list of names and technical terms (<=180 tokens).')
        generation.update(prompt_ids=prompt.to(input_device), prompt_condition_type='all-segments')
    print('Transcribing locally in FP32. CPU and weight offload can be slow; please wait.', flush=True)
    start = time.perf_counter()
    with torch.inference_mode():
        tokens = model.generate(**inputs, **generation)
    inference_seconds = time.perf_counter() - start
    transcript = processor.batch_decode(tokens, skip_special_tokens=True)[0].strip()
    stamp = datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    stem = ROOT / 'results' / f'{stamp}-{args.model}'
    stem.parent.mkdir(exist_ok=True)
    stem.with_suffix('.txt').write_text(transcript + '\n', encoding='utf-8')
    metadata = dict(model=json.loads(manifest_path.read_text(encoding='utf-8')),
        audio=str(audio_path), audio_seconds=len(audio)/16000, mode=args.mode,
        dtype='float32', quantization=False, beams=args.beams, threads=args.threads,
        cuda_peak_allocated_bytes=torch.cuda.max_memory_allocated() if args.mode == 'hybrid' else None,
        context=args.context, load_seconds=load_seconds, inference_seconds=inference_seconds,
        device_map=getattr(model, 'hf_device_map', {'all': 'cpu'}),
        torch=torch.__version__, transformers=__import__('transformers').__version__,
        transcript=transcript)
    stem.with_suffix('.json').write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding='utf-8')
    print('\n' + transcript + '\n', flush=True)
    print(f'Load: {load_seconds:.1f} s; inference: {inference_seconds:.1f} s\nSaved: {stem.with_suffix(".txt")}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    load = sub.add_parser('download', help='Explicit public model download (no audio upload)')
    load.add_argument('--model', choices=MODELS, default='kinit-turbo')
    run = sub.add_parser('transcribe', help='Offline audio transcription; opens file picker if audio is omitted')
    run.add_argument('audio', nargs='?')
    run.add_argument('--model', choices=MODELS, default='kinit-turbo')
    run.add_argument('--mode', choices=['cpu', 'hybrid'], default='cpu')
    run.add_argument('--context', default='')
    run.add_argument('--beams', type=int, default=5, choices=range(1, 11))
    run.add_argument('--threads', type=int, default=8, choices=range(1, 21))
    args = parser.parse_args()
    if args.command == 'download':
        download(args.model)
    else:
        transcribe(args)


if __name__ == '__main__':
    main()
