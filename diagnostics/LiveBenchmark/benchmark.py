"""Original-weight ASR experiment runner. Importing this file never imports torch."""
from __future__ import annotations
import argparse
import array
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
from io import BytesIO
import json
import math
import os
from pathlib import Path
import re
import sys
import time
import wave

RATE = 16000
FRAME = 320
SCENARIOS = ('baseline', *(f'alt{i:02}' for i in range(1, 11)))
MODELS = {
    'kinit': 'kinit/whisper-large-v3-turbo-sk',
    'large': 'openai/whisper-large-v3',
    'turbo': 'openai/whisper-large-v3-turbo',
}


def edit_distance(a, b):
    row = list(range(len(b)+1))
    for i, left in enumerate(a, 1):
        nxt = [i]
        for j, right in enumerate(b, 1):
            nxt.append(min(nxt[-1]+1, row[j]+1, row[j-1]+(left != right)))
        row = nxt
    return row[-1]


def repeated_boundary(left, right):
    """Diagnostic ONLY: return exact equal suffix/prefix count; never remove words."""
    a, b = left.split(), right.split()
    return max((i for i in range(1, min(len(a), len(b), 32)+1)
                if a[-i:] == b[:i]), default=0)


def longest_word_run(text):
    maximum = run = 0
    previous = None
    for word in re.findall(r'\w+', text.casefold()):
        run = run+1 if word == previous else 1
        previous = word
        maximum = max(maximum, run)
    return maximum


def join_text(parts):
    return ' '.join(p.strip() for p in parts if p.strip())


