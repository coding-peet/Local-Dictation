"""Supplementary serialized two-lane replay. Importing this file never loads torch."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import sys
import time

from benchmark import (FRAME, RATE, MODELS, atomic_json, edit_distance, gpu_mutex,
                       join_text, longest_word_run, pcm_wav, read_pcm, sha256, timeline_text)

SCENARIOS = {'dual02_06': (2, 6), 'dual03_05': (3, 5), 'dual03_12': (3, 12)}


def replay(scenario, pcm, infer, checkpoint=None):
    """Disjoint authoritative epochs, with disjoint short drafts within each.

    One request at a time. Actual synchronized request wall time advances virtual
    capture. Arrival is quantized to complete 20 ms frames, plus the actual final
    partial frame. We retain the complete input. Long repair always wins when due.
    Obsolete short ticks coalesce to the latest fixed short interval; gaps are
    explicit and are eventually covered by the authoritative epoch repair.
    """
    if scenario not in SCENARIOS or not len(pcm):
        raise ValueError('Unknown scenario or empty audio')
    fast_seconds, long_seconds = SCENARIOS[scenario]
    fast, epoch = fast_seconds * RATE, long_seconds * RATE
    n, now, start, last_tick = len(pcm), 0., 0, 0
    duration = n / RATE
    committed, drafts, events, repairs, skipped = [], [], [], [], []
    previous_snapshot, revised_words = '', 0

    def available():
        # This is equivalent to ingesting each 320-sample arrival in order.
        if now >= duration - 1e-10:
            return n
        return min(n, math.floor((now * RATE + 1e-6) / FRAME) * FRAME)

    while start < n:
        end = min(start + epoch, n)
        received = available()
        max_tick = (end - start - 1) // fast
        if received >= end:
            kind, a, z = 'repair', start, end
            skipped.extend([[start + (tick - 1) * fast, start + tick * fast]
                            for tick in range(last_tick + 1, max_tick + 1)])
        else:
            tick = min(max_tick, (received - start) // fast)
            if tick <= last_tick:
                wake = min(end, start + (last_tick + 1) * fast)
                now = max(now, wake / RATE)
                continue
            skipped.extend([[start + (missed - 1) * fast, start + missed * fast]
                            for missed in range(last_tick + 1, tick)])
            kind, a, z = 'draft', start + (tick - 1) * fast, start + tick * fast
            last_tick = tick
        if not (start <= a < z <= received):
            raise RuntimeError('Request contains unavailable or repaired samples')
        begun, repaired_before = now, start
        response = infer(pcm[a:z])
        elapsed = float(response['request_wall_seconds'])
        inference = float(response.get('inference_seconds', 0))
        load = float(response.get('load_seconds', 0))
        if any(not math.isfinite(v) or v < 0 for v in (elapsed, inference, load)):
            raise ValueError('Invalid measured inference/request/load time')
        now += elapsed
        text = response['text'].strip()
        replaced_count = len(drafts) if kind == 'repair' else 0
        replaced_text = join_text([item['text'] for item in drafts]) if kind == 'repair' else ''
        if kind == 'repair':
            committed.append(text)
            repairs.append([a, z])
            drafts.clear()
            start, last_tick = z, 0
        else:
            drafts.append(dict(start_sample=a, end_sample=z, text=text))
        snapshot = join_text([*committed, *(item['text'] for item in drafts)])
        old, new, prefix = previous_snapshot.split(), snapshot.split(), 0
        while prefix < min(len(old), len(new)) and old[prefix] == new[prefix]:
            prefix += 1
        revised = len(old) - prefix
        revised_words += revised
        previous_snapshot = snapshot
        event = dict(index=len(events), kind=kind, start_sample=a, end_sample=z,
                     available_samples_at_request=received,
                     audio_available_seconds=received / RATE,
                     available_samples_at_finish=available(),
                     repaired_until_sample_at_request=repaired_before,
                     virtual_started_seconds=begun, virtual_finished_seconds=now,
                     request_wall_seconds=elapsed, inference_seconds=inference, load_seconds=load,
                     text=text, snapshot=snapshot, committed_text=join_text(committed),
                     draft_text=join_text([item['text'] for item in drafts]),
                     visible_draft_spans=[[item['start_sample'], item['end_sample']] for item in drafts],
                     visible_repaired_spans=list(repairs),
                     revised_word_count=revised, replaced_draft_span_count=replaced_count,
                     replaced_draft_text=replaced_text,
                     repair_after_audio_end_seconds=now - z / RATE if kind == 'repair' else None)
        events.append(event)
        if checkpoint:
            checkpoint(event)

    if repairs[0][0] != 0 or repairs[-1][1] != n or any(
            left[1] != right[0] for left, right in zip(repairs, repairs[1:])):
        raise RuntimeError('Authoritative repair spans must partition the full recording')
    final = join_text(committed)
    visible = [event for event in events if event['snapshot']]
    draft_events = [event for event in events if event['kind'] == 'draft' and event['text']]
    repair_events = [event for event in events if event['kind'] == 'repair']
    first_text = visible[0]['virtual_finished_seconds'] if visible else None
    return dict(scenario=scenario, text=final, final_repaired_text=final, events=events,
                duration_seconds=duration, fast_seconds=fast_seconds, long_seconds=long_seconds,
                timing_kind='accelerated_discrete_event_audio_replay_actual_inference',
                realtime_microphone_verified=False, capture_stop_reason='end_of_recording',
                accepted_samples=n, unprocessed_samples=0, audio_coverage_fraction=1.,
                repaired_samples=sum(z - a for a, z in repairs), repair_spans=repairs,
                skipped_draft_spans=skipped, skipped_draft_samples=sum(z - a for a, z in skipped),
                first_text_seconds=first_text, first_preview_seconds=first_text,
                first_short_draft_seconds=draft_events[0]['virtual_finished_seconds'] if draft_events else None,
                first_commit_seconds=repair_events[0]['virtual_finished_seconds'],
                first_repair_seconds=repair_events[0]['virtual_finished_seconds'],
                repair_after_audio_end_seconds=[event['repair_after_audio_end_seconds'] for event in repair_events],
                final_virtual_seconds=now, final_after_stop_seconds=max(0., now - duration),
                revised_word_count=revised_words, max_word_run=longest_word_run(final),
                cumulative_inference_seconds=sum(event['inference_seconds'] for event in events),
                cumulative_request_wall_seconds=sum(event['request_wall_seconds'] for event in events),
                cumulative_decode_audio_seconds=sum((event['end_sample'] - event['start_sample']) / RATE for event in events),
                global_full_recording_refinement=False,
                text_reconciliation='authoritative disjoint long epochs replace their short hypotheses; no lexical deduplication',
                accuracy_status='unscored_without_confirmed_independent_human_reference')


def validate_resume(path, identity):
    """Reject stale identity or damaged/missing private artifacts, never relabel."""
    if not path.exists():
        return False
    result = json.loads(path.read_text(encoding='utf-8'))
    if result.get('status') != 'complete' or result.get('identity') != identity:
        raise ValueError('Existing result has different identity or is incomplete: ' + str(path))
    if not result.get('artifacts'):
        raise ValueError('Existing result has no artifact identities: ' + str(path))
    for filename, digest in result['artifacts'].items():
        artifact = path.parent / filename
        if artifact.parent != path.parent or not artifact.is_file() or sha256(artifact) != digest:
            raise ValueError('Missing or changed result artifact: ' + str(artifact))
    return True


def source_identity(root):
    paths = [Path(__file__), Path(__file__).with_name('benchmark.py'),
             Path(__file__).with_name('profiles.py'), root / 'server/model_engine.py',
             root / 'server/asr.py', root / 'server/local_dictation/sessions.py']
    return {str(path.relative_to(root)): sha256(path) for path in paths}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--scenario', action='append', choices=SCENARIOS)
    parser.add_argument('--model', action='append', choices=MODELS)
    parser.add_argument('--recording', action='append')
    parser.add_argument('--resume', action='store_true')
    parser.add_argument('--plan-only', action='store_true', help='Check WAV/source identity; no output writes or torch')
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[2]
    output, manifest_path = args.output.resolve(), args.manifest.resolve()
    # Do not permit this supplementary runner to alter the frozen experiment.
    frozen = Path(r'C:\AI\LocalDictation\benchmarks\20261111-live').resolve()
    if output.is_relative_to(root) or output == frozen or output.is_relative_to(frozen):
        parser.error('Use a new private output root outside Git and the frozen experiment')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    clips = [clip for clip in manifest['clips'] if not args.recording or clip['clip_id'] in args.recording]
    ids = [clip['clip_id'] for clip in clips]
    if len(set(ids)) != len(ids) or any(Path(cid).name != cid or cid in ('.', '..') or not cid for cid in ids):
        parser.error('Recording IDs must be unique safe filenames')
    if args.recording and set(args.recording) != set(ids):
        parser.error('Unknown recording ID')
    models, scenarios = args.model or list(MODELS), args.scenario or list(SCENARIOS)
    jobs = [(model, scenario, clip) for model in models for scenario in scenarios for clip in clips]
    if not jobs:
        parser.error('No jobs selected')
    for clip in clips:
        path = Path(clip['audio']['file'])
        path = path if path.is_absolute() else manifest_path.parent / path
        if sha256(path) != clip['audio']['sha256']:
            raise ValueError('Corpus audio hash mismatch: ' + clip['clip_id'])
        pcm = read_pcm(path)
        if not pcm:
            raise ValueError('Empty corpus audio: ' + clip['clip_id'])
        clip['_path'], clip['_samples'] = path, len(pcm)
    fingerprint = source_identity(root)
    corpus_hash = sha256(manifest_path)
    if args.plan_only:
        print(json.dumps(dict(jobs=[f'{s}/{m}/{c["clip_id"]}' for m, s, c in jobs],
                              count=len(jobs), gpu_imported=False, source_fingerprint=fingerprint), indent=2))
        return 0

    with gpu_mutex() as lock:
        # profiles imports model engine helpers; all GPU-related imports follow
        # the exact same named mutex used by the frozen benchmark and controls.
        from profiles import make_engine, profile_metadata, generation_options
        output.mkdir(parents=True, exist_ok=True)
        source_path = output / 'source_manifest.json'
        source = dict(method='supplementary_dual_lane_v1', source_fingerprint=fingerprint,
                      corpus_manifest_sha256=corpus_hash)
        if source_path.exists() and json.loads(source_path.read_text(encoding='utf-8')) != source:
            raise ValueError('Output source identity differs; choose a new private output root')
        atomic_json(source_path, source)
        engine = None
        initialized = time.perf_counter()
        try:
            engine = make_engine()
            init_seconds = time.perf_counter() - initialized
            for model in models:
                model_id = MODELS[model]
                engine.torch.cuda.synchronize()
                activated = time.perf_counter()
                engine._load_model(model_id)
                engine.torch.cuda.synchronize()
                activation_seconds = time.perf_counter() - activated
                for _, scenario, clip in [job for job in jobs if job[0] == model]:
                    profile = json.loads(json.dumps(profile_metadata(model_id)))
                    profile['generation_options'] = generation_options(model_id, clip.get('language', 'sk'))
                    profile = json.loads(json.dumps(profile))
                    identity = dict(source_fingerprint=fingerprint, corpus_manifest_sha256=corpus_hash,
                                    audio_sha256=clip['audio']['sha256'], samples=clip['_samples'],
                                    scenario=scenario, model_id=model_id, language=clip.get('language', 'sk'),
                                    profile=profile)
                    folder = output / scenario / model
                    folder.mkdir(parents=True, exist_ok=True)
                    result_path = folder / (clip['clip_id'] + '.json')
                    job_path = result_path.with_suffix('.job.json')
                    job_key = f'{scenario}/{model}/{clip["clip_id"]}'
                    if result_path.exists():
                        if args.resume and validate_resume(result_path, identity):
                            print('RESUME complete ' + job_key, flush=True)
                            continue
                        raise ValueError('Existing result requires matching --resume: ' + job_key)
                    if job_path.exists():
                        previous = json.loads(job_path.read_text(encoding='utf-8'))
                        if not args.resume or previous.get('identity') != identity:
                            raise ValueError('Partial job requires matching identity and --resume: ' + job_key)
                    event_path = result_path.with_suffix('.events.jsonl')
                    if event_path.exists():
                        if not job_path.exists():
                            raise ValueError('Unidentified existing checkpoint: ' + str(event_path))
                        event_path.rename(event_path.with_name(event_path.name + '.partial-' + str(time.time_ns())))
                    atomic_json(job_path, dict(status='running', identity=identity))
                    print('START ' + job_key, flush=True)
                    pcm = read_pcm(clip['_path'])
                    engine.torch.cuda.reset_peak_memory_stats()
                    try:
                        with event_path.open('w', encoding='utf-8', buffering=1) as stream:
                            def checkpoint(event):
                                stream.write(json.dumps(event, ensure_ascii=False) + '\n')
                                stream.flush()

                            def infer(values):
                                engine.torch.cuda.synchronize()
                                begun = time.perf_counter()
                                data = pcm_wav(values)
                                response = engine.transcribe(data, '', model_id, clip.get('language', 'sk'))
                                engine.torch.cuda.synchronize()
                                response['request_wall_seconds'] = time.perf_counter() - begun
                                return response

                            result = replay(scenario, pcm, infer, checkpoint)
                        result.update(status='complete', schema_version=1, identity=identity,
                                      model=model, model_id=model_id, recording_id=clip['clip_id'],
                                      created_utc=datetime.now(timezone.utc).isoformat(),
                                      generation_profile=profile, gpu_mutex=lock,
                                      runtime_init_seconds=init_seconds,
                                      shared_model_activation_seconds=activation_seconds,
                                      model_activation_policy='once per model-group; excluded from warm replay',
                                      cold_first_preview_estimate_seconds=(None if result['first_text_seconds'] is None else
                                          result['first_text_seconds'] + init_seconds + activation_seconds),
                                      cuda_peak_allocated_mib=engine.torch.cuda.max_memory_allocated() / 2**20,
                                      torch_version=engine.torch.__version__,
                                      transformers_version=__import__('transformers').__version__,
                                      reference=clip.get('reference', {}))
                        reference = clip.get('reference', {})
                        if reference.get('score_enabled') and reference.get('confirmed_human'):
                            ref_path = Path(reference['path'])
                            ref_path = ref_path if ref_path.is_absolute() else manifest_path.parent / ref_path
                            words = ref_path.read_text(encoding='utf-8-sig').split()
                            result['wer'] = edit_distance(words, result['text'].split()) / max(1, len(words))
                            result['accuracy_status'] = 'confirmed_human_reference_case_sensitive_whitespace_tokenized_WER'
                        text_path = result_path.with_suffix('.txt')
                        text_path.write_text(result['final_repaired_text'] + '\n', encoding='utf-8')
                        timeline_path = result_path.with_suffix('.timeline.txt')
                        timeline_path.write_text(timeline_text(result), encoding='utf-8')
                        result['artifacts'] = {path.name: sha256(path) for path in (event_path, text_path, timeline_path)}
                        atomic_json(result_path, result)  # Completion marker only after every artifact exists.
                        atomic_json(job_path, dict(status='complete', identity=identity))
                        print('DONE ' + job_key + f' requests={len(result["events"])} '
                              f'compute={result["cumulative_inference_seconds"]:.3f}s coverage=1.000000', flush=True)
                    except BaseException as error:
                        atomic_json(job_path, dict(status='cancelled' if isinstance(error, KeyboardInterrupt) else 'failed',
                                                   identity=identity, error_type=type(error).__name__))
                        raise
                engine.unload()
        finally:
            if engine is not None:
                engine.unload()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
