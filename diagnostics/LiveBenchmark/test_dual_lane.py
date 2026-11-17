"""Synthetic replay and persistence tests. No model, torch or private corpus."""
import array
import json
from pathlib import Path
import tempfile
import unittest

try:
    import dual_lane as d
except ModuleNotFoundError:
    d = None


class ReplayTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(d, 'dual_lane implementation must exist')

    def audio(self, seconds):
        return array.array('h', [1000]) * round(seconds * 16000)

    def infer(self, pcm):
        return dict(text='yes yes', inference_seconds=.1, load_seconds=0,
                    request_wall_seconds=.1)

    def test_repairs_replace_all_short_text_without_lexical_dedup(self):
        result = d.replay('dual02_06', self.audio(12.23), self.infer)
        self.assertEqual(result['text'], 'yes yes yes yes yes yes')
        self.assertEqual(result['final_repaired_text'], result['text'])
        self.assertEqual(result['repair_spans'], [[0, 96000], [96000, 192000], [192000, 195680]])
        self.assertEqual(result['events'][2]['kind'], 'repair')
        self.assertEqual(result['events'][2]['snapshot'], 'yes yes')
        self.assertEqual(result['events'][2]['replaced_draft_span_count'], 2)
        self.assertGreater(result['revised_word_count'], 0)

    def test_mismatched_cadence_restarts_at_repaired_boundary(self):
        result = d.replay('dual03_05', self.audio(11.01), self.infer)
        self.assertEqual([(e['kind'], e['start_sample'], e['end_sample']) for e in result['events']],
                         [('draft', 0, 48000), ('repair', 0, 80000),
                          ('draft', 80000, 128000), ('repair', 80000, 160000),
                          ('repair', 160000, 176160)])
        self.assertEqual(result['unprocessed_samples'], 0)
        self.assertEqual(result['audio_coverage_fraction'], 1)

    def test_slow_inference_prioritizes_repairs_without_future_audio_or_loss(self):
        def slow(pcm):
            return dict(text='word', inference_seconds=10, request_wall_seconds=10)
        result = d.replay('dual02_06', self.audio(19.01), slow)
        self.assertEqual([e['kind'] for e in result['events']], ['draft'] + ['repair'] * 4)
        self.assertEqual([e['virtual_finished_seconds'] for e in result['events']], [12, 22, 32, 42, 52])
        self.assertEqual(result['repair_spans'], [[0, 96000], [96000, 192000], [192000, 288000], [288000, 304160]])
        self.assertAlmostEqual(result['final_after_stop_seconds'], 32.99)
        self.assertTrue(all(e['end_sample'] <= e['available_samples_at_request'] for e in result['events']))
        self.assertTrue(all(e['start_sample'] >= e['repaired_until_sample_at_request'] for e in result['events']))

    def test_backlogged_drafts_coalesce_and_keep_visible_chronological_hypotheses(self):
        calls = 0
        def uneven(pcm):
            nonlocal calls
            calls += 1
            return dict(text=f'draft{calls}', inference_seconds=6 if calls == 1 else .1,
                        request_wall_seconds=6 if calls == 1 else .1)
        result = d.replay('dual03_12', self.audio(12), uneven)
        self.assertEqual([(e['start_sample'], e['end_sample']) for e in result['events']],
                         [(0, 48000), (96000, 144000), (0, 192000)])
        self.assertEqual(result['events'][1]['snapshot'], 'draft1 draft2')
        self.assertEqual(result['events'][1]['visible_draft_spans'], [[0, 48000], [96000, 144000]])
        self.assertEqual(result['skipped_draft_spans'], [[48000, 96000]])
        self.assertEqual(result['text'], 'draft3')

    def test_partial_frame_tail_and_silent_audio_are_never_dropped(self):
        for scenario in ('dual02_06', 'dual03_05', 'dual03_12'):
            pcm = array.array('h', [0]) * 16001
            result = d.replay(scenario, pcm, self.infer)
            self.assertEqual(result['repair_spans'], [[0, 16001]])
            self.assertEqual(result['events'][0]['end_sample'], 16001)
            self.assertEqual(result['text'], 'yes yes')
            self.assertAlmostEqual(result['first_text_seconds'], 1.0000625 + .1)

    def test_invalid_audio_scenario_and_measured_time_rejected(self):
        with self.assertRaises(ValueError):
            d.replay('dual02_06', [], self.infer)
        with self.assertRaises(ValueError):
            d.replay('wrong', self.audio(1), self.infer)
        for elapsed in (-1, float('nan'), float('inf')):
            with self.assertRaises(ValueError):
                d.replay('dual02_06', self.audio(1), lambda pcm: dict(text='x', request_wall_seconds=elapsed))


class ResumeTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(d, 'dual_lane implementation must exist')

    def test_resume_rejects_changed_source_profile_audio_and_missing_artifact(self):
        with tempfile.TemporaryDirectory(dir=Path.cwd()) as folder:
            path = Path(folder) / 'clip.json'
            artifact = Path(folder) / 'clip.txt'
            artifact.write_text('kept', encoding='utf-8')
            identity = {'source': 'abc', 'profile': {'beam': 1}, 'audio': '123'}
            result = dict(status='complete', identity=identity,
                          artifacts={artifact.name: d.sha256(artifact)})
            path.write_text(json.dumps(result), encoding='utf-8')
            self.assertTrue(d.validate_resume(path, identity))
            for changed in ({**identity, 'source': 'bad'}, {**identity, 'profile': {'beam': 5}},
                            {**identity, 'audio': 'bad'}):
                with self.assertRaises(ValueError):
                    d.validate_resume(path, changed)
            artifact.unlink()
            with self.assertRaises(ValueError):
                d.validate_resume(path, identity)


if __name__ == '__main__':
    unittest.main()
