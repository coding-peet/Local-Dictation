"""Combine the frozen and dual-lane experiments without changing their inputs."""
from __future__ import annotations

import argparse
import csv
from html import escape
import json
from pathlib import Path
from statistics import median

from benchmark import MODELS, SCENARIOS, edit_distance, sha256
from report import audit_spans, normalized_words, score

DUAL_SCENARIOS = ("dual02_06", "dual03_05", "dual03_12")


def validate_output(output, protected_roots, protected_files=()):
    output = output.resolve()
    for root in protected_roots:
        root = root.resolve()
        if output.is_relative_to(root) or root.is_relative_to(output):
            raise ValueError("Report output must not overlap protected input directories")
    for path in protected_files:
        if path.resolve().is_relative_to(output):
            raise ValueError("Report output must not contain an input file")


def optional_median(values):
    present = [value for value in values if value is not None]
    return median(present) if present else None


def repair_coverage(events, sample_count):
    cursor = 0
    for event in events:
        if event["kind"] != "repair":
            continue
        start, end = event["start_sample"], event["end_sample"]
        if start != cursor or not start < end <= sample_count:
            return False
        cursor = end
    return cursor == sample_count


def pre_refinement_text(result):
    events = [e for e in result["events"] if e["kind"] != "refine"]
    return events[-1]["snapshot"] if events else ""


