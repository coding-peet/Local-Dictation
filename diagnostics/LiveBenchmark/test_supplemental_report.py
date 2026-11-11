"""Reference and sample ownership checks; no audio/model loads."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import supplemental_report as r


class EvidenceTests(unittest.TestCase):
    def test_report_preserves_two_source_series_and_scores_only_confirmed_reference(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            reference = base / "reference.txt"
            reference.write_text("one two", encoding="utf-8")
            clips = [dict(clip_id=str(i), audio=dict(sha256=str(i), samples_per_channel=1),
                          reference=dict(path=str(reference), score_enabled=i == 0,
                                         confirmed_human=i == 0)) for i in range(3)]
            manifest = base / "manifest.json"
            manifest.write_text(json.dumps(dict(clips=clips)), encoding="utf-8")
            control_results = []
            for series, scenarios in (("original", r.SCENARIOS), ("dual", r.DUAL_SCENARIOS)):
                for scenario in scenarios:
                    for model, model_id in r.MODELS.items():
                        for clip in clips:
                            events = [dict(kind="repair" if series == "dual" else "commit",
                                           start_sample=0, end_sample=1, snapshot="one two")]
                            if scenario == "alt10":
                                events.append(dict(kind="refine", start_sample=0, end_sample=1,
                                                   snapshot="one two"))
                            result = dict(status="complete", scenario=scenario, model=model,
                                          recording_id=clip["clip_id"], text="one two", events=events,
                                          identity=dict(source_fingerprint=dict(runner=series),
                                                        audio_sha256=clip["audio"]["sha256"],
                                                        corpus_manifest_sha256=r.sha256(manifest),
                                                        profile=dict(model_id=model_id)),
                                          first_preview_seconds=1, first_commit_seconds=2,
                                          final_after_stop_seconds=3, cumulative_inference_seconds=4,
                                          revised_word_count=5)
                            path = base / series / scenario / model / (clip["clip_id"] + ".json")
                            path.parent.mkdir(parents=True, exist_ok=True)
                            path.write_text(json.dumps(result), encoding="utf-8")
            for model_id in r.MODELS.values():
                for clip in clips:
                    control_results.append(dict(model=model_id, clip_id=clip["clip_id"],
                                                text="one two", audio_sha256=clip["audio"]["sha256"]))
            controls = base / "controls.json"
            controls.write_text(json.dumps(dict(results=control_results,
                profiles=[dict(model_id=model_id) for model_id in r.MODELS.values()])), encoding="utf-8")
            args = ["--manifest", str(manifest), "--original-results", str(base / "original"),
                    "--dual-results", str(base / "dual"), "--controls", str(controls),
                    "--output", str(base / "out")]
            self.assertEqual(r.main(args), 0)
            summary = json.loads((base / "out/summary.json").read_text(encoding="utf-8"))
            self.assertEqual(summary["measured_results"], 126)
            self.assertEqual(summary["transcript_variants"], 135)
            self.assertEqual(len(summary["rows"]), 45)
            self.assertTrue(all(row["scored_recordings"] == 1 and row["reference_wer"] == 0
                                for row in summary["rows"]))
            for series in ("original", "dual"):
                for path in (base / series).rglob("*.json"):
                    empty = json.loads(path.read_text(encoding="utf-8"))
                    empty.update(text="", first_preview_seconds=None, first_commit_seconds=None)
                    for event in empty["events"]:
                        event["snapshot"] = ""
                    path.write_text(json.dumps(empty), encoding="utf-8")
            self.assertEqual(r.main(args), 0)
            summary = json.loads((base / "out/summary.json").read_text(encoding="utf-8"))
            self.assertTrue(all(row["first_text_median"] is None and row["first_repair_median"] is None
                                for row in summary["rows"]))
            changed_path = base / "dual/dual02_06/kinit/0.json"
            changed = json.loads(changed_path.read_text(encoding="utf-8"))
            changed["identity"]["profile"]["beam"] = 5
            changed_path.write_text(json.dumps(changed), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "profile"):
                r.main(args)

    def test_report_output_must_not_overlap_input_directories_or_contain_input_files(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            protected = base / "measured-results"
            for output in (protected, protected / "report", base):
                with self.assertRaisesRegex(ValueError, "overlap"):
                    r.validate_output(output, [protected])
            with self.assertRaisesRegex(ValueError, "input"):
                r.validate_output(base / "references", [], [base / "references/summary.json"])
            r.validate_output(base / "new-report", [protected], [base / "manifest.json"])

    def test_overlay_retains_supplied_wording_provenance_without_inventing_acoustic_audit(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            draft = base / "draft.txt"
            draft.write_text("corrected wording", encoding="utf-8")
            entry = dict(clip_id="a", audio_sha256="audio", path=str(draft),
                         confirmed_human=True, score_enabled=True,
                         sha256=hashlib.sha256(draft.read_bytes()).hexdigest(),
                         provenance="Peter supplied corrected wording in reply",
                         kind="user_supplied_corrected_wording",
                         limitation="No separate full listening declaration")
            overlay = base / "overlay.json"
            overlay.write_text(json.dumps(dict(references=[entry])), encoding="utf-8")
            refs = r.references(dict(clips=[dict(clip_id="a", audio=dict(sha256="audio"))]),
                                base / "manifest.json", overlay)
            self.assertEqual(refs["a"]["kind"], "user_supplied_corrected_wording")
            self.assertEqual(refs["a"]["limitation"], entry["limitation"])

    def test_only_repair_spans_establish_final_coverage(self):
        events = [dict(kind="fast", start_sample=0, end_sample=12),
                  dict(kind="repair", start_sample=0, end_sample=6),
                  dict(kind="repair", start_sample=6, end_sample=12)]
        self.assertTrue(r.repair_coverage(events, 12))
        self.assertFalse(r.repair_coverage(events[:-1], 12))

    def test_overlap_and_out_of_bounds_are_not_complete(self):
        for spans in ([(0, 7), (6, 12)], [(0, 13)], [(0, 0), (0, 12)]):
            self.assertFalse(r.repair_coverage([
                dict(kind="repair", start_sample=a, end_sample=z) for a, z in spans], 12))

    def test_adaptive_pre_refinement_is_separate_from_final(self):
        result = dict(text="whole final", events=[
            dict(kind="commit", snapshot="first"),
            dict(kind="commit", snapshot="chunked final"),
            dict(kind="refine", snapshot="whole final")])
        self.assertEqual(r.pre_refinement_text(result), "chunked final")
        self.assertEqual(result["text"], "whole final")

    def test_unconfirmed_overlay_is_not_human_accuracy(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            draft = base / "draft.txt"
            draft.write_text("draft", encoding="utf-8")
            entry = dict(clip_id="a", audio_sha256="audio", path=str(draft),
                         confirmed_human=False, score_enabled=True,
                         sha256=hashlib.sha256(draft.read_bytes()).hexdigest())
            overlay = base / "overlay.json"
            overlay.write_text(json.dumps(dict(references=[entry])), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "confirmed"):
                r.references(dict(clips=[]), base / "manifest.json", overlay)

    def test_changed_confirmed_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            draft = base / "draft.txt"
            draft.write_text("changed", encoding="utf-8")
            entry = dict(clip_id="a", audio_sha256="audio", path=str(draft),
                         confirmed_human=True, score_enabled=True, sha256="old",
                         provenance="Peter checked the complete audio")
            overlay = base / "overlay.json"
            overlay.write_text(json.dumps(dict(references=[entry])), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "hash"):
                r.references(dict(clips=[dict(clip_id="a", audio=dict(sha256="audio"))]),
                             base / "manifest.json", overlay)


if __name__ == "__main__":
    unittest.main()
