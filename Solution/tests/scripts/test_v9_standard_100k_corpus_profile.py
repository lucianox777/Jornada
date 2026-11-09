"""Generator 100k profiler never turns resource numbers into V9 evidence."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SOLUTION = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("v9_std_profiler",
    SOLUTION / "scripts/v9-standard-100k-corpus-profile.py")
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)

class V9Standard100kProfileTests(unittest.TestCase):
    def test_aggregate_rss_only_no_scientific_claims_or_personal_data(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for name in ("observacoes.csv","pessoas_verdade.csv","gabarito.json","generation-manifest.json"):
                (root/name).write_text("fixture", encoding="utf-8")
            sample = root / "rss-kib"
            sample.write_text("12345\n", encoding="utf-8")
            with mock.patch.object(p.binder, "corpus_provenance",
                                   return_value=({}, "a"*64, "b"*64)):
                result = p.create_report(root, sample)
            self.assertEqual(result["generatorPeakResidentBytes"], 12345*1024)
            self.assertEqual(result["generatedCorpusLogicalBytes"], 28)
            self.assertEqual(result["people"], 100000)
            self.assertFalse(result["testUsed"])
            self.assertFalse(result["certified"])
            self.assertFalse(result["activated"])
            self.assertIn("GENERATOR_ONLY_NO_SQL", result["scope"])
            self.assertNotIn("rows", result)
            sample.write_text("invalid", encoding="utf-8")
            with mock.patch.object(p.binder, "corpus_provenance",
                                   return_value=({}, "a"*64, "b"*64)):
                with self.assertRaisesRegex(ValueError, "RSS"):
                    p.create_report(root, sample)

if __name__ == "__main__":
    unittest.main()
