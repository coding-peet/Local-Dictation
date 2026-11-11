"""Synthetic scheduler tests: never load torch, models, or private recordings."""
import array
import unittest
import benchmark as b


class PlanningTests(unittest.TestCase):
    def audio(self, seconds, voice=True):
        return array.array('h', [1000 if voice else 0]) * int(seconds*b.RATE)

    def infer(self, samples):
        return dict(text='yes yes', inference_seconds=.1, load_seconds=0, request_wall_seconds=.1)

    def test_fixed_exact_coverage_including_tail(self):
        for name in ('alt01','alt02','alt03','alt04'):
            result=b.replay(name,self.audio(17.23),self.infer)
            spans=[(e['start_sample'],e['end_sample']) for e in result['events'] if e['kind']=='commit']
            self.assertEqual(spans[0][0],0)
            self.assertEqual(spans[-1][1],int(17.23*b.RATE))
            self.assertTrue(all(a[1]==z[0] for a,z in zip(spans,spans[1:])))
            self.assertEqual(result['unprocessed_samples'],0)

    def test_legitimate_repetitions_preserved(self):
        result=b.replay('alt01',self.audio(6.1),self.infer)
        self.assertEqual(result['text'],'yes yes yes yes yes yes')

    def test_measured_backpressure(self):
        def slow(samples):
            return dict(text='word',inference_seconds=10.,load_seconds=0,request_wall_seconds=10.)
        result=b.replay('alt01',self.audio(9),slow)
        self.assertEqual([e['virtual_finished_seconds'] for e in result['events']],[13.,23.,33.])
        self.assertEqual(result['final_after_stop_seconds'],24.)

    def test_pause_threshold(self):
        pcm=self.audio(2)+self.audio(.4,False)+self.audio(11)
        result=b.replay('alt05',pcm,self.infer)
        self.assertAlmostEqual(result['events'][0]['end_sample']/b.RATE,2.36)
        self.assertTrue(all((e['end_sample']-e['start_sample'])/b.RATE<=10.02 for e in result['events']))

    def test_baseline_half_second_first_preview(self):
        result=b.replay('baseline',self.audio(2),self.infer)
        self.assertAlmostEqual(result['events'][0]['audio_available_seconds'],.5)
        self.assertEqual(result['unprocessed_samples'],0)

    def test_baseline_buffer_overload_is_visible(self):
        def slow(samples):
            return dict(text='word',inference_seconds=40.,load_seconds=0,request_wall_seconds=40.)
        result=b.replay('baseline',self.audio(60),slow)
        self.assertGreater(result['unprocessed_samples'],0)
        self.assertEqual(result['capture_stop_reason'],'baseline_32s_buffer_limit')

    def test_baseline_buffer_limit_applies_during_commit_inference(self):
        def slow_commit(samples):
            elapsed=40. if len(samples)>=20*b.RATE else .1
            return dict(text='word',inference_seconds=elapsed,load_seconds=0,request_wall_seconds=elapsed)
        result=b.replay('baseline',self.audio(60),slow_commit)
        self.assertEqual(result['accepted_samples'],32*b.RATE)
        self.assertEqual(result['unprocessed_samples'],28*b.RATE)

    def test_overlap_commits_disjoint_hops(self):
        for name in ('alt08','alt09'):
            result=b.replay(name,self.audio(25.1),self.infer)
            spans=[(e['start_sample'],e['end_sample']) for e in result['events'] if e['kind']=='commit']
            self.assertEqual(spans[0][0],0)
            self.assertEqual(spans[-1][1],int(25.1*b.RATE))
            self.assertTrue(all(a[1]==z[0] for a,z in zip(spans,spans[1:])))

    def test_adaptive_full_refine(self):
        result=b.replay('alt10',self.audio(30),self.infer)
        self.assertEqual(result['events'][-1]['kind'],'refine')
        self.assertEqual(result['events'][-1]['start_sample'],0)
        self.assertEqual(result['events'][-1]['end_sample'],30*b.RATE)
        self.assertEqual(result['text'],'yes yes')

    def test_overlap_coalesces_missed_preview_ticks(self):
        def slow(samples):
            return dict(text='word',inference_seconds=20.,load_seconds=0,request_wall_seconds=20.)
        result=b.replay('alt08',self.audio(81),slow)
        previews=[e for e in result['events'] if e['kind']=='preview']
        self.assertEqual([e['end_sample']/b.RATE for e in previews],[8.,44.,80.])
        self.assertTrue(all(e['end_sample']/b.RATE <= e['audio_available_seconds'] for e in previews))

    def test_word_metrics_never_change_text(self):
        self.assertEqual(b.edit_distance('a b a'.split(),'a a'.split()),1)
        self.assertEqual(b.repeated_boundary('yes yes','yes yes'),2)
        self.assertEqual(b.longest_word_run('yes yes yes no no'),3)


if __name__=='__main__':
    unittest.main()
