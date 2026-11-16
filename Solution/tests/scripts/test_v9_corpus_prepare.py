import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2] / "scripts"
spec = importlib.util.spec_from_file_location("v9_prepare", ROOT / "v9-corpus-prepare.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)

class CorpusPrepareTests(unittest.TestCase):
    def test_generate_once_then_reuse(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            out = root / "generated"
            store = root / "store"
            config = {"seed": 42, "people": 100000}
            script = ("from pathlib import Path; import sys; "
                      "p=Path(sys.argv[1]); p.mkdir(exist_ok=True); "
                      "[(p/n).write_text('synthetic') for n in "
                      "('pessoas_verdade.csv','observacoes.csv','gabarito.json','generation-manifest.json')]")
            import sys
            first = prepare.prepare(store, config, [sys.executable, "-c", script, str(out)], out)
            self.assertEqual(first["status"], "GENERATED")
            second = prepare.prepare(store, config, [], out)
            self.assertEqual(second["status"], "REUSED")
            self.assertEqual(first["fingerprint"], second["fingerprint"])
            handoff = prepare.handoff(store, config, root / "train" / "corpus")
            self.assertEqual(handoff["status"], "HANDOFF_VERIFIED")
            self.assertEqual(handoff["fingerprint"], first["fingerprint"])
            (Path(second["corpus"]) / "observacoes.csv").write_text("tampered")
            with self.assertRaises(ValueError):
                prepare.prepare(store, config, [], out)
            with self.assertRaises(ValueError):
                prepare.handoff(store, config, root / "second-train" / "corpus")

    def test_handoff_requires_preparation_lock(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic")
            store = root / "store"
            prepare.snapshot.publish(store, config, source)
            with self.assertRaisesRegex(ValueError, "preparation lock"):
                prepare.handoff(store, config, root / "consumer")

    def test_handoff_rejects_symlink_preparation_lock(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic")
            store = root / "store"
            prepare.snapshot.publish(store, config, source)
            (store / ".prepare.lock").symlink_to(root / "outside-lock")
            with self.assertRaisesRegex(ValueError, "preparation lock"):
                prepare.handoff(store, config, root / "consumer")
            self.assertFalse((root / "consumer").exists())

    def test_reused_corrupt_corpus_never_regenerates(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic")
            store = root / "store"
            prepare.snapshot.publish(store, config, source)
            (store / prepare.snapshot.identity(config) / "observacoes.csv").write_text("tampered")
            marker = root / "generator-was-run"
            import sys
            with self.assertRaises(ValueError):
                prepare.prepare(store, config, [sys.executable, "-c",
                    "from pathlib import Path; import sys; Path(sys.argv[1]).touch()",
                    str(marker)], root / "fresh")
            self.assertFalse(marker.exists())

    def test_handoff_rejects_symlinked_store(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic")
            store = root / "real-store"
            prepare.snapshot.publish(store, config, source)
            (store / ".prepare.lock").touch()
            alias = root / "alias-store"
            alias.symlink_to(store, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "corpus store"):
                prepare.handoff(alias, config, root / "consumer")
            self.assertFalse((root / "consumer").exists())

    def test_cross_process_reuse_without_generator(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic-" + name)
            store = root / "store"
            config = {"seed": 42}
            prepare.snapshot.publish(store, config, source)
            (store / ".prepare.lock").touch()
            config_path = root / "config.json"
            import json
            import subprocess
            import sys
            config_path.write_text(json.dumps(config), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(ROOT / "v9-corpus-prepare.py"),
                 "--store", str(store), "--config", str(config_path),
                 "--generation-output", str(root / "unused-output"),
                 "--handoff-to", str(root / "consumer"),
                 "--receipt", str(root / "receipt.json")],
                check=True, capture_output=True, text=True)
            self.assertIn("V9_CORPUS_PREPARE_REUSED", result.stdout)
            self.assertFalse((root / "unused-output").exists())
            self.assertEqual(
                prepare.snapshot.verify(root / "consumer", config)["fingerprint"],
                prepare.snapshot.identity(config))

    def test_preparation_lock_rejects_nonregular_inode(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            store = root / "store"
            store.mkdir()
            import os
            os.mkfifo(store / ".prepare.lock")
            with self.assertRaisesRegex(ValueError, "preparation lock"):
                prepare.prepare(store, {"seed": 42}, [], root / "out")
            with self.assertRaisesRegex(ValueError, "preparation lock"):
                prepare.handoff(store, {"seed": 42}, root / "consumer")

    def test_cache_miss_without_generator_fails(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):
                prepare.prepare(Path(d)/"store", {"seed": 42}, [], Path(d)/"out")

    def test_handoff_rejects_symlink_intermediate_destination(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            config = {"seed": 42}
            source = root / "source"
            source.mkdir()
            for name in prepare.snapshot.REQUIRED:
                (source / name).write_text("synthetic")
            store = root / "store"
            prepare.snapshot.publish(store, config, source)
            (store / ".prepare.lock").touch()
            real = root / "real"
            real.mkdir()
            alias = root / "alias"
            alias.symlink_to(real, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "ancestor"):
                prepare.handoff(store, config, alias / "nested" / "corpus")
            self.assertFalse((real / "nested").exists())

    def test_prepare_rejects_symlink_store_ancestor_before_generation(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            real = root / "real"
            real.mkdir()
            alias = root / "alias"
            alias.symlink_to(real, target_is_directory=True)
            marker = root / "generator-ran"
            import sys
            with self.assertRaisesRegex(ValueError, "ancestor"):
                prepare.prepare(alias / "store", {"seed": 42},
                    [sys.executable, "-c", "from pathlib import Path; import sys; Path(sys.argv[1]).touch()", str(marker)],
                    root / "out")
            self.assertFalse(marker.exists())
            self.assertFalse((real / "store").exists())
            with self.assertRaisesRegex(ValueError, "unsafe corpus store"):
                prepare.handoff(alias / "store", {"seed": 42}, root / "consumer")

if __name__ == "__main__":
    unittest.main()
