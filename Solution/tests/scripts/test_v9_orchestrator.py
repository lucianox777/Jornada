#!/usr/bin/env python3
"""Behavioural and structural tests of the V9 parallel orchestrator (no network, SQL or 30k)."""
import importlib.util
import json
import re
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SOLUTION = Path(__file__).resolve().parents[2]
REPO = SOLUTION.parent
spec = importlib.util.spec_from_file_location("v9_orchestrator", SOLUTION / "scripts" / "v9-orchestrator.py")
orch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(orch)
WORKFLOW = (REPO / ".github" / "workflows" / "v9-parallel-orchestrator.yml").read_text(encoding="utf-8")
PROBE = ".github/workflows/v9-physical-seed42-probe.yml"
SHA_A, SHA_B = "a" * 40, "b" * 40


def job_blocks(text):
    """Split the top-level `jobs:` mapping into {job_id: block text} without a YAML dependency."""
    body = text.split("\njobs:\n", 1)[1]
    blocks, current = {}, None
    for line in body.splitlines():
        m = re.match(r"^  ([a-z0-9-]+):\s*$", line)
        if m:
            current = m.group(1)
            blocks[current] = []
        elif current:
            blocks[current].append(line)
    return {k: "\n".join(v) for k, v in blocks.items()}


def run(id_, sha, branch, conclusion="success", mode="execute_30k", path=PROBE, event="workflow_dispatch"):
    return {"id": id_, "path": path, "event": event, "head_sha": sha, "head_branch": branch,
            "conclusion": conclusion, "mode": mode}


def front(**kw):
    base = {"id": "X", "pr": 1, "branch": "feat/x", "sha": SHA_A,
            "physical": [{"id": "seed42-30k-train", "workflow": PROBE, "mode": "execute_30k"}],
            "physical_check_names": ["SQL físico 30k clean — FS blind Rank-Resolve TF0"]}
    base.update(kw)
    return base


GREEN = [{"name": "unit", "status": "completed", "conclusion": "success"},
         {"name": "SQL físico 30k clean — FS blind Rank-Resolve TF0", "status": "completed", "conclusion": "failure"}]


class WorkflowStructureTests(unittest.TestCase):
    def test_1_independent_fronts_run_in_parallel(self):
        jobs = job_blocks(WORKFLOW)
        for name in ("references", "artifact-preflight", "executor-contract", "fronts-status"):
            self.assertNotIn("needs:", jobs[name], name)
        self.assertNotRegex(WORKFLOW.split("\njobs:\n")[0], r"cancel-in-progress:\s*true")
        self.assertIn("group: ${{ github.workflow }}-${{ github.run_id }}", WORKFLOW)

    def test_2_real_dependency_respected(self):
        self.assertIn("needs: [references, artifact-preflight, executor-contract, fronts-status]",
                      job_blocks(WORKFLOW)["summary"])

    def test_3_one_failure_does_not_cancel_others(self):
        jobs = job_blocks(WORKFLOW)
        self.assertIn("fail-fast: false", jobs["artifact-preflight"])
        self.assertIn("if: always()", jobs["summary"])

    def test_4_never_dispatches_30k(self):
        header = WORKFLOW.split("\njobs:\n")[0]
        self.assertIn("actions: read", header)
        self.assertNotRegex(header, r"actions:\s*write|contents:\s*write")
        for forbidden in ("execute_30k", "/dispatches", "gh workflow run", "workflow_call", "push:"):
            self.assertNotIn(forbidden, WORKFLOW)

    def test_10_summary_fails_when_any_job_failed(self):
        self.assertIn('for r in "$R1" "$R2" "$R3" "$R4"; do test "$r" = success; done', job_blocks(WORKFLOW)["summary"])

    def test_11_no_activation_or_publication_commands(self):
        for forbidden in ("DT-15 publish", "--v9-publish", "ATIVA", "modelo_linkage", "operationalActivation=true"):
            self.assertNotIn(forbidden, WORKFLOW)


