import unittest
import report


class ReportingTests(unittest.TestCase):
    def test_normalization_preserves_accent_and_technical_identity(self):
        self.assertEqual(report.normalized_words('Áno, admin.peter.chnurik a LocalDictation.exe!'),
                         ['áno', 'admin.peter.chnurik', 'a', 'localdictation.exe'])
        self.assertNotEqual(report.normalized_words('výnimka'), report.normalized_words('vynimka'))

    def test_wer_counts_deleted_words_and_preserves_intentional_repetition(self):
        measured=report.score('Áno, áno. Bodka.', 'áno bodka')
        self.assertAlmostEqual(measured['wer'], 1/3)
        self.assertEqual(measured['reference_words'], 3)

    def test_multiword_repeat_is_flagged_without_modifying_text(self):
        text='Toto je správny text. Toto je správny text.'
        candidate=report.phrase_repeat(text)
        self.assertEqual(candidate['repeats'], 2)
        self.assertEqual(candidate['words'], 4)
        self.assertEqual(text, 'Toto je správny text. Toto je správny text.')

    def test_coverage_audit_exposes_gap_and_overlap(self):
        result={'events':[{'kind':'commit','start_sample':0,'end_sample':4},
                          {'kind':'commit','start_sample':6,'end_sample':9}]}
        audit=report.audit_spans(result, 10)
        self.assertEqual(audit['commit_gaps'], [[4,6],[9,10]])
        self.assertFalse(audit['all_final_audio_covered'])
        result['events'].append({'kind':'refine','start_sample':0,'end_sample':10})
        self.assertTrue(report.audit_spans(result, 10)['all_final_audio_covered'])

    def test_disjoint_coverage_is_not_a_whole_recording_refinement(self):
        result={'events':[{'kind':'commit','start_sample':0,'end_sample':4},
                          {'kind':'commit','start_sample':4,'end_sample':10}]}
        audit=report.audit_spans(result, 10)
        self.assertTrue(audit['all_final_audio_covered'])
        self.assertFalse(audit['whole_recording_refined'])


if __name__=='__main__':
    unittest.main()