def replay(scenario, pcm, infer, checkpoint=None):
    """Discrete-event 20ms replay with measured request time as server backpressure.

    infer(array[int16]) returns text, inference_seconds, load_seconds and
    request_wall_seconds. Only synthetic tests may substitute a fake infer.
    """
    if scenario not in SCENARIOS:
        raise ValueError('Unknown scenario')
    n = len(pcm)
    if not n:
        raise ValueError('Empty audio')
    duration = n/RATE
    # Voice activity is precisely current server frame RMS > .008. Audio is
    # neither cropped nor normalized. Last partial frame has its actual length.
    frame_ends = list(range(FRAME, n, FRAME)) + [n]
    voice = []
    energies = []
    for end in frame_ends:
        values = pcm[max(0, end-FRAME):end]
        energy = sum(v*v for v in values)/len(values)
        energies.append(energy)
        voice.append(math.sqrt(energy)/32768 > .008)
    events, committed = [], []
    now = 0.0
    available = start = frame_index = last_voice = 0
    capture_limit = n
    stop_reason = 'end_of_recording'
    draft = previous_snapshot = ''
    last_decode = -1e9  # real server monotonic clock is already > .75 at startup
    decoded_until = -1
    next_preview = 3.0
    next_overlap = {'alt08': 8.0, 'alt09': 12.0}.get(scenario, 0)
    revised_words = 0
    boundary_repeats = []
    skipped_silence = []

    def ingest():
        nonlocal available, frame_index, last_voice, capture_limit, stop_reason
        if available >= capture_limit:
            return
        while frame_index < len(frame_ends) and frame_ends[frame_index]/RATE <= now+1e-8:
            end = min(frame_ends[frame_index], capture_limit)
            if scenario == 'baseline' and end-start >= 32*RATE:
                end = start+32*RATE
                capture_limit = end
                stop_reason = 'baseline_32s_buffer_limit'
            available = end
            if voice[frame_index]:
                last_voice = end
            frame_index += 1
            if available >= capture_limit:
                break

    def invoke(a, z, kind, available_at_request):
        nonlocal now, draft, previous_snapshot, revised_words, last_decode, decoded_until
        request_time = now
        result = infer(pcm[a:z])
        elapsed = float(result['request_wall_seconds'])
        if elapsed < 0 or not math.isfinite(elapsed):
            raise ValueError('Invalid measured request wall time')
        now += elapsed
        # Frames arrive while inference owns its original buffer. In particular,
        # baseline's 32 s limit applies BEFORE the completed chunk is removed.
        ingest()
        text = result['text'].strip()
        if kind == 'commit':
            boundary_repeats.append(repeated_boundary(join_text(committed), text))
            committed.append(text)
            draft = ''
        elif kind == 'refine':
            committed[:] = [text]
            draft = ''
        else:
            draft = text
        snapshot = join_text([*committed, draft])
        # Word replacements/deletions in the existing draft, excluding appended
        # suffixes. This is UI churn, not an accuracy score.
        old, new = previous_snapshot.split(), snapshot.split()
        prefix = 0
        while prefix < min(len(old), len(new)) and old[prefix] == new[prefix]:
            prefix += 1
        revised_words += len(old)-prefix
        previous_snapshot = snapshot
        event = dict(index=len(events), kind=kind, start_sample=a, end_sample=z,
                     audio_available_seconds=available_at_request/RATE,
                     virtual_started_seconds=request_time, virtual_finished_seconds=now,
                     text=text, snapshot=snapshot, committed_text=join_text(committed),
                     draft_text=draft, inference_seconds=float(result.get('inference_seconds', 0)),
                     load_seconds=float(result.get('load_seconds', 0)),
                     request_wall_seconds=elapsed)
        events.append(event)
        if checkpoint:
            checkpoint(event)
        last_decode = now
        decoded_until = z
        return text

    while start < capture_limit:
        ingest()
        finishing = available >= capture_limit
        size = available-start
        if not size:
            if finishing:
                break
            now = max(now, frame_ends[frame_index]/RATE)
            continue
        cut = None
        kind = 'commit'
        if scenario == 'baseline':
            silence = (available-last_voice)/RATE
            should_cut = size >= 27*RATE or silence >= .7
            if not finishing and decoded_until == available:
                pass
            elif finishing or should_cut or (size >= .5*RATE and now-last_decode >= .75-1e-8):
                if not last_voice or (silence >= size/RATE and not draft):
                    skipped_silence.append([start, available])
                    start = available
                    continue
                cut = available
                if size >= 27*RATE:
                    cut = min(range(start+20*RATE, start+26*RATE, 320),
                              key=lambda p: sum(v*v for v in pcm[p-800:p+800]))
                kind = 'commit' if finishing or should_cut else 'preview'
        elif scenario in ('alt01', 'alt02', 'alt03', 'alt04'):
            seconds = {'alt01':3, 'alt02':6, 'alt03':10, 'alt04':15}[scenario]
            if size >= seconds*RATE or finishing:
                cut = min(start+seconds*RATE, available)
        elif scenario in ('alt05', 'alt06', 'alt07'):
            pause, minimum, maximum = {'alt05':(.35,1.5,10),
                'alt06':(.7,3,20), 'alt07':(1.1,4,27)}[scenario]
            if finishing or size >= maximum*RATE or (size >= minimum*RATE and
                                                       (available-last_voice)/RATE >= pause-1e-8):
                cut = min(start+int(maximum*RATE), available)
        elif scenario in ('alt08', 'alt09'):
            window, hop = {'alt08':(8,6), 'alt09':(12,8)}[scenario]
            # The same samples may occur in several drafts. Final commitments
            # use independently decoded disjoint hops: no text deduplication.
            if available/RATE >= next_overlap-1e-8:
                latest_tick = window+math.floor((available/RATE-window+1e-8)/hop)*hop
                z = min(n, round(latest_tick*RATE))
                a = max(0, z-window*RATE)
                invoke(a, z, 'preview', available)
                next_overlap = latest_tick+hop
                ingest()
                cut = min(start+hop*RATE, available)
            elif finishing:
                cut = min(start+hop*RATE, available)
        elif scenario == 'alt10':
            if size >= 24*RATE:
                cut = min(range(start+12*RATE, start+24*RATE, FRAME),
                          key=lambda p: sum(v*v for v in pcm[p-800:p+800]))
            elif finishing:
                cut = available
            elif size/RATE >= next_preview-1e-8:
                cut, kind = available, 'preview'
        if cut is not None:
            invoke(start, cut, kind, available)
            if kind == 'commit':
                start = cut
                next_preview = 3.
            elif scenario == 'alt10':
                next_preview = (math.floor((available-start)/RATE/3)+1)*3
            continue
        # Wake on the next actual frame, never on queued stale preview work.
        if frame_index >= len(frame_ends) or available >= capture_limit:
            raise RuntimeError('Scheduler made no progress')
        now = max(now, frame_ends[frame_index]/RATE)

    if scenario == 'alt10':
        now = max(now, duration)
        invoke(0, n, 'refine', n)
    final = join_text([*committed, draft])
    previews = [e for e in events if e['text']]
    commits = [e for e in events if e['kind'] in ('commit','refine') and e['text']]
    return dict(scenario=scenario, text=final, events=events, duration_seconds=duration,
                timing_kind='accelerated_discrete_event_audio_replay_actual_inference',
                realtime_microphone_verified=False, capture_stop_reason=stop_reason,
                accepted_samples=capture_limit, unprocessed_samples=n-capture_limit,
                audio_coverage_fraction=capture_limit/n, skipped_silence_spans=skipped_silence,
                first_preview_seconds=previews[0]['virtual_finished_seconds'] if previews else None,
                first_commit_seconds=commits[0]['virtual_finished_seconds'] if commits else None,
                final_virtual_seconds=max(now, capture_limit/RATE),
                final_after_stop_seconds=max(0, now-duration), revised_word_count=revised_words,
                boundary_repeat_word_counts=boundary_repeats, max_word_run=longest_word_run(final),
                cumulative_inference_seconds=sum(e['inference_seconds'] for e in events),
                cumulative_request_wall_seconds=sum(e['request_wall_seconds'] for e in events),
                cumulative_decode_audio_seconds=sum((e['end_sample']-e['start_sample'])/RATE for e in events),
                text_reconciliation='none; independently decoded disjoint commitment spans',
                accuracy_status='unscored_without_confirmed_independent_human_reference')


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024*1024), b''):
            digest.update(block)
    return digest.hexdigest()