class EvidenceTests(unittest.TestCase):
    def test_5_divergent_sha_blocks(self):
        row = orch.front_status(front(), GREEN, [run(1, SHA_B, "feat/x")])
        self.assertFalse(row["TECHNICALLY_APPROVED"])
        self.assertIsNone(row["physical"]["seed42-30k-train"])
        self.assertIn("verified physical evidence missing at exact SHA: seed42-30k-train", row["blockers"])

    def test_9_other_branch_evidence_never_reused(self):
        self.assertIsNone(orch.physical_evidence([run(1, SHA_A, "other")], PROBE, SHA_A, "feat/x", "execute_30k"))
        self.assertIsNone(orch.physical_evidence([run(1, SHA_A, "feat/x", event="pull_request")], PROBE, SHA_A, "feat/x"))
        self.assertIsNone(orch.physical_evidence([run(1, SHA_A, "feat/x", mode="preflight")], PROBE, SHA_A, "feat/x", "execute_30k"))
        self.assertEqual(orch.physical_evidence([run(1, SHA_A, "feat/x"), run(2, SHA_A, "feat/x")],
                                                PROBE, SHA_A, "feat/x", "execute_30k")["id"], 2)

    def test_verified_pr_auto_30k_counts_only_with_same_sha_and_two_real_jobs(self):
        legitimate = {**run(55, SHA_A, "feat/x", event="pull_request", mode="auto_30k"),
                      "physical_job": "success", "stable_gate": "success",
                      "status": "completed", "same_repository_branch": True}
        row = orch.front_status(front(), GREEN, [legitimate])
        self.assertEqual(row["physical"]["seed42-30k-train"], 55)
        self.assertTrue(row["TECHNICALLY_APPROVED"])
        for diff in (
            {"physical_job": None}, {"stable_gate": None},
            {"mode": "preflight"}, {"status": "in_progress"},
            {"head_sha": SHA_B}, {"head_branch": "another"},
            {"conclusion": "failure"}, {"same_repository_branch": False},
            {"path": "other.yml"},
        ):
            with self.subTest(diff=diff):
                wrong = {**legitimate, **diff}
                self.assertIsNone(orch.physical_evidence(
                    [wrong], PROBE, SHA_A, "feat/x", "execute_30k"))
        self.assertIsNone(orch.physical_evidence([legitimate], "other.yml",
                                                 SHA_A, "feat/x"))

    def test_10_failures_are_not_success(self):
        failing = GREEN + [{"name": "e2e", "status": "completed", "conclusion": "failure"}]
        row = orch.front_status(front(), failing, [run(1, SHA_A, "feat/x")])
        self.assertEqual(row["ci"], "FAILED")
        self.assertFalse(row["TECHNICALLY_APPROVED"])
        self.assertFalse(row["merge_ready"])
        pending = orch.front_status(front(), GREEN + [{"name": "e2e", "status": "in_progress", "conclusion": None}],
                                    [run(1, SHA_A, "feat/x")])
        self.assertEqual(pending["ci"], "PENDING")

    def test_11_states_are_distinct_and_never_certify(self):
        row = orch.front_status(front(), GREEN, [run(1, SHA_A, "feat/x")])
        self.assertTrue(row["EXECUTED"] and row["TECHNICALLY_APPROVED"])
        self.assertEqual(row["STATISTICALLY_ELIGIBLE"], "NO_ELIGIBLE_CONFIGURATION")
        self.assertFalse(row["CERTIFIED"])
        self.assertFalse(row["OPERATIONALLY_AUTHORIZED"])
        blocked = orch.front_status(front(scientific_blockers=["Gold grid"]), GREEN, [run(1, SHA_A, "feat/x")])
        self.assertTrue(blocked["TECHNICALLY_APPROVED"])
        self.assertFalse(blocked["merge_ready"])
        self.assertIn("NO_ELIGIBLE_CONFIGURATION", orch.report([row]))

    def test_12_merging_one_front_does_not_silently_revalidate_or_invalidate_others(self):
        runs = [run(1, SHA_A, "feat/x"), run(2, "c" * 40, "master", path="v2v3.yml", mode=None)]
        x = orch.front_status(front(), GREEN, runs)
        master_front = front(id="A", pr=None, branch="master", sha="d" * 40,
                             physical=[{"id": "v2v3", "workflow": "v2v3.yml", "mode": None}], physical_check_names=[])
        a = orch.front_status(master_front, [], runs)
        self.assertTrue(x["TECHNICALLY_APPROVED"])  # unaffected by master moving
        self.assertIsNone(a["physical"]["v2v3"])     # master moved: old evidence explicitly missing
        self.assertIn("verified physical evidence missing at exact SHA: v2v3", a["blockers"])


class ReportHonestyTests(unittest.TestCase):
    def test_unimplemented_front_is_never_executed_or_approved(self):
        row = orch.front_status(front(id="B", pr=None, branch="master", physical=[], not_implemented=True,
                                      physical_check_names=[]), [], [])
        self.assertFalse(row["EXECUTED"])
        self.assertFalse(row["TECHNICALLY_APPROVED"])
        self.assertTrue(row["blockers"][0].startswith("NOT_IMPLEMENTED"))

    def test_front_without_pr_has_no_merge_state(self):
        row = orch.front_status(front(pr=None, branch="master", physical=[], physical_check_names=[]), [], [])
        self.assertIsNone(row["merge_ready"])
        self.assertIn("n/a (sem PR)", orch.report([row]))


class ArtifactAndReferenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp)
        self.repo = self.tmp / "repo"
        for rel in (orch.REFERENCES, orch.EXECUTOR, orch.V2V3_WORKFLOW):
            (self.repo / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(REPO / rel, self.repo / rel)

    def test_central_references_are_consistent(self):
        self.assertEqual(orch.audit_references(REPO)["status"], "REFERENCES_OK")

    def test_8_incomplete_sha_in_workflow_or_reference_is_rejected(self):
        wf = self.repo / orch.V2V3_WORKFLOW
        text = wf.read_text(encoding="utf-8")
        full = "8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc"
        wf.write_text(text.replace(full, full.replace("430b5a5b", "430b5b"), 1), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "length 62"):
            orch.audit_references(self.repo)
        wf.write_text(text, encoding="utf-8")
        ref_path = self.repo / orch.REFERENCES
        ref = json.loads(ref_path.read_bytes())
        ref["artifacts"]["v3-trial-model"]["files"][0]["sha256"] = full[:63]
        ref_path.write_text(json.dumps(ref), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "incomplete"):
            orch.audit_references(self.repo)

    def test_6_missing_artifact_file_blocks(self):
        root = self.tmp / "dl"
        root.mkdir()
        with self.assertRaisesRegex(ValueError, "missing"):
            orch.verify_artifact(REPO, "v3-trial-model", root)

    def test_7_zip_internal_path_is_validated_not_assumed(self):
        root = self.tmp / "meta"
        root.mkdir()
        receipt = json.loads((REPO / orch.REFERENCES).read_bytes())["artifacts"]["v3-trial-metadata"]["files"][0]["content"]
        (root / "derivation-receipt.json").write_text(json.dumps(receipt), encoding="utf-8")  # flattened: wrong
        with self.assertRaisesRegex(ValueError, "same name found at"):
            orch.verify_artifact(REPO, "v3-trial-metadata", root)
        nested = root / "v9-v3-provisional"
        nested.mkdir()
        (nested / "derivation-receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
        self.assertEqual(orch.verify_artifact(REPO, "v3-trial-metadata", root)["files"][0]["path"],
                         "v9-v3-provisional/derivation-receipt.json")

    def test_positive_executor_invocation_contract_uses_the_workflow_arguments(self):
        ref = json.loads((REPO / orch.REFERENCES).read_bytes())
        source, trial = self.tmp / "src", self.tmp / "trial"
        (source / "study-tf").mkdir(parents=True)
        trial.mkdir()
        (source / "plan-tf.json").write_text("{}", encoding="utf-8")
        (source / "study-tf" / (ref["models"]["V2_ONE_MF_original"] + ".fitted.json")).write_text("v2", encoding="utf-8")
        (trial / (ref["models"]["V3_trial"] + ".fitted.json")).write_text("v3", encoding="utf-8")
        (trial / "derivation-receipt.json").write_text("{}", encoding="utf-8")
        fake = {"v2": ref["models"]["V2_ONE_MF_original"], "v3": ref["models"]["V3_trial"]}
        with patch.object(orch, "sha256", side_effect=lambda p: fake.get(Path(p).read_text(), "0" * 64)):
            out = orch.invocation_contract(REPO, source, trial)
        argv = out["preflight_argv"]
        self.assertEqual(len(argv), 5)
        self.assertTrue(argv[0].endswith("data/reference/v9-tf-seed43-frozen-transfer/executor.json"))
        self.assertEqual(Path(argv[3]).name, ref["models"]["V3_trial"] + ".fitted.json")
        (trial / "derivation-receipt.json").unlink()
        with patch.object(orch, "sha256", side_effect=lambda p: fake.get(Path(p).read_text(), "0" * 64)):
            with self.assertRaisesRegex(ValueError, "missing or empty"):
                orch.invocation_contract(REPO, source, trial)

    def test_aggregate_only_drops_identity_rows(self):
        out = orch.aggregate_only({"a": 1, "IdentityOutcomes": [{"BaseIdentity": "x"}],
                                   "m": {"TP": 3, "Samples": [1, 2]}})
        self.assertEqual(out, {"a": 1, "m": {"TP": 3}})


if __name__ == "__main__":
    unittest.main()
