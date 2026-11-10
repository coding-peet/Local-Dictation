import unittest
from local_dictation.comparison import compare, compose

class ComparisonTests(unittest.TestCase):
    def check_roundtrip(self, a, b):
        pieces = compare(a, b)
        self.assertEqual(compose(pieces, {}), a)
        self.assertEqual(compose(pieces, {p["id"]: "verification" for p in pieces if p["different"]}), b)
        self.assertEqual(pieces, compare(a, b))
        return pieces

    def test_exact_unicode_and_whitespace(self):
        self.check_roundtrip("Žltý 😀 dom.\nCena 2,50 €.", "Žltý 😀 strom.\nCena 2,05 €.")

    def test_insert_delete_and_empty_source(self):
        pieces = self.check_roundtrip("one two three", "one three")
        self.assertTrue(any(p["different"] and not p["verification"] for p in pieces))
        self.check_roundtrip("", "áno")
        self.check_roundtrip("áno", "")

    def test_custom_does_not_change_original(self):
        pieces = compare("abc def", "abc xyz")
        diff = next(p for p in pieces if p["different"])
        before = repr(pieces)
        self.assertEqual(compose(pieces, {diff["id"]: {"custom": "čau"}}), "abc čau")
        self.assertEqual(repr(pieces), before)
