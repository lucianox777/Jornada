"""Fast hermetic regression for indexed in-memory SQL blocking diagnostics."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-sqlite-memory-cells.py"
spec = importlib.util.spec_from_file_location("memory_sql_v9", SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def row(oid, base, gestor, *, split="TRAIN", name="MARIA DA SILVA",
        mother="ANA DE SOUZA LIMA", birth="", cpf="",
        phon="MRS", mother_phon="ANSLM"):
    return {
        "observacao_id": oid, "base_person_id": base,
        "particao": split, "gestor": gestor,
        "cpf": cpf, "nome_normalizado": name, "nome_fonetico": phon,
        "nome_mae_normalizado": mother, "nome_mae_fonetico": mother_phon,
        "data_nascimento": birth,
    }


class SqliteMemoryCellsTests(unittest.TestCase):
    def test_uses_real_memory_sql_and_recovers_pm_without_birth(self):
        rows = [row("a", "p", "G1"), row("b", "p", "G2"),
                row("c", "u", "G3")]
        report = m.assess(rows)
        train = report["results"]["TRAIN"]
        self.assertEqual(report["engine"], "sqlite3_memory_real_indexed_sql")
        self.assertEqual(report["selection_split"], "TRAIN_ONLY")
        self.assertFalse(report["TEST_evaluated"])
        self.assertFalse(report["fdr_measured"])
        self.assertEqual(train["all_no_cpf_truth_M"], 1)
        self.assertEqual(train["name_eligible_truth_M"], 1)
        self.assertEqual(train["metrics"]["UNION_4"]["M"], 1)
        self.assertEqual(train["metrics"]["UNION_4"]["U"], 2)
        self.assertEqual(train["cells"]["P_PHON_M_LAST"]["M"], 1)
        self.assertEqual(train["cells"]["P_ANY_M_PHON"]["M"], 1)
        self.assertEqual(train["cells"]["P_PHON_BIRTH_YEAR"]["M"], 0)
        self.assertEqual(report["frozen_train_plan_evaluated_without_validation_refit"]["TRAIN"]["M"], 1)
        self.assertNotIn("MARIA DA SILVA", json.dumps(report))

    def test_name_is_required_even_with_mother_and_date(self):
        rows = [row("1", "p", "A", name="NÃO INFORMADO", birth="1980-05-01"),
                row("2", "p", "B", name="NÃO INFORMADO", birth="1980-05-01")]
        a = m.assess(rows)
        self.assertEqual(a["results"]["TRAIN"]["all_no_cpf_truth_M"], 1)
        self.assertEqual(a["results"]["TRAIN"]["name_eligible_truth_M"], 0)
        self.assertEqual(a["results"]["TRAIN"]["abstaining_observations"], 2)
        self.assertIsNone(a["results"]["TRAIN"]["metrics"]["UNION_4"]["recall"])

    def test_pd_valid_without_mother_and_sentinel_abstains(self):
        rows = [row("a", "p", "G1", mother="", mother_phon="", birth="1980-01-01"),
                row("b", "p", "G2", mother="", mother_phon="", birth="1980-12-01")]
        report = m.assess(rows)
        t = report["results"]["TRAIN"]
        self.assertEqual(t["name_eligible_truth_M"], 1)
        self.assertEqual(t["cells"]["P_PHON_BIRTH_YEAR"]["M"], 1)
        self.assertEqual(t["cells"]["P_PHON_M_LAST"]["M"], 0)
        for r in rows:
            r["data_nascimento"] = "1900-01-01"
        self.assertEqual(m.assess(rows)["results"]["TRAIN"]["name_eligible_truth_M"], 0)

    def test_any_surname_prioritizes_last_meaningful_and_removes_agnome(self):
        self.assertEqual(m.surname_keys("MARIA DA SILVA OLIVEIRA NETO"),
                         ("OLIVEIRA", "SILVA"))
        self.assertEqual(m.surname_keys("ANA DE SOUZA"), ("SOUZA",))
        self.assertEqual(m.surname_keys("MARIA"), ())
        self.assertTrue(m.key("A|B", "C") != m.key("A", "B|C"))

    def test_cap_fails_entire_pass_instead_of_silently_truncating(self):
        rows = [row("a", "p", "G1"), row("b", "p", "G2"),
                row("c", "q", "G3")]
        with self.assertRaisesRegex(ValueError, "cap="):
            m.assess(rows, cap=1)

    def test_rejects_test_identity_leakage_and_duplicate_observations(self):
        with self.assertRaisesRegex(ValueError, "leakage"):
            m.assess([row("a","p","A"), row("b","p","B",split="TEST")])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            m.assess([row("a","p","A"), row("a","q","B")])

    def test_train_plan_applies_to_validation_without_refitting_or_test_metrics(self):
        rows = [row("1","p","A"),row("2","p","B"),
                row("3","v","A",split="VALIDATION",name="JOSÉ SILVA",phon="JS"),
                row("4","v","B",split="VALIDATION",name="JOSÉ SILVA",phon="JS"),
                row("5","t","A",split="TEST"),row("6","t","B",split="TEST")]
        a = m.assess(rows)
        self.assertEqual(a["results"]["VALIDATION"]["name_eligible_truth_M"],1)
        self.assertEqual(set(a["results"]), {"TRAIN","VALIDATION"})
        self.assertEqual(a["frozen_train_plan_evaluated_without_validation_refit"]["TRAIN"]["M"],1)
        self.assertEqual(a["frozen_train_plan_evaluated_without_validation_refit"]["VALIDATION"]["M"],1)

    def test_snapshot_integrity_protects_training_source_and_no_overwrite(self):
        rows = [row("a","p","A"),row("b","p","B")]
        import csv
        from io import StringIO
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)/"frozen"
            root.mkdir()
            source=root/"canonical_observations.csv"
            with source.open("w",encoding="utf-8",newline="") as fh:
                writer=csv.DictWriter(fh,fieldnames=sorted(m.REQUIRED))
                writer.writeheader()
                writer.writerows(rows)
            freq=root/"train_name_frequencies.json"
            joint=root/"train_joint_name_frequencies.json"
            freq.write_text("{}",encoding="utf-8")
            joint.write_text("{}",encoding="utf-8")
            manifest = {
                "version":"V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1",
                "projection_version":m.PROJECTION,
                "frozen":True, "test_used_for_frequency":False,
                "frozen_source_sha256":m.digest(source),
                "frequency_sha256":m.digest(freq),
                "joint_frequency_sha256":m.digest(joint)
            }
            (root/"manifest.json").write_text(json.dumps(manifest),encoding="utf-8")
            output=Path(temp)/"report.json"
            report=m.run(root,output,100)
            self.assertEqual(report["results"]["TRAIN"]["name_eligible_truth_M"],1)
            self.assertTrue(output.exists())
            with self.assertRaisesRegex(FileExistsError,"already exists"):
                m.run(root,output,100)
            with self.assertRaisesRegex(ValueError,"inside immutable snapshot"):
                m.run(root,root/"report.json",100)
            source.write_text(source.read_text()+"\n",encoding="utf-8")
            with self.assertRaisesRegex(ValueError,"SHA256"):
                m.run(root,Path(temp)/"report2.json",100)


if __name__ == "__main__":
    unittest.main()
