#!/usr/bin/env python3
"""Offline/fail-closed contract tests for synthetic HTTP volume fixture."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "scripts" / "v9-http-volume-fixture.py"
SOURCE = ROOT / "tests" / "fixtures" / "ingestao" / "AA01_v2"
spec = importlib.util.spec_from_file_location("v9_http_volume_fixture", MODULE)
assert spec and spec.loader
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def valid_cpf(cpf: str) -> bool:
    if len(cpf) != 11 or not cpf.isdigit() or len(set(cpf)) == 1:
        return False
    def digit(s: str) -> str:
        v = sum(int(x)*w for x,w in zip(s,range(len(s)+1,1,-1))) * 10 % 11
        return "0" if v == 10 else str(v)
    return cpf[-2:] == digit(cpf[:9]) + digit(cpf[:9]+cpf[9])


class VolumeFixtureTest(unittest.TestCase):
    def test_deterministic_jsonl_rows_and_cross_file_ids(self):
        with tempfile.TemporaryDirectory() as td:
            a, b = Path(td)/"a", Path(td)/"b"
            mod.generate(SOURCE, a, 19, 355)
            mod.generate(SOURCE, b, 19, 355)
            for f in ("manifest.json", "pessoas.jsonl", "registros.jsonl"):
                self.assertEqual((a/f).read_bytes(), (b/f).read_bytes())
            people = [json.loads(row) for row in (a/"pessoas.jsonl").read_text().splitlines()]
            records = [json.loads(row) for row in (a/"registros.jsonl").read_text().splitlines()]
            self.assertEqual(len(people), 19)
            self.assertEqual(len(records), 19)
            self.assertEqual([p["idPessoaEntrega"] for p in people],
                             [r["idPessoaEntrega"] for r in records])
            self.assertEqual(len({p["cpf"] for p in people}),19)
            self.assertTrue(all(valid_cpf(p["cpf"]) for p in people))
            self.assertEqual(len({r["codigoRegistroOrigem"] for r in records}),19)
            self.assertTrue(all(p["sourceTransactionId"] and p["nomeMae"] for p in people))
            self.assertEqual(len({a["sourceRecordId"] for p in people
                                  for a in p["atributosTransversais"]}), 19)

    def test_highest_allowed_count_and_original_fixture_untouched(self):
        with tempfile.TemporaryDirectory() as td:
            original = (SOURCE/"pessoas.jsonl").read_bytes()
            mod.generate(SOURCE, Path(td)/"one-thousand", 1000, 355)
            lines = (Path(td)/"one-thousand"/"pessoas.jsonl").read_text().splitlines()
            self.assertEqual(len(lines), 1000)
            self.assertEqual((SOURCE/"pessoas.jsonl").read_bytes(), original)

    def test_refuse_overwrite_and_ramps_not_predeclared(self):
        with tempfile.TemporaryDirectory() as td:
            for count, seed in ((0,355),(1001,355),(10000,355),(1,356)):
                with self.subTest(count=count, seed=seed):
                    with self.assertRaises(ValueError):
                        mod.generate(SOURCE, Path(td)/f"bad-{count}-{seed}",count,seed)
            mod.generate(SOURCE,Path(td)/"fixture",1,355)
            with self.assertRaises(FileExistsError):
                mod.generate(SOURCE,Path(td)/"fixture",1,355)

    def test_nonoverlapping_batches_reconcile_10000(self):
        with tempfile.TemporaryDirectory() as td:
            all_ids, all_cpf = set(), set()
            for batch in range(10):
                folder=Path(td)/f"batch-{batch}"
                first=batch*1000+1
                mod.generate(SOURCE,folder,1000,355,first)
                persons=[json.loads(row) for row in (folder/"pessoas.jsonl").read_text().splitlines()]
                records=[json.loads(row) for row in (folder/"registros.jsonl").read_text().splitlines()]
                self.assertEqual(len(persons),1000)
                self.assertEqual(len(records),1000)
                self.assertEqual([p["idPessoaEntrega"] for p in persons],
                                 [r["idPessoaEntrega"] for r in records])
                self.assertTrue(all(valid_cpf(p["cpf"]) for p in persons))
                for person in persons:
                    self.assertNotIn(person["idPessoaEntrega"],all_ids)
                    self.assertNotIn(person["cpf"],all_cpf)
                    all_ids.add(person["idPessoaEntrega"])
                    all_cpf.add(person["cpf"])
            self.assertEqual(len(all_ids),10000)
            self.assertEqual(len(all_cpf),10000)
            with self.assertRaises(ValueError):
                mod.generate(SOURCE,Path(td)/"outside",2,355,10000)



if __name__ == "__main__":
    unittest.main()
