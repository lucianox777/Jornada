"""Synthetic corruption exposure is not a calibrated municipal error probability."""
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT=Path(__file__).resolve().parents[2]/"scripts"/"v9-synthetic-corruption-impact.py"
spec=importlib.util.spec_from_file_location("v9_corruption_audit",SCRIPT)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def obs(oid,base,manager,*,part="TRAIN",name="MARIA SILVA",mother="ANA LIMA",
        dob="1989-02-03",corruptions="",cpf=""):
    return {"observacao_id":oid,"base_person_id":base,"particao":part,
            "gestor":manager,"nome":name,"nome_mae":mother,
            "data_nascimento":dob,"corrupcoes":corruptions,"cpf":cpf}


class CorruptionSensitivityAuditTests(unittest.TestCase):
    def test_true_pair_labels_are_not_equated_with_common_bernoulli(self):
        rows=[
            obs("a","p","G1",name="MARA SILVA",mother="ANN LIMA",
                dob="1989-02-04",corruptions="NOME_DROP_CHAR|MAE_DOUBLE_CHAR|DATE_DAY"),
            obs("b","p","G2",mother="",dob="",corruptions="MAE_MISSING|DATE_MISSING"),
            obs("c","p","G2",cpf="12345678909"),
            obs("d","other","G1",part="VALIDATION"),
            obs("e","other","G2",part="VALIDATION"),
        ]
        report=m.analyze(rows)
        self.assertEqual(report["observations"],5)
        self.assertEqual(report["triple_observable_corruption_per_observation"]["count"],1)
        self.assertTrue(report["not_the_common_bernoulli_draw"])
        self.assertEqual(report["true_pairs_across_managers"]["ALL"]["pairs"],3)
        self.assertEqual(report["true_pairs_across_managers"]["NO_CPF"]["pairs"],2)
        self.assertEqual(report["true_pairs_across_managers"]["NO_CPF"]["rates"][
            "dob_disagrees_or_missing"]["count"],1)
        self.assertFalse(report["may_promote_v9_or_recalibrate"])
        self.assertNotIn("MARIA SILVA",json.dumps(report))
        self.assertNotIn("12345678909",json.dumps(report))

    def test_unrelated_differences_do_not_become_pair_label_corruption(self):
        r=m.analyze([
            obs("a","p","G1",name="MARIA SILVA"),
            obs("b","p","G2",name="MARIA SILVA"),
            obs("c","q","G1",name="LUISA SOUZA"),
            obs("d","q","G2",name="LUISA SOUZA")
        ])
        self.assertEqual(r["true_pairs_across_managers"]["ALL"]["pairs"],2)
        self.assertEqual(r["true_pairs_across_managers"]["ALL"]["rates"][
            "person_names_disagree"]["count"],0)

    def test_partition_leakage_and_unknown_overlay_abort(self):
        with self.assertRaisesRegex(ValueError,"leaked"):
            m.analyze([obs("a","p","G1"),obs("b","p","G2",part="TEST")])
        with self.assertRaisesRegex(ValueError,"unknown V2"):
            m.analyze([obs("a","p","G1",corruptions="UNKNOWN_OVERLAY")])


    def test_clean_bootstrap_audit_excludes_test_without_relabelling_truth(self):
        rows=[
            obs("a","train","G1",part="TRAIN",corruptions="DATE_DAY",dob="1989-02-04"),
            obs("b","train","G2",part="TRAIN"),
            obs("c","validation","G1",part="VALIDATION"),
            obs("d","validation","G2",part="VALIDATION"),
            obs("e","test","G1",part="TEST",corruptions="DATE_DAY",dob="1989-02-05"),
            obs("f","test","G2",part="TEST"),
        ]
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder); source=root/"observacoes.csv"
            with source.open("w",newline="",encoding="utf8") as file:
                writer=csv.DictWriter(file,fieldnames=list(rows[0]))
                writer.writeheader();writer.writerows(rows)
            sha=hashlib.sha256(source.read_bytes()).hexdigest()
            manifest=root/"generation-manifest.json"
            manifest.write_text(json.dumps({
                "ruleset_version":"JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
                "error_profile":"clean",
                "outputs":[{"path":"observacoes.csv","sha256":sha}]
            }))
            audited=m.run(source,root/"train-val.json",manifest,exclude_test=True)
            self.assertEqual(audited["observations"],4)
            self.assertEqual(audited["partition_scope"],"TRAIN_VALIDATION_ONLY")
            self.assertFalse(audited["test_rows_included_in_aggregates"])
            self.assertFalse(audited["test_metrics_reported_for_data_quality_audit_only"])
            self.assertNotIn("TEST",audited["observations_by_split"])
            self.assertEqual(audited["true_pairs_across_managers"]["ALL"]["pairs"],2)
            self.assertEqual(audited["true_pairs_across_managers"]["ALL"]["rates"]["dob_disagrees_or_missing"]["count"],1)
            self.assertEqual(audited["manifest_recorded_error_profile"],"clean")

    def test_manifest_sha_required_and_legacy_profile_not_invented(self):
        rows=[obs("a","p","G1"),obs("b","p","G2")]
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/"observacoes.csv";target=root/"report.json"
            with source.open("w",newline="",encoding="utf8") as f:
                w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
            mpath=root/"generation-manifest.json"
            sha=hashlib.sha256(source.read_bytes()).hexdigest()
            mpath.write_text(json.dumps({
                "ruleset_version":"JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
                "outputs":[{"path":"observacoes.csv","sha256":sha}]
            }))
            out=m.run(source,target,mpath)
            self.assertIsNone(out["manifest_recorded_error_profile"])
            self.assertFalse(out["profile_source_verified"])
            self.assertTrue(out["legacy_missing_profile_in_manifest"])
            with self.assertRaisesRegex(ValueError,"overwrite"):
                m.run(source,target,mpath)
            mpath.write_text(json.dumps({
                "ruleset_version":"JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
                "outputs":[{"path":"observacoes.csv","sha256":"0"*64}]
            }))
            with self.assertRaisesRegex(ValueError,"SHA"):
                m.run(source,root/"another.json",mpath)

if __name__=="__main__":
    unittest.main()
