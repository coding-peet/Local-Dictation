import unittest
from benchmark_tools import score

class MetricTests(unittest.TestCase):
    def test_case_and_punctuation_are_ignored(self):
        self.assertEqual(score('BOM, Nexus!', 'bom nexus')['wer'],0)
    def test_insertion_deletion_substitution(self):
        d=score('jeden dva tri štyri','jeden dve tri navyše štyri')
        self.assertEqual((d['substitutions'],d['insertions'],d['deletions']),(1,1,0))
        self.assertEqual(d['wer'],0.5)
        d=score('jeden dva tri','jeden tri')
        self.assertEqual(d['deletions'],1)
        self.assertAlmostEqual(d['wer'],1/3)
    def test_diacritics_and_part_numbers_are_preserved(self):
        self.assertEqual(score('päť ABC1c','pat ABC1b')['wer'],1)
    def test_completely_empty_output_is_all_deletions(self):
        d=score('jeden dva','')
        self.assertEqual((d['wer'],d['deletions']),(1,2))
