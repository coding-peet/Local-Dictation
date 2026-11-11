"""Generate a private, self-contained comparison report; never run inference."""
from __future__ import annotations
import argparse
import csv
from html import escape
import json
from pathlib import Path
import re
from statistics import median
import unicodedata

from benchmark import edit_distance, SCENARIOS, MODELS


def normalized_words(text):
    # Preserve accents, numbers and technical identifiers' internal punctuation.
    return re.findall(r"[^\W_]+(?:[._'-][^\W_]+)*", unicodedata.normalize('NFC', text).casefold())


def score(reference, hypothesis):
    expected, actual = normalized_words(reference), normalized_words(hypothesis)
    chars_expected = ' '.join(expected)
    chars_actual = ' '.join(actual)
    return dict(wer=edit_distance(expected, actual)/max(1, len(expected)),
                cer=edit_distance(chars_expected, chars_actual)/max(1, len(chars_expected)),
                reference_words=len(expected), hypothesis_words=len(actual))


def phrase_repeat(text):
    """Adjacent phrase repetition candidate only; never rewrite user wording."""
    words = normalized_words(text)
    best = dict(words=0, repeats=0, phrase='')
    for width in range(3, min(13, len(words)//2+1)):
        for start in range(len(words)-2*width+1):
            phrase = words[start:start+width]
            repeats = 1
            while words[start+repeats*width:start+(repeats+1)*width] == phrase:
                repeats += 1
            if repeats >= 2 and width*repeats > best['words']*best['repeats']:
                best = dict(words=width, repeats=repeats, phrase=' '.join(phrase))
    return best


def audit_spans(result, sample_count):
    spans = [(e['start_sample'], e['end_sample']) for e in result['events'] if e['kind']=='commit']
    refined = any(e['kind']=='refine' and e['start_sample']==0 and e['end_sample']==sample_count
                  for e in result['events'])
    cursor, gaps, overlaps = 0, [], []
    for start, end in spans:
        if start > cursor:
            gaps.append([cursor, start])
        elif start < cursor:
            overlaps.append([start, cursor])
        if not start <= end <= sample_count:
            raise ValueError('Invalid sample span')
        cursor = end
    if cursor < sample_count:
        gaps.append([cursor, sample_count])
    silent = result.get('skipped_silence_spans', [])
    return dict(disjoint_commit_spans=not overlaps, commit_gaps=gaps,
                skipped_silence_spans=silent, whole_recording_refined=refined,
                full_final_audio_pass=refined or (not gaps and not overlaps))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    checkout = Path(__file__).resolve().parents[2]
    if args.output.resolve().is_relative_to(checkout):
        parser.error('Private report must be outside Git')
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    clips = {c['clip_id']: c for c in manifest['clips']}
    rows, details, missing = [], [], []
    sources = set()
    for scenario in SCENARIOS:
        for model in MODELS:
            group = []
            for clip_id, clip in clips.items():
                path = args.results/scenario/model/(clip_id+'.json')
                if not path.exists():
                    missing.append(f'{scenario}/{model}/{clip_id}')
                    continue
                result = json.loads(path.read_text(encoding='utf-8'))
                if result.get('status')!='complete' or result['identity']['audio_sha256']!=clip['audio']['sha256']:
                    raise ValueError('Incomplete or mismatched result: '+str(path))
                sources.add(json.dumps(result['identity']['source_fingerprint'], sort_keys=True))
                reference = clip.get('reference', {})
                measured = None
                if reference.get('score_enabled') and reference.get('confirmed_human'):
                    reference_path = Path(reference['path'])
                    if not reference_path.is_absolute():
                        reference_path = args.manifest.parent/reference_path
                    measured = score(reference_path.read_text(encoding='utf-8-sig'), result['text'])
                record = dict(scenario=scenario, model=model, recording=clip_id,
                              text=result['text'], normalized_reference_score=measured,
                              phrase_repeat_candidate=phrase_repeat(result['text']),
                              span_audit=audit_spans(result, clip['audio']['samples_per_channel']),
                              result_path=str(path), result=result)
                details.append(record)
                group.append(record)
            if not group:
                continue
            wer = next((x['normalized_reference_score']['wer'] for x in group
                        if x['normalized_reference_score'] is not None), None)
            rows.append(dict(scenario=scenario, model=model, completed=len(group),
                reference_wer=wer,
                first_text_median=median(x['result']['first_preview_seconds'] for x in group
                                         if x['result']['first_preview_seconds'] is not None),
                first_commit_median=median(x['result']['first_commit_seconds'] for x in group
                                           if x['result']['first_commit_seconds'] is not None),
                after_stop_median=median(x['result']['final_after_stop_seconds'] for x in group),
                compute_total=sum(x['result']['cumulative_inference_seconds'] for x in group),
                revisions=sum(x['result']['revised_word_count'] for x in group),
                min_coverage=min(x['result']['audio_coverage_fraction'] for x in group),
                max_word_run=max(x['result']['max_word_run'] for x in group),
                phrase_repeat_flags=sum(x['phrase_repeat_candidate']['repeats']>=2 for x in group),
                full_final_audio_passes=sum(x['span_audit']['full_final_audio_pass'] for x in group)))
    if len(sources)>1:
        raise ValueError('Mixed benchmark/profile/session source fingerprints')
    args.output.mkdir(parents=True, exist_ok=True)
    summary = dict(complete=len(details), expected=len(SCENARIOS)*len(MODELS)*len(clips),
                   missing=missing, rows=rows,
                   normalization='NFC lowercase; accents retained; internal dot/underscore/hyphen/apostrophe in identifiers retained',
                   accuracy_limit='Only one user supplied intended-text reference, model-assisted clip alignment; no new phonetic listening verification. Other clips have no human reference.',
                   timing_limit='Accelerated standardized 20ms replay with actual measured inference; warm models; not physical microphone latency.')
    (args.output/'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    with (args.output/'comparison.csv').open('w', newline='', encoding='utf-8-sig') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]) if rows else ['scenario'])
        writer.writeheader()
        writer.writerows(rows)
    def fmt(value):
        return '—' if value is None else f'{value:.2f}'
    table = []
    for row in sorted(rows, key=lambda r:(r['reference_wer'] is None, r['reference_wer'] or 0, r['after_stop_median'])):
        table.append('<tr>'+''.join('<td>'+escape(str(v))+'</td>' for v in [
            row['scenario'], row['model'], f"{row['completed']}/3", fmt(100*row['reference_wer']) if row['reference_wer'] is not None else 'unscored',
            fmt(row['first_text_median']), fmt(row['first_commit_median']), fmt(row['after_stop_median']),
            fmt(row['compute_total']), row['revisions'], fmt(100*row['min_coverage']), row['phrase_repeat_flags']])+'</tr>')
    transcript_blocks = []
    for scenario in SCENARIOS:
        blocks=[]
        for clip_id in clips:
            cells=[]
            for item in details:
                if item['scenario']!=scenario or item['recording']!=clip_id:
                    continue
                measured=item['normalized_reference_score']
                quality='No independent human reference' if measured is None else f"Intended-text WER {100*measured['wer']:.2f}% / CER {100*measured['cer']:.2f}%"
                cells.append('<article><h4>'+escape(item['model'])+'</h4><p class="small">'+escape(quality)+'</p><p>'+escape(item['text'])+'</p></article>')
            blocks.append('<h3>'+escape(clip_id)+'</h3><div class="models">'+''.join(cells)+'</div>')
        transcript_blocks.append('<details><summary>'+escape(scenario)+'</summary>'+''.join(blocks)+'</details>')
    document='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<title>Local Dictation private live comparison</title><style>
:root{color-scheme:light dark}body{font:16px/1.6 system-ui;max-width:1600px;margin:auto;padding:24px}h1{line-height:1.2}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:9px;border-bottom:1px solid #8885;text-align:left}th{position:sticky;top:0;background:Canvas}article{border:1px solid #8885;border-radius:8px;padding:16px;overflow-wrap:anywhere}article h4{margin:0}.models{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}details{margin:24px 0;padding:16px;border:1px solid #8885;border-radius:8px}summary{cursor:pointer;font-size:22px;font-weight:600}.small{font-size:13px;color:GrayText}.scroll{overflow:auto}@media(max-width:900px){.models{grid-template-columns:1fr}}
</style><h1>Local Dictation: private live transcription comparison</h1>'''
    document+=f'<p><strong>{len(details)}/{summary["expected"]} completed.</strong> Three complete recordings, three original FP16 models, ten alternatives and current baseline.</p>'
    document+='<p>'+escape(summary['accuracy_limit'])+'</p><p>'+escape(summary['timing_limit'])+'</p>'
    document+='<p>Table is ordered by the one referenced clip’s WER, then final latency. This is not a universal accuracy ranking. Coverage, wording, startup costs and visible revision behavior also matter. Phrase-repeat flags are candidates for inspection and never remove text.</p>'
    document+='<div class="scroll"><table><thead><tr>'+''.join('<th>'+x+'</th>' for x in ['Scenario','Model','Clips','WER % (1 clip)','First text s','First commit s','After stop s','Compute total s','Revised words','Coverage %','Phrase flags'])+'</tr></thead><tbody>'+''.join(table)+'</tbody></table></div>'
    document+='<h2>All exact transcripts</h2><p>Expand a scenario to compare three models on each recording. No wording is cleaned up.</p>'+''.join(transcript_blocks)
    (args.output/'report.html').write_text(document, encoding='utf-8')
    print(json.dumps(dict(complete=len(details), expected=summary['expected'], report=str(args.output/'report.html'))))


if __name__=='__main__':
    main()