def read_pcm(path):
    with wave.open(str(path), 'rb') as wav:
        if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) != (1,2,RATE):
            raise ValueError('Benchmark requires complete mono16kHz PCM16 WAV from corpus manifest')
        pcm = array.array('h', wav.readframes(wav.getnframes()))
    if sys.byteorder != 'little':
        pcm.byteswap()
    return pcm


def pcm_wav(pcm):
    stream = BytesIO()
    values = array.array('h', pcm)
    if sys.byteorder != 'little':
        values.byteswap()
    with wave.open(stream, 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(values.tobytes())
    return stream.getvalue()


@contextmanager
def gpu_mutex(name=r'Local\LocalDictation.LiveBenchmark.GPU', timeout_seconds=7200):
    """One shared Windows kernel mutex, acquired BEFORE torch/engine import.

    An abandoned mutex is acquired by Windows and reported; it is not reset.
    This serializes benchmark jobs only. Production is never contacted/stopped.
    """
    if sys.platform != 'win32':
        raise RuntimeError('This experiment requires the Windows named GPU mutex')
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    kernel.CreateMutexW.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.ReleaseMutex.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateMutexW(None, False, name)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    acquired = False
    try:
        waited = time.perf_counter()
        outcome = kernel.WaitForSingleObject(handle, int(timeout_seconds*1000))
        if outcome not in (0, 0x80):
            raise TimeoutError(f'GPU mutex wait failed: {outcome}')
        acquired = True
        yield dict(name=name, abandoned_previous_owner=outcome == 0x80,
                   wait_seconds=time.perf_counter()-waited)
    finally:
        if acquired:
            kernel.ReleaseMutex(handle)
        kernel.CloseHandle(handle)


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix+'.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    os.replace(temporary, path)


def timeline_text(result):
    lines = [f"Scenario: {result['scenario']}", f"Model: {result['model_id']}",
             f"Recording: {result['recording_id']}",
             'Accelerated discrete-event replay, not real-time microphone validation.',
             'Each request below ran actual original-weight inference.', '']
    for event in result['events']:
        lines.extend([f"{event['index']:03} {event['kind']} "
                      f"audio {event['start_sample']/RATE:.3f}..{event['end_sample']/RATE:.3f}s "
                      f"available {event['audio_available_seconds']:.3f}s "
                      f"virtual {event['virtual_started_seconds']:.3f}..{event['virtual_finished_seconds']:.3f}s "
                      f"compute {event['inference_seconds']:.3f}s wall {event['request_wall_seconds']:.3f}s",
                      event['text'], 'Snapshot: '+event['snapshot'], ''])
    lines.extend(['FINAL', result['text'], ''])
    return '\n'.join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--scenario', action='append', choices=SCENARIOS)
    parser.add_argument('--model', action='append', choices=list(MODELS))
    parser.add_argument('--recording', action='append', help='Exact manifest clip_id; repeatable')
    parser.add_argument('--mutex', default=r'Local\LocalDictation.LiveBenchmark.GPU')
    parser.add_argument('--resume', action='store_true', help='Skip complete matching jobs only')
    parser.add_argument('--plan-only', action='store_true', help='Validate identity and print job IDs; no torch')
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    if output.is_relative_to(root):
        parser.error('Private experiment outputs must be outside the Git checkout')
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    clips = [clip for clip in manifest['clips']
             if not args.recording or clip['clip_id'] in args.recording]
    if args.recording and set(args.recording) != {c['clip_id'] for c in clips}:
        parser.error('Unknown recording ID')
    scenarios = args.scenario or list(SCENARIOS)
    models = args.model or list(MODELS)
    jobs = [(model, scenario, clip) for model in models for scenario in scenarios for clip in clips]
    if not jobs:
        parser.error('No jobs selected')
    # Hash all private inputs before acquiring/loading models. Never print text.
    for clip in clips:
        path = Path(clip['audio']['file'])
        if not path.is_absolute():
            path = args.manifest.parent/path
        if sha256(path) != clip['audio']['sha256']:
            raise ValueError('Corpus audio hash mismatch: '+clip['clip_id'])
        clip['_resolved_audio_path'] = str(path)
    if args.plan_only:
        print(json.dumps({'jobs':[f'{s}/{m}/{c["clip_id"]}' for m,s,c in jobs],
                          'count':len(jobs), 'gpu_imported':False}, indent=2))
        return 0
    output.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(root/'server'))
    # profiles imports model_engine/torch only inside make_engine, after mutex.
    from profiles import make_engine, profile_metadata
    fingerprint_files = [Path(__file__), Path(__file__).with_name('profiles.py'),
                         root/'server/model_engine.py', root/'server/local_dictation/sessions.py']
    source_fingerprint = {str(p.relative_to(root)): sha256(p) for p in fingerprint_files}
    corpus_hash = sha256(args.manifest)
    with gpu_mutex(args.mutex) as lock:
        engine_started = time.perf_counter()
        engine = make_engine()
        runtime_init_seconds = time.perf_counter()-engine_started
        try:
            for model in models:
                model_id = MODELS[model]
                profile = json.loads(json.dumps(profile_metadata(model_id)))
                model_jobs = [(s,c) for m,s,c in jobs if m == model]
                engine.torch.cuda.synchronize()
                load_started = time.perf_counter()
                engine._load_model(model_id)
                engine.torch.cuda.synchronize()
                activation_seconds = time.perf_counter()-load_started
                for scenario, clip in model_jobs:
                    job_key = f'{scenario}/{model}/{clip["clip_id"]}'
                    folder = output/scenario/model
                    folder.mkdir(parents=True, exist_ok=True)
                    result_path = folder/(clip['clip_id']+'.json')
                    identity = dict(source_fingerprint=source_fingerprint,
                                    corpus_manifest_sha256=corpus_hash,
                                    audio_sha256=clip['audio']['sha256'], profile=profile)
                    if args.resume and result_path.exists():
                        old = json.loads(result_path.read_text(encoding='utf-8'))
                        if old.get('status') == 'complete' and old.get('identity') == identity:
                            print('RESUME complete '+job_key, flush=True)
                            continue
                        raise ValueError('Existing result has different identity: '+job_key)
                    pcm = read_pcm(clip['_resolved_audio_path'])
                    checkpoint_path = folder/(clip['clip_id']+'.events.jsonl')
                    # Partial jobs restart from original audio; old partial trace
                    # is preserved rather than presented as measured resume.
                    if checkpoint_path.exists():
                        checkpoint_path.rename(checkpoint_path.with_name(
                            checkpoint_path.name+'.partial-'+str(time.time_ns())))
                    checkpoint_stream = checkpoint_path.open('w', encoding='utf-8', buffering=1)
                    def checkpoint(event):
                        checkpoint_stream.write(json.dumps(event, ensure_ascii=False)+'\n')
                        checkpoint_stream.flush()
                    def infer(values):
                        data = pcm_wav(values)
                        engine.torch.cuda.synchronize()
                        started = time.perf_counter()
                        response = engine.transcribe(data, '', model_id, clip.get('language','sk'))
                        engine.torch.cuda.synchronize()
                        response['request_wall_seconds'] = time.perf_counter()-started
                        response['actual_inference'] = True
                        return response
                    print('START '+job_key, flush=True)
                    engine.torch.cuda.reset_peak_memory_stats()
                    try:
                        result = replay(scenario, pcm, infer, checkpoint)
                    finally:
                        checkpoint_stream.close()
                    result.update(status='complete', schema_version=1, identity=identity,
                                  model=model, model_id=model_id, recording_id=clip['clip_id'],
                                  created_utc=datetime.now(timezone.utc).isoformat(),
                                  generation_profile=profile, gpu_mutex=lock,
                                  runtime_init_seconds=runtime_init_seconds,
                                  shared_model_activation_seconds=activation_seconds,
                                  model_activation_policy='once per model-group; excluded from warm replay',
                                  cold_first_preview_estimate_seconds=(None if result['first_preview_seconds'] is None else
                                      result['first_preview_seconds']+activation_seconds+runtime_init_seconds),
                                  cuda_peak_allocated_mib=engine.torch.cuda.max_memory_allocated()/2**20,
                                  torch_version=engine.torch.__version__,
                                  transformers_version=__import__('transformers').__version__,
                                  reference=clip.get('reference',{}))
                    reference = clip.get('reference',{})
                    if reference.get('score_enabled') and reference.get('confirmed_human'):
                        reference_path = Path(reference['path'])
                        if not reference_path.is_absolute():
                            reference_path = args.manifest.parent/reference_path
                        words = reference_path.read_text(encoding='utf-8-sig').split()
                        result['wer'] = edit_distance(words,result['text'].split())/max(1,len(words))
                        result['accuracy_status'] = 'confirmed_human_reference_case_sensitive_whitespace_tokenized_WER'
                    atomic_json(result_path, result)
                    result_path.with_suffix('.timeline.txt').write_text(timeline_text(result), encoding='utf-8')
                    result_path.with_suffix('.txt').write_text(result['text']+'\n', encoding='utf-8')
                    print('DONE '+job_key+f' requests={len(result["events"])} '
                          f'compute={result["cumulative_inference_seconds"]:.3f}s '
                          f'coverage={result["audio_coverage_fraction"]:.6f}', flush=True)
                engine.unload()
        finally:
            engine.unload()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
