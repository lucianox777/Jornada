import importlib.util
import json
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-train-canonical-term-prevalence.py"
spec = importlib.util.spec_from_file_location("term_audit", SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def row(oid, person, part="TRAIN", first="ANA LIMA", last="LIMA"):
    return dict(observacao_id=oid, base_person_id=person, particao=part,
                nome_normalizado=first, nome_mae_normalizado="MARIA SOUZA",
                nome_ultimo_sobrenome=last,
                nome_sobrenomes_json=json.dumps([last] if last else []),
                nome_mae_ultimo_sobrenome="SOUZA",
                nome_mae_sobrenomes_json='["SOUZA"]')


class TermAuditTests(unittest.TestCase):
    def test_deduplicates_by_base_identity(self):
        report = m.assess([row("a", "p"), row("b", "p"), row("c", "q")])
        self.assertEqual(report["distinct_base_persons"], 2)
        self.assertEqual(report["attributes"]["NOME_PRENOME"]["term_identity_incidences"], 2)
        self.assertEqual(report["attributes"]["NOME_ULTIMO_SOBRENOME"]["distinct_terms"], 1)
        self.assertFalse(report["test_evaluated"])
        self.assertFalse(report["llr_weight_or_joint_tf_estimated"])

    def test_rejects_non_train(self):
        with self.assertRaisesRegex(ValueError, "TRAIN-only"):
            m.assess([row("a", "p", "TEST")])
        with self.assertRaisesRegex(ValueError, "TRAIN-only"):
            m.assess([row("a", "p", "VALIDATION")])

    def test_requires_canonical_columns(self):
        item = row("a", "p")
        del item["nome_sobrenomes_json"]
        with self.assertRaisesRegex(ValueError, "C# V2"):
            m.assess([item])

    def test_surname_mismatch_fails_closed(self):
        item = row("a", "p")
        item["nome_sobrenomes_json"] = '["SOUZA"]'
        with self.assertRaisesRegex(ValueError, "noncanonical"):
            m.assess([item])

    def test_absent_surname_does_not_invent_frequency(self):
        report = m.assess([row("a", "p", last="")])
        self.assertIsNone(report["attributes"]["NOME_ULTIMO_SOBRENOME"]["max_identities_per_term"])
        self.assertFalse(report["attributes"]["NOME_ULTIMO_SOBRENOME"]["published_ibge_frequency_verified"])


if __name__ == "__main__":
    unittest.main()
