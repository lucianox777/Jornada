import importlib.util
import copy
import json
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-e2e-1k-factual.py"
spec = importlib.util.spec_from_file_location("factual", SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class FactualTests(unittest.TestCase):
    def setUp(self):
        self.rows = [{"codigoPessoaOrigem": f"SYNTH-{i:032X}",
                      "idPessoaEntrega": f"SYNTH-DEL-{i:032X}",
                      "cpf": "synthetic" if i < 3 else None} for i in range(6)]

    def test_both_contracts_and_both_cpf_classes(self):
        for manager in ("SEHAB", "SMADS"):
            facts = m.select_facts(self.rows, manager, 1)
            self.assertEqual(len(facts), 4)
            self.assertEqual(sum(x["hasCpf"] for x in facts), 2)
            self.assertEqual(facts, m.select_facts(list(reversed(self.rows)), manager, 1))
            self.assertTrue(all("cpf" not in x["payload"] for x in facts))
            self.assertTrue(set(x["payload"]["codigoRegistroOrigem"] for x in facts).isdisjoint(
                x["payload"]["codigoRegistroOrigem"] for x in m.select_facts(self.rows, manager, 2)))

    def test_delivery_manifest_and_truth_match_actual_package(self):
        raw = m.encoded({"GestorCodigo": "SEHAB", "PackageFileName": "old.zip",
                         "BasePersonId": "base", "OpaquePersonId": "opaque"}) + b"\n"
        manifest = {"truthSidecar": {"path": "bridge-truth.jsonl", "sha256": m.sha(raw)},
                    "packages": [{"GestorCodigo": "SEHAB", "FileName": "old.zip",
                                  "sha256": "A" * 64, "PeopleCount": 1}]}
        original = {"wave": 1, "packages": [{"manager": "SEHAB", "fileName": "old.zip",
                    "zipSha256": "A" * 64, "zipBytes": 5, "versions": 1}]}
        delivered = copy.deepcopy(original)
        delivered["packages"][0].update(fileName="new.zip", zipSha256="B" * 64, zipBytes=9)
        result, truth = m.delivery_wave(manifest, raw, original, delivered)
        self.assertEqual(result["truthSidecar"]["path"], "bridge-truth.jsonl")
        self.assertEqual(result["truthSidecar"]["sha256"], m.sha(truth))
        self.assertEqual(result["packages"][0]["FileName"], "new.zip")
        self.assertEqual(result["packages"][0]["sha256"], "B" * 64)
        self.assertEqual(json.loads(truth), {**json.loads(raw), "PackageFileName": "new.zip"})
        self.assertEqual(manifest["packages"][0]["FileName"], "old.zip")
        delivered["packages"][0]["versions"] = 2
        with self.assertRaises(ValueError):
            m.delivery_wave(manifest, raw, original, delivered)

    def test_missing_cpf_classes_fails(self):
        with self.assertRaises(ValueError):
            m.select_facts(self.rows[:3], "SEHAB", 1)

    def test_payload_mismatch_is_not_success(self):
        m.eq_fields({"valorConcedido": 613}, {"valorConcedido": 613})
        for actual in ({}, {"valorConcedido": 614}, {"valorConcedido": "613"}):
            with self.assertRaises(ValueError):
                m.eq_fields(actual, {"valorConcedido": 613})


if __name__ == "__main__":
    unittest.main()
