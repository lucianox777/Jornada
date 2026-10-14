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

    def test_residual_profile_identifies_missed_person_and_mother_phonetic_cell(self):
        rows = [
            row("s1", "p", "G1", name="MARIA GOMES", mother="ANA LIMA",
                phon="PSAME", mother_phon="MSAME"),
            row("s2", "p", "G2", name="MARINA SOUZA", mother="LUIZA COSTA",
                phon="PSAME", mother_phon="MSAME"),
            row("s3", "q", "G3", name="JULIA ARAUJO", mother="BETE MELO",
                phon="DIFF", mother_phon="DIFF")
        ]
        result = m.assess(rows)
        t = result["results"]["TRAIN"]
        self.assertEqual(t["name_eligible_truth_M"], 1)
        self.assertEqual(t["metrics"]["UNION_EXTENDED"]["M"], 0)
        residual = t["unrecovered_extended_truth_profile"]
        self.assertEqual(residual["unrecovered_eligible_true_M"], 1)
        self.assertEqual(residual["reason_categories"], {"PERSON_MOTHER": 1})
        self.assertEqual(
            residual["candidate_hypotheses_potential_true_M_ONLY"]["P_PHON_M_PHON"], 1)
        self.assertEqual(residual["matching_signal_counts"]["P_PHON"], 1)
        self.assertEqual(residual["matching_signal_counts"]["M_PHON"], 1)
        self.assertFalse(residual["hypothesis_U_fanout_sql_cost_measured"])
        payload = json.dumps(result)
        self.assertNotIn("MARINA SOUZA", payload)
        self.assertNotIn("LUIZA COSTA", payload)
        self.assertNotIn("s1", payload)

    def test_residual_profile_allows_person_only_without_exaggerating_gain(self):
        rows = [
            row("a", "p", "G1", name="ANA GOMES", mother="MARIA SILVA",
                phon="P1", mother_phon="M1"),
            row("b", "p", "G2", name="ANA GOMES", mother="TERESA LIMA",
                phon="P1", mother_phon="M2")
        ]
        result = m.assess(rows)["results"]["TRAIN"]
        missed = result["unrecovered_extended_truth_profile"]
        self.assertEqual(missed["unrecovered_eligible_true_M"], 1)
        self.assertEqual(missed["reason_categories"], {"PERSON_ONLY": 1})
        self.assertEqual(missed["candidate_hypotheses_potential_true_M_ONLY"], {})
        self.assertEqual(
            sum(missed["reason_categories"].values()),
            result["name_eligible_truth_M"]-result["metrics"]["UNION_EXTENDED"]["M"])

    def test_targeted_cells_recover_distinct_training_residuals_without_redefining_old_union(self):
        rows = [
            row("a1", "p1", "G1", name="JOSE SILVA", mother="MARIA SOUZA",
                phon="JS", mother_phon="MS", birth="1980-01-02"),
            row("a2", "p1", "G2", name="JOSE LIMA", mother="LUIZA COSTA",
                phon="JL", mother_phon="LC", birth="1981-01-02"),
            row("b1", "p2", "G1", name="ANA SILVA", mother="LUIZA MELO",
                phon="AS", mother_phon="LM", birth="1980-01-03"),
            row("b2", "p2", "G2", name="CARLOS SILVA", mother="MARIA ANDRADE",
                phon="CS", mother_phon="MA", birth="1980-10-04"),
            row("c1", "p3", "G1", name="MARIA SILVA", mother="ANA OLIVEIRA",
                phon="MS", mother_phon="AO"),
            row("c2", "p3", "G2", name="TERESA SILVA", mother="ANA COSTA",
                phon="TS", mother_phon="AC")
        ]
        report = m.assess(rows)
        train = report["results"]["TRAIN"]
        self.assertEqual(train["name_eligible_truth_M"], 3)
        # A dedicated new SQL index must add a real M pair beyond V1;
        # still verify legacy V1 union has not been silently rewritten.
        self.assertEqual(train["metrics"]["UNION_EXPANDED"]["M"], 3)
        self.assertGreater(
            train["metrics"]["UNION_EXPANDED"]["M"],
            train["metrics"]["UNION_EXTENDED"]["M"])
        self.assertEqual(train["cells"]["P_FIRST_DOB_MONTHDAY"]["M"], 1)
        self.assertEqual(train["cells"]["P_ANY_BIRTH_YEAR"]["M"], 1)
        self.assertEqual(train["cells"]["P_LAST_M_FIRST"]["M"], 1)
        self.assertEqual(train["unrecovered_expanded_truth_profile"]["unrecovered_eligible_true_M"], 0)
        self.assertFalse(report["fdr_measured"])

    def test_targeted_birth_cells_do_not_use_sentinal_dates(self):
        rows = [row("a", "p", "G1", name="ANA LIMA",
                    mother="MARIA SOUZA", birth="1900-01-01"),
                row("b", "p", "G2", name="ANA LIMA",
                    mother="MARIA SOUZA", birth="1900-01-01")]
        out = m.assess(rows)["results"]["TRAIN"]["cells"]
        self.assertEqual(out["P_FIRST_DOB_MONTHDAY"]["M"], 0)
        self.assertEqual(out["P_ANY_BIRTH_YEAR"]["M"], 0)
        self.assertEqual(out["P_ANY_DOB_MONTHDAY"]["M"], 0)
        self.assertEqual(out["P_PREFIX3_BIRTH_YEAR"]["M"], 0)

    def test_train_only_exact_subset_minimizes_U_under_95_recall(self):
        truth = {(f"p{i}", f"q{i}") for i in range(10)}
        baseline = set(list(truth)[:8])
        last_two = truth - baseline
        train_cells = {name: set() for name in m.PASS_ORDER}
        train_cells[m.EXTENDED[0]] = baseline | {("base-u", "x")}
        train_cells["P_FIRST_DOB_MONTHDAY"] = last_two | {("u", "1")}
        train_cells["P_ANY_BIRTH_YEAR"] = last_two | {
            ("u", "2"), ("u", "3"), ("u", "4")
        }
        fit = m.fit_min_u_plans_on_train(train_cells, truth)
        self.assertEqual(fit["status"], "TRAIN_TARGET_FEASIBLE")
        self.assertEqual(fit["required_M"], 10)
        self.assertEqual(fit["exact_targeted"]["added_cells"],
                         ["P_FIRST_DOB_MONTHDAY"])
        self.assertEqual(fit["exact_targeted"]["train_U"], 2)
        self.assertGreaterEqual(fit["greedy_pruned"]["train_M"], 10)
        self.assertLessEqual(fit["greedy_pruned"]["train_U"], 2)

    def test_train_selection_cannot_be_improved_with_validation_labels(self):
        train_truth = {("t1", "t2")}
        val_truth = {("v1", "v2")}
        train_cells = {name: set() for name in m.PASS_ORDER}
        train_cells["P_FIRST_DOB_MONTHDAY"] = set(train_truth)
        train_cells["P_ANY_BIRTH_YEAR"] = {("u1", "u2")}
        fit = m.fit_min_u_plans_on_train(train_cells, train_truth)
        self.assertEqual(fit["exact_targeted"]["added_cells"],
                         ["P_FIRST_DOB_MONTHDAY"])
        sql_pairs = {}
        for name in m.PASS_ORDER:
            sql_pairs["TRAIN", name] = (train_cells[name], 0)
            sql_pairs["VALIDATION", name] = (
                set(val_truth) if name == "P_ANY_BIRTH_YEAR" else set(), 0)
        fixed = m.apply_fixed_min_u_plans(
            fit, sql_pairs, {"TRAIN": train_truth, "VALIDATION": val_truth},
            {"TRAIN": 2, "VALIDATION": 2})
        chosen = fixed["plans"]["exact_targeted"]
        self.assertEqual(chosen["measured"]["TRAIN"]["recall"], 1)
        self.assertEqual(chosen["measured"]["VALIDATION"]["M"], 0)
        self.assertFalse(chosen["measured"]["VALIDATION"]["recall_target_met"])
        self.assertFalse(fixed["validation_refit"])

    def test_fails_closed_when_no_train_subset_can_reach_95_percent(self):
        true = {(str(i), str(i + 100)) for i in range(20)}
        cells = {name: set() for name in m.PASS_ORDER}
        cells["P_PHON_M_LAST"] = set(list(true)[:2])
        fit = m.fit_min_u_plans_on_train(cells, true)
        self.assertEqual(fit["status"], "TRAIN_TARGET_INFEASIBLE")
        self.assertIsNone(fit["exact_targeted"])
        self.assertIsNone(fit["greedy_pruned"])

    def test_global_95_takes_precedence_over_conditional_recall(self):
        truth = {(str(i), str(i + 100)) for i in range(20)}
        cells = {name: set() for name in m.PASS_ORDER}
        cells[m.EXTENDED[0]] = set(truth)
        impossible = m.fit_min_u_plans_on_train(cells, truth,
                                                 all_truth_count=22)
        self.assertEqual(impossible["status"],
                         "ELIGIBILITY_CEILING_BELOW_GLOBAL_95")
        self.assertEqual(impossible["required_all_truth_M"], 21)
        self.assertIsNone(impossible["exact_targeted"])
        feasible = m.fit_min_u_plans_on_train(cells, truth,
                                               all_truth_count=21)
        self.assertEqual(feasible["status"], "TRAIN_TARGET_FEASIBLE")
        self.assertEqual(feasible["required_M"], 20)

    def test_validation_must_meet_global_95_even_if_eligible_recall_is_100(self):
        truth = {("a", "b"), ("c", "d")}
        cells = {name: set() for name in m.PASS_ORDER}
        cells[m.EXTENDED[0]] = set(truth)
        fit = m.fit_min_u_plans_on_train(cells, truth)
        sql = {}
        for name in m.PASS_ORDER:
            sql["TRAIN", name] = (cells[name], 0)
            sql["VALIDATION", name] = (
                {("v1", "v2")} if name == m.EXTENDED[0] else set(), 0)
        result = m.apply_fixed_min_u_plans(
            fit, sql, {"TRAIN": truth, "VALIDATION": {("v1", "v2")}},
            {"TRAIN": 4, "VALIDATION": 2},
            {"TRAIN": 2, "VALIDATION": 2})
        val = result["plans"]["greedy_pruned"]["measured"]["VALIDATION"]
        self.assertEqual(val["recall"], 1)
        self.assertEqual(val["recall_against_all_no_cpf_truth_M"], 0.5)
        self.assertTrue(val["eligible_recall_target_met"])
        self.assertFalse(val["global_recall_target_met"])
        self.assertFalse(val["recall_target_met"])

    def test_identity_cluster_bootstrap_reports_no_certification_or_PII(self):
        rows = [
            row("a", "p1", "G1"), row("b", "p1", "G2"),
            row("c", "p2", "G1"), row("d", "p2", "G2"),
            row("e", "p3", "G1"), row("f", "p3", "G2")
        ]
        all_truth = m.truth_for(rows, False)
        assert len(all_truth) == 3
        first = m.cluster_bootstrap_global_recall(
            rows, all_truth, {("a", "b"), ("c", "d")}, rounds=200, seed=123)
        second = m.cluster_bootstrap_global_recall(
            rows, all_truth, {("a", "b"), ("c", "d")}, rounds=200, seed=123)
        self.assertEqual(first, second)
        self.assertEqual(first["match_bearing_base_identities"], 3)
        self.assertAlmostEqual(first["point_recall"], 2/3)
        self.assertLess(first["lower_one_sided_95"], 0.95)
        self.assertFalse(first["statistically_supported_global_95"])
        self.assertFalse(first["population_or_fdr_certification"])
        self.assertNotIn("p1", json.dumps(first))
        # Deterministic successful identity recall should have bootstrap
        # lower confidence bound at exactly 100%.
        total = m.cluster_bootstrap_global_recall(
            rows, all_truth, all_truth, rounds=200, seed=123)
        self.assertEqual(total["lower_one_sided_95"], 1)
        self.assertTrue(total["statistically_supported_global_95"])

    def test_cluster_bootstrap_rejects_wrong_true_pair_identity_and_bad_rounds(self):
        rows = [row("a", "p1", "G1"), row("b", "p2", "G2")]
        with self.assertRaisesRegex(ValueError, "intra-identity"):
            m.cluster_bootstrap_global_recall(rows, {("a", "b")}, set())
        with self.assertRaisesRegex(ValueError, "iterations"):
            m.cluster_bootstrap_global_recall(
                rows, set(), set(), rounds=10)
        empty = m.cluster_bootstrap_global_recall(
            rows, set(), set(), rounds=100)
        self.assertIsNone(empty["point_recall"])
        self.assertFalse(empty["statistically_supported_global_95"])

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
