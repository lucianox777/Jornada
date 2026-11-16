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

    def test_optimizer_only_changes_do_not_invalidate(self):
        config = {"seed": 42, "people": 100000, "mother_error": 0.10}
        # Optimizer search dimensions are intentionally absent from the
        # generation configuration, so they must not alter its identity.
        optimizer_a = {"tf": "given_name", "hypotheses": 324}
        optimizer_b = {"tf": "surname", "hypotheses": 81}
        self.assertNotEqual(optimizer_a, optimizer_b)
        self.assertEqual(module.identity(config), module.identity(config))

    def test_symlink_store_and_payload_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            src = root / "input"
            src.mkdir()
            for name in module.REQUIRED:
                (src / name).write_text("synthetic", encoding="utf-8")
            (root / "link").symlink_to(src, target_is_directory=True)
            with self.assertRaises(ValueError):
                module.publish(root / "link", {"seed": 42}, src)
            (src / "observacoes.csv").unlink()
            (src / "observacoes.csv").symlink_to(src / "gabarito.json")
            with self.assertRaises(ValueError):
                module.publish(root / "store", {"seed": 42}, src)

    def test_published_snapshot_is_immutable_on_conflicting_source(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source_a = root / "source-a"
            source_b = root / "source-b"
            source_a.mkdir()
            source_b.mkdir()
            for name in module.REQUIRED:
                (source_a / name).write_text("original-" + name)
                (source_b / name).write_text("different-" + name)
            store = root / "store"
            first = module.publish(store, config, source_a)
            reused = module.publish(store, config, source_b)
            self.assertEqual(first, reused)
            self.assertEqual(module.verify(store / module.identity(config), config), first)
            self.assertEqual((store / module.identity(config) / "observacoes.csv").read_text(),
                             "original-observacoes.csv")

    def test_missing_file_fails_closed(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):
                module.publish(Path(d) / "store", {"seed": 1}, Path(d))

if __name__ == "__main__":
    unittest.main()