def references(manifest, manifest_path, overlay_path=None):
    clips = {c["clip_id"]: c for c in manifest["clips"]}
    refs = {}
    for clip_id, clip in clips.items():
        entry = clip.get("reference", {})
        if not (entry.get("score_enabled") and entry.get("confirmed_human")):
            continue
        path = Path(entry["path"])
        if not path.is_absolute():
            path = manifest_path.parent / path
        refs[clip_id] = dict(text=path.read_text(encoding="utf-8-sig"),
                             sha256=sha256(path), path=str(path),
                             provenance=entry.get("provenance"),
                             kind=entry.get("kind"), limitation=entry.get("limitation"))
    if overlay_path:
        overlay = json.loads(overlay_path.read_text(encoding="utf-8-sig"))
        seen = set()
        for entry in overlay["references"]:
            if not (entry.get("score_enabled") and entry.get("confirmed_human")):
                raise ValueError("Overlay reference is not explicitly human confirmed")
            clip_id = entry["clip_id"]
            if clip_id in seen or clip_id not in clips:
                raise ValueError("Duplicate or unknown overlay recording")
            seen.add(clip_id)
            if entry["audio_sha256"] != clips[clip_id]["audio"]["sha256"]:
                raise ValueError("Overlay audio hash mismatch")
            path = Path(entry["path"])
            if not path.is_absolute():
                path = overlay_path.parent / path
            if sha256(path) != entry["sha256"]:
                raise ValueError("Confirmed reference hash mismatch")
            text = path.read_text(encoding="utf-8-sig")
            if not text.strip() or not entry.get("provenance"):
                raise ValueError("Reference text/provenance must be nonempty")
            refs[clip_id] = dict(text=text, sha256=sha256(path), path=str(path),
                                 provenance=entry["provenance"],
                                 kind=entry.get("kind", "human_supplied_reference"),
                                 limitation=entry.get("limitation"))
    return refs


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--original-results", type=Path, required=True)
    parser.add_argument("--dual-results", type=Path, required=True)
    parser.add_argument("--controls", type=Path, required=True)
    parser.add_argument("--reference-overlay", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    checkout = Path(__file__).resolve().parents[2]
    if args.output.resolve().is_relative_to(checkout):
        parser.error("Private report must remain outside Git")
    manifest = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
    clips = {c["clip_id"]: c for c in manifest["clips"]}
    if len(clips) != 3 or len(manifest["clips"]) != 3:
        raise ValueError("This comparison requires exactly three unique corpus recordings")
    refs = references(manifest, args.manifest, args.reference_overlay)
    validate_output(args.output, [checkout, args.original_results, args.dual_results,
        Path(r"C:\AI\LocalDictation\benchmarks\20261111-live")],
        [args.manifest, args.controls, *([args.reference_overlay] if args.reference_overlay else []),
         *(Path(value["path"]) for value in refs.values())])
    manifest_hash = sha256(args.manifest)
    controls = json.loads(args.controls.read_text(encoding="utf-8-sig"))
    full = {(x["model"], x["clip_id"]): x for x in controls["results"]}
    control_profiles = {p["model_id"]: p for p in controls["profiles"]}
    profiles, source_groups, details = {}, {}, []
    for series, folder, scenarios in (("original", args.original_results, SCENARIOS),
                                       ("dual_lane", args.dual_results, DUAL_SCENARIOS)):
        fingerprints = set()
        for scenario in scenarios:
            for model, model_id in MODELS.items():
                for clip_id, clip in clips.items():
                    path = folder / scenario / model / (clip_id + ".json")
                    result = json.loads(path.read_text(encoding="utf-8"))
                    identity = result["identity"]
                    if (result.get("status") != "complete" or result["scenario"] != scenario
                            or result["model"] != model or result["recording_id"] != clip_id
                            or identity["audio_sha256"] != clip["audio"]["sha256"]
                            or identity["corpus_manifest_sha256"] != manifest_hash):
                        raise ValueError("Incomplete or mismatched result: " + str(path))
                    fingerprint = identity["source_fingerprint"]
                    fingerprints.add(json.dumps(fingerprint, sort_keys=True))
                    profile = identity["profile"]
                    if control_profiles[model_id] != profile:
                        raise ValueError("Full control generation profile differs")
                    if model in profiles and profile != profiles[model]:
                        raise ValueError("Generation profile differs across compared series")
                    profiles[model] = profile
                    control = full[(model_id, clip_id)]
                    if control["audio_sha256"] != clip["audio"]["sha256"]:
                        raise ValueError("Full control audio differs")
                    samples = clip["audio"]["samples_per_channel"]
                    coverage = (repair_coverage(result["events"], samples) if series == "dual_lane"
                                else audit_spans(result, samples)["all_final_audio_covered"])
                    item = dict(series=series, scenario=scenario, model=model,
                                clip_id=clip_id, text=result["text"],
                                result_path=str(path), complete_audio=coverage,
                                first_text_seconds=result["first_preview_seconds"],
                                first_repair_seconds=result["first_commit_seconds"],
                                after_stop_seconds=result["final_after_stop_seconds"],
                                inference_seconds=result["cumulative_inference_seconds"],
                                revised_word_count=result["revised_word_count"],
                                exact_full_control=result["text"] == control["text"],
                                raw_result=result)
                    variants = [item]
                    if scenario == "alt10":
                        pre = dict(item)
                        pre.update(scenario="alt10_before_whole_refinement",
                                   text=pre_refinement_text(result),
                                   complete_audio=audit_spans(dict(result, events=[
                                       e for e in result["events"] if e["kind"] != "refine"]),
                                       samples)["all_final_audio_covered"])
                        pre["exact_full_control"] = pre["text"] == control["text"]
                        variants.append(pre)
                    for variant in variants:
                        reference = refs.get(clip_id)
                        variant["normalized_reference_score"] = (
                            score(reference["text"], variant["text"]) if reference else None)
                        variant["normalized_distance_to_full_control"] = score(
                            control["text"], variant["text"])
                        details.append(variant)
        if len(fingerprints) != 1:
            raise ValueError("Mixed source fingerprints within " + series)
        source_groups[series] = json.loads(next(iter(fingerprints)))

    rows = []
    groups = sorted({(x["series"], x["scenario"], x["model"]) for x in details})
    for series, scenario, model in groups:
        group = [x for x in details if (x["series"], x["scenario"], x["model"]) ==
                 (series, scenario, model)]
        scored = [x for x in group if x["normalized_reference_score"]]
        words = sum(x["normalized_reference_score"]["reference_words"] for x in scored)
        errors = sum(edit_distance(normalized_words(refs[x["clip_id"]]["text"]),
                                   normalized_words(x["text"])) for x in scored)
        rows.append(dict(series=series, scenario=scenario, model=model, completed=len(group),
                         scored_recordings=len(scored), reference_words=words,
                         word_errors=errors, reference_wer=errors / words if words else None,
                         complete_audio_outputs=sum(x["complete_audio"] for x in group),
                         exact_full_controls=sum(x["exact_full_control"] for x in group),
                         first_text_median=optional_median(x["first_text_seconds"] for x in group),
                         first_repair_median=optional_median(x["first_repair_seconds"] for x in group),
                         after_stop_median=median(x["after_stop_seconds"] for x in group),
                         inference_total=sum(x["inference_seconds"] for x in group),
                         revised_word_count=sum(x["revised_word_count"] for x in group)))
    # Pre-refinement variants reuse their original replay's timing. They are an
    # accuracy/context diagnostic, not a separately timed execution.
    notes = ["Warm accelerated 20ms replay with actual measured inference; not microphone latency.",
             "Dual lanes share one serialized GPU/model. Longer disjoint repair epochs are authoritative.",
             "Dual finals have no whole-recording replacement. Compare them to alt10 BEFORE and AFTER whole refinement.",
             "alt10_before_whole_refinement reuses original replay timings; it is not a separately timed run.",
             "Distance/agreement with model controls measures consistency, not independent human accuracy.",
             "The first reference is intended wording with model-assisted clip alignment, not a new acoustic audit.",
             "Reference overlays preserve the supplied provenance/kind; corrected wording is not automatically a verbatim acoustic audit.",
             "Short-epoch drafts may be coalesced under load. Repair coverage must still cover all source samples.",
             "This three-recording case set cannot establish broad model superiority."]
    args.output.mkdir(parents=True, exist_ok=True)
    summary = dict(measured_results=126, transcript_variants=len(details), rows=rows,
                   source_groups=source_groups, profiles=profiles,
                   references={key: {k: v for k, v in value.items() if k != "text"}
                               for key, value in refs.items()}, notes=notes)
    (args.output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
                                              encoding="utf-8")
    (args.output / "transcripts.json").write_text(json.dumps(details, ensure_ascii=False, indent=2) + "\n",
                                                  encoding="utf-8")
    with (args.output / "comparison.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    def fmt(value):
        return "unscored" if value is None else f"{value:.3f}"
    table = []
    for row in sorted(rows, key=lambda x: (x["complete_audio_outputs"] != 3,
                       x["reference_wer"] is None, x["reference_wer"] or 0, x["inference_total"])):
        values = [row["scenario"], row["model"], row["scored_recordings"],
                  fmt(100 * row["reference_wer"]) if row["reference_wer"] is not None else "unscored",
                  row["complete_audio_outputs"], row["exact_full_controls"],
                  fmt(row["first_text_median"]), fmt(row["first_repair_median"]),
                  fmt(row["after_stop_median"]), fmt(row["inference_total"])]
        table.append("<tr>" + "".join("<td>" + escape(str(v)) + "</td>" for v in values) + "</tr>")
    articles = []
    for series, scenario in sorted({(x["series"], x["scenario"]) for x in details}):
        cells = []
        for item in details:
            if (item["series"], item["scenario"]) != (series, scenario):
                continue
            metric = item["normalized_reference_score"]
            label = "unscored" if metric is None else f"WER {100 * metric['wer']:.3f}%"
            cells.append("<article><h3>" + escape(item["clip_id"] + " / " + item["model"]) +
                         "</h3><p>" + escape(label) + "</p><pre>" + escape(item["text"]) + "</pre></article>")
        articles.append("<details><summary>" + escape(scenario) + "</summary>" + "".join(cells) + "</details>")
    html = """<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<title>Local Dictation: dual-lane supplement</title><style>
body{font:16px system-ui;background:#18212b;color:#e8eef5;margin:2rem;line-height:1.5}
table{border-collapse:collapse}td,th{padding:.5rem;border:1px solid #586271;text-align:left}
pre{white-space:pre-wrap;font:inherit}details{margin:1rem 0;padding:1rem;background:#222d39}
summary{cursor:pointer;font-weight:600}article{border-top:1px solid #586271;margin-top:1rem}
</style><h1>Dual-lane supplement</h1>"""
    html += "".join("<p>" + escape(note) + "</p>" for note in notes)
    html += "<p>126 measured outputs; 9 additional alt10 pre-refinement text views. Scored reference recordings: "
    html += str(len(refs)) + ".</p><table><tr>"
    html += "".join("<th>" + escape(v) + "</th>" for v in [
        "Policy", "Model", "Scored clips", "WER %", "Complete audio /3", "Exact full controls /3",
        "First text s", "First repair s", "After stop s", "Total inference s"])
    html += "</tr>" + "".join(table) + "</table>" + "".join(articles) + "</html>"
    (args.output / "report.html").write_text(html, encoding="utf-8")
    print(json.dumps(dict(measured_results=126, variants=len(details), rows=len(rows),
                          scored_recordings=len(refs), output=str(args.output))))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
