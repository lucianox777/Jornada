import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[2] / "scripts" / "v9-corpus-snapshot.py"
spec = importlib.util.spec_from_file_location("corpus_snapshot", MODULE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class CorpusSnapshotTests(unittest.TestCase):
    def test_reuse_invalidation_and_tampering(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            src = root / "input"
            src.mkdir()
            for name in module.REQUIRED:
                (src / name).write_text("synthetic-data", encoding="utf-8")
            cfg = {"seed": 42, "people": 100000, "mother_error": 0.10}
            store = root / "store"
            first = module.publish(store, cfg, src)
            self.assertEqual(first, module.publish(store, cfg, src))
            self.assertEqual(first, module.verify(store / module.identity(cfg), cfg))
            changed = {**cfg, "mother_error": 0.134}
            self.assertNotEqual(module.identity(cfg), module.identity(changed))
            with self.assertRaises(ValueError):
                module.verify(store / module.identity(cfg), changed)
            (store / module.identity(cfg) / "observacoes.csv").write_text("changed")
            with self.assertRaises(ValueError):
                module.verify(store / module.identity(cfg), cfg)
            with self.assertRaises(ValueError):
                module.publish(store, cfg, src)

    def test_missing_file_fails_closed(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):
                module.publish(Path(d) / "store", {"seed": 1}, Path(d))

if __name__ == "__main__":
    unittest.main()
