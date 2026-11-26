import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import os
spec=importlib.util.spec_from_file_location("scale",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py")
S=importlib.util.module_from_spec(spec);spec.loader.exec_module(S)
class ScaleTests(unittest.TestCase):
    def test_sql_workflow_stops_on_first_failed_command(self):
        # Execute the real workflow command groups with synthetic shell mocks.
        # A failed migration or negative test must never be hidden by a later
        # successful command (Bash disables errexit inside a group on ||).
        import re
        import subprocess
        workflow=(Path(__file__).resolve().parents[3]/".github/workflows/jornada-v9-phase4-scale.yml").read_text()
        for name,log in (("Fresh schema with C07 resident identity","schema.log"),
                         ("SQL regression on the completed disposable FULL; reverse, verify and reapply","publication-regression.log")):
            block=workflow.split("- name: "+name,1)[1].split("\n      - name:",1)[0]
            match=re.search(r"(?ms)^          ([{(])\n(.*?)^          ([})]) >[^\n]*"+re.escape(log),block)
            self.assertIsNotNone(match)
            body=match[1]+"\n"+match[2]+match[3]
            shell="set -euo pipefail\nJORNADA_V9_PHASE4_SCALE=optimized1k\ndb=mock\nJORNADA_V9_DEV_ATTEMPT=mock\n"
            shell+="sql() { :; }\nrun_sql() { echo CALL; return 7; }\nrun_fixture() { run_sql; }\n"
            result=subprocess.run(["bash","-c",shell+body+" || { echo FAILED; exit 1; }"],capture_output=True,text=True)
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(result.stdout.splitlines().count("CALL"),1)
            self.assertIn("FAILED",result.stdout)

    def test_regression_rechecks_all_six_negatives_at_each_physical_stage(self):
        import re
        import subprocess

        workflow = (Path(__file__).resolve().parents[3] /
                    ".github/workflows/jornada-v9-phase4-scale.yml").read_text()
        sql = workflow.split("- name: SQL regression on the completed disposable FULL", 1)[1]
        group = re.search(r"(?ms)^          \(\n(.*?)^          \) >", sql)[1]
        shell = """set -eu
run_sql() { printf 'MIGRATION %s\n' "$1"; }
run_fixture() { printf 'NEGATIVES %s\n' "$1"; }
"""
        restored = subprocess.check_output(["bash", "-c", shell + group], text=True,
            env={**os.environ, "JORNADA_V9_PHASE4_SCALE": "diagnostic1k"})
        applied = subprocess.check_output(["bash", "-c", shell + group], text=True,
            env={**os.environ, "JORNADA_V9_PHASE4_SCALE": "optimized1k"})
        def tested_schemas(output):
            return [line.split()[1] for line in output.splitlines() if line.startswith("NEGATIVES ")]
        self.assertEqual(tested_schemas(restored), [
            "20261126_V3", "20261126_V2", "20261125_V1", "BASELINE",
            "20261125_V1", "20261126_V2", "20261126_V3",
        ])
        self.assertEqual(tested_schemas(applied), ["BASELINE", "20261125_V1", "20261126_V2", "20261126_V3"])
        for source, after in (("V3", "20261126_V2"), ("V2", "20261125_V1"), ("V1", "BASELINE")):
            tail = restored.split("V9_SQL_REGRESSION_STAGE=ROLLBACK_" + source, 1)[1]
            self.assertEqual(tested_schemas(tail)[0], after)

    def test_scale_failure_diagnostics_are_fixed_and_stage_specific(self):
        for code in ("PROCESSOR_TIMEOUT","PROCESSOR_REJECTED","HTTP_STATUS_UNAVAILABLE",
                     "SQL_LINEAGE_QUERY_FAILED","SILVER_SOURCE_SET_DIVERGENCE","VOLUME_ADMISSION"):
            self.assertEqual(S.safe_failure_code("ingest",ValueError(code)),code)
        self.assertEqual(S.safe_failure_code("ingest",ValueError("PROCESSOR_TIMEOUT private")), "UNCLASSIFIED")
        self.assertEqual(S.safe_failure_code("ingest",ValueError("arbitrary private text")), "UNCLASSIFIED")
        self.assertEqual(S.safe_failure_code("diagnostic",ValueError("PROCESSOR_TIMEOUT")), "UNCLASSIFIED")
        self.assertEqual(S.safe_failure_code("diagnostic",ValueError("V9_E2E_ORACLE_X")), "V9_E2E_ORACLE_X")
    def test_sql_failure_summary_retains_numbers_not_messages(self):
        lines=["V9_SQL_REGRESSION_STAGE=BASELINE\n",
               "PUBLICATION_GUARDS_SIX_CODES_PASS\n",
               "V9_SQL_FIXTURE_STAGE=CURRENT_MULTISET\n",
               "Msg 8622, Level 16, State 1, Server private, Line 42\n",
               "arbitrary private message\n",
               "V9_SQL_FIXTURE_STAGE=PRIVATE_VALUE\n",
               "Msg 123, Level 16, State 1, Server private\n"]
        self.assertEqual(S.sql_failure_summary(lines),
                         ["V9_SQL_REGRESSION_STAGE=BASELINE",
                          "V9_SQL_FIXTURE_STAGE=CURRENT_MULTISET",
                          "V9_SQL_ERROR=8622_16_1","V9_SQL_ERROR=123_16_1"])
        self.assertEqual(S.sql_failure_summary(["private\n"]),["V9_SQL_ERROR=UNCLASSIFIED"])

    def test_paired_prefull_frame_preserves_physical_identity_and_evidence(self):
        from copy import deepcopy
        import uuid
        a,b=str(uuid.UUID(int=1)),str(uuid.UUID(int=2))
        x,y=str(uuid.UUID(int=3)),str(uuid.UUID(int=4))
        def row(i,person):
            return dict(Id=i,Origin=i,Version=1,Wave=1,Manager="SYNTH",
                        Observation={"NomeCompleto":"SYNTH"},HasCpf=True,Initial=person,
                        Current=person,Canonical=person,Status="RESOLVIDO",
                        Method="CPF_DETERMINISTICO",State="REFERENCIA",
                        AsOf="2026-11-22T00:00:00+00:00",
                        NameVerified=None,BirthVerified=None,MotherVerified=None)
        left=dict(Attempt=a,Rows=[row(1,x),row(2,y)],
                  InitialRefs=[dict(Person=x,Name="SYNTH",Birth=None,Mother=None),
                               dict(Person=y,Name="SYNTH",Birth=None,Mother=None)],
                  SnapshotMs=1,VersionStoreStartBytes=0,VersionStoreEndBytes=0,ProcessMemoryBytes=1)
        right=deepcopy(left);right["Attempt"]=b
        right["Rows"].reverse();right["InitialRefs"].reverse();right["SnapshotMs"]=2
        S.verify_paired_prefull_frames(left,right,a,b)
        for mutate in (
            lambda f:f["Rows"][0].update(Initial=x),
            lambda f:f["Rows"][0].update(AsOf="2026-11-23T00:00:00+00:00"),
            lambda f:f["Rows"][0]["Observation"].update(NomeCompleto="SYNTH_CHANGED"),
            lambda f:f["Rows"].append(deepcopy(f["Rows"][0])),
            lambda f:f["InitialRefs"][0].update(Person=x),
            lambda f:f["InitialRefs"][0].update(Name="SYNTH_CHANGED"),
            lambda f:f.update(Attempt=a),
            lambda f:f.update(UnexpectedField="SYNTH"),
        ):
            changed=deepcopy(right);mutate(changed)
            with self.assertRaises(ValueError):S.verify_paired_prefull_frames(left,changed,a,b)
        with self.assertRaises(ValueError):S.verify_paired_prefull_frames(left,left,a,a)

    def test_restored_pair_requires_same_physical_input_and_two_runs(self):
        import uuid
        attempt, first, second = (str(uuid.UUID(int=i)) for i in (1, 2, 3))
        evidence = {
            "Attempt": attempt,
            "Rows": [dict(Id=1, Origin=1, Version=1, Wave=1, Manager="SYNTH",
                          Observation={"NomeCompleto": "SYNTH"}, HasCpf=True,
                          Initial=first, Current=first, Canonical=first,
                          Status="RESOLVIDO", Method="CPF_DETERMINISTICO",
                          State="REFERENCIA", AsOf="2026-11-22T00:00:00+00:00",
                          NameVerified=None, BirthVerified=None, MotherVerified=None)],
            "InitialRefs": [dict(Person=first, Name="SYNTH", Birth=None, Mother=None)],
            "SnapshotMs": 1, "VersionStoreStartBytes": 0, "VersionStoreEndBytes": 0,
            "ProcessMemoryBytes": 1,
        }
        receipt = S.verify_restored_prefull_frames(
            evidence, dict(evidence, SnapshotMs=2), attempt, first, second)
        self.assertEqual(receipt, {"observations": 1, "references": 1})
        with self.assertRaises(ValueError):
            S.verify_restored_prefull_frames(evidence, evidence, attempt, first, first)
        with self.assertRaises(ValueError):
            S.verify_restored_prefull_frames(evidence, dict(evidence, Attempt=second),
                                             attempt, first, second)
        with self.assertRaises(ValueError):
            S.verify_restored_prefull_frames(evidence, dict(evidence, InitialRefs=[
                dict(Person=second, Name="SYNTH", Birth=None, Mother=None)]),
                attempt, first, second)

    def test_independent_rank_drift_blocks_even_when_both_restored_pairs_match(self):
        from contextlib import redirect_stdout
        from io import StringIO

        parity = S.module("parity", "v9-publication-scale-parity.py")
        digest = "a" * 64
        rank = dict(Id=1, Best=None, BestScore=0, Second=None, SecondScore=None, Margin=None,
                    ScoreableRecords=0, PeopleWithoutScoreableEvidence=0, CandidateCount=0,
                    HasPass=False, Status="INDEFINIDO", Resolved=None, Reason="SYNTH")
        graph = dict(key=1, group=[1], candidates=[], neighbors=[], minimum=None, reason="SYNTH")
        def row(profile, rank_row):
            return dict(profile=profile, versions=1, eligible=1, winnerSha256=digest,
                        pairMetrics={}, normalizedPublicationSha256=digest,
                        normalizedDecisionGraphSha256=digest,
                        decisionFieldSha256=S.decision_field_digests([graph], [rank_row]),
                        goldRankDiagnostics=dict(normalizedGoldRecordScoreSha256=digest,
                            normalizedGoldScoreTiersSha256=digest, rawUuidRankVerified=True,
                            counts=dict(rows=1, scoreRecords=0, tiedBestRows=0, tiedSecondRows=0,
                                        tiedBestOrSecondRows=0, tiedResolvedRows=0)))
        baseline, optimized = "diagnostic1k", "optimized1k"
        stable = [row(baseline, rank), row(optimized, rank)]
        same = [row(optimized, rank), row(baseline, rank)]
        with redirect_stdout(StringIO()):
            parity.check_comparisons([stable, same])
        changed_rank = dict(rank, Best=[7])
        changed = [row(optimized, changed_rank), row(baseline, changed_rank)]
        messages = StringIO()
        with redirect_stdout(messages), self.assertRaises(SystemExit):
            parity.check_comparisons([stable, changed])
        self.assertIn("V9_PARITY_DIFFERENT_GRAPH_COMPONENTS=rank.Best", messages.getvalue())
        self.assertNotIn("V9_PUBLICATION_SCALE_NORMALIZED_1K_PARITY_PASS", messages.getvalue())

    def test_progressive_diagnostic_admission(self):
        env=dict(GITHUB_ACTIONS="true",GITHUB_REPOSITORY="lucianox777/Jornada",
                 GITHUB_EVENT_NAME="pull_request",JORNADA_RUNTIME_MODE="DEV",
                 JORNADA_V9_FULL_DIAGNOSTIC="true")
        for label,n in (("diagnostic1k",1000),("diagnostic2k",2000),("diagnostic4k",4000),
                        ("optimized1k",1000),("optimized2k",2000),("optimized4k",4000)):
            with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=label,
                           JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                self.assertEqual(S.profile()[1][0],n)
                with self.assertRaises(ValueError):S.plan(Path("."))
        for label,n in (("comparable",10000),("challenge",10000),("diagnostic4k",2000),("optimized4k",10000),("optimized2k",4000)):
            with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=label,
                           JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                with self.assertRaises(ValueError):S.profile()
    def test_http_ingest_admits_exact_optimized_volumes_and_preserves_negative_scope(self):
        env=dict(GITHUB_ACTIONS="true",GITHUB_REPOSITORY="lucianox777/Jornada",
                 GITHUB_EVENT_NAME="pull_request",JORNADA_RUNTIME_MODE="DEV",
                 JORNADA_V9_FULL_DIAGNOSTIC="true")
        # Stop at the first fixture read: no HTTP, SQL or real data access.
        for n in (2000,4000):
            for prefix in ("diagnostic","optimized"):
                with self.subTest(population=n,prefix=prefix):
                    with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=f"{prefix}{n//1000}k",
                                     JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                        with patch.object(S.H,"get_json",side_effect=RuntimeError("fixture read reached")) as read:
                            with self.assertRaisesRegex(RuntimeError,"fixture read reached"):
                                S.H.ingest(Path("."),None,population=n)
                            read.assert_called_once()
        valid=dict(env,JORNADA_V9_PHASE4_SCALE="optimized2k",JORNADA_E2E_HTTP_VOLUME_COUNT="2000")
        for changes,n in (({"GITHUB_ACTIONS":"false"},2000),({"GITHUB_REPOSITORY":"other/repo"},2000),
                          ({"GITHUB_EVENT_NAME":"push"},2000),({"JORNADA_V9_FULL_DIAGNOSTIC":"false"},2000),
                          ({"JORNADA_RUNTIME_MODE":"PROD"},2000),({"JORNADA_RUNTIME_MODE":"HML"},2000),
                          ({"JORNADA_V9_PHASE4_SCALE":"optimized4k"},2000),
                          ({"JORNADA_E2E_HTTP_VOLUME_COUNT":"4000"},2000),
                          ({"JORNADA_V9_PHASE4_SCALE":"comparable","JORNADA_E2E_HTTP_VOLUME_COUNT":"10000"},10000),
                          ({"JORNADA_V9_PHASE4_SCALE":"challenge","JORNADA_E2E_HTTP_VOLUME_COUNT":"10000"},10000)):
            with self.subTest(changes=changes,population=n):
                with patch.dict(os.environ,dict(valid,**changes),clear=True):
                    with patch.object(S.H,"get_json") as read:
                        with self.assertRaisesRegex(ValueError,"^VOLUME_ADMISSION$"):
                            S.H.ingest(Path("."),None,population=n)
                        read.assert_not_called()
    def test_decision_component_digests_detect_exact_field_changes(self):
        key=("base-a","SMS",1)
        graph=dict(key=key,group=[key],candidates=[],neighbors=[],minimum=None,reason="X")
        rank=dict(Id=key,Best=None,BestScore=0,Second=None,SecondScore=None,Margin=None,
                  ScoreableRecords=0,PeopleWithoutScoreableEvidence=0,CandidateCount=0,HasPass=False,
                  Status="NAO_RESOLVIDO",Resolved=None,Reason="X")
        a=S.decision_field_digests([graph],[rank])
        b=S.decision_field_digests([dict(graph,reason="Y")],[dict(rank,BestScore=1)])
        self.assertEqual({k for k in a if a[k]!=b[k]},{"graph.reason","rank.BestScore"})
        self.assertEqual(len(a),17)
        key2=("base-b","SMS",1)
        g2=dict(graph,key=key2,group=[key2])
        r2=dict(rank,Id=key2)
        self.assertEqual(S.decision_field_digests([graph,g2],[rank,r2]),
                         S.decision_field_digests([g2,graph],[r2,rank]))
        with self.assertRaises(ValueError):S.decision_field_digests([dict(graph,unexpected=1)],[rank])
    def test_gold_evidence_normalization_detects_uuid_tie_without_relaxing_rank(self):
        ids={1:("source","SMS",1),2:("a","SMS",1),3:("b","SMS",1),4:("c","SMS",1)}
        x="11111111-1111-4111-8111-111111111111"
        y="22222222-2222-4222-8222-222222222222"
        z="33333333-3333-4333-8333-333333333333"
        def frame(a,b,second_only=False):
            target=lambda u:{a:[ids[2]],b:[ids[3]],z:[ids[4]]}[u]
            rows=[dict(Source=1,Record=2,Person=a,Posterior=.8,LogOdds=2),
                  dict(Source=1,Record=3,Person=b,Posterior=.8,LogOdds=2)]
            if second_only:
                rows.append(dict(Source=1,Record=4,Person=z,Posterior=.9,LogOdds=3))
            rank=dict(Id=1,Best=z if second_only else min(a,b),BestScore=.9 if second_only else .8,
                Second=min(a,b) if second_only else max(a,b),SecondScore=.8,Margin=1 if second_only else 0,
                ScoreableRecords=len(rows),PeopleWithoutScoreableEvidence=0,CandidateCount=len(rows),
                HasPass=True,Status="CONFLITO",Resolved=None,Reason="X")
            graph=dict(observationId=1,candidates=[a,b]+([z] if second_only else []))
            return rows,rank,graph,target
        def verify(f):
            rows,rank,graph,target=f
            return S.gold_rank_diagnostics(rows,[rank],[graph],ids,target,len(rows))
        for second_only in (False,True):
            left=frame(x,y,second_only);right=frame(y,x,second_only)
            l=verify(left);r=verify(right)
            self.assertEqual(l,r)
            self.assertEqual(l["counts"]["tiedBestRows"],0 if second_only else 1)
            self.assertEqual(l["counts"]["tiedSecondRows"],1)
            self.assertNotEqual(left[3](left[1]["Second"]),right[3](right[1]["Second"]))
            invalid=frame(x,y,second_only);invalid[1]["Second"]=z if not second_only else y
            with self.assertRaises(ValueError):verify(invalid)
        changed=frame(x,y);changed[0][0]["Posterior"]=.81;changed[1]["BestScore"]=.81
        self.assertNotEqual(verify(frame(x,y))["normalizedGoldRecordScoreSha256"],
                            verify(changed)["normalizedGoldRecordScoreSha256"])
        with self.assertRaises(ValueError):
            S.gold_rank_diagnostics(left[0]+[left[0][0]],[left[1]],[left[2]],ids,left[3],len(left[0])+1)
    def test_conditional_bound_and_cap(self):
        b=S.bound(10000,0,20000,0,1)
        self.assertAlmostEqual(b["cpUpper"],1-.025**(1/10000),places=12)
        self.assertGreater(S.bound(10000,1,20000,1,216)["conditionalFdrUpper"],b["conditionalFdrUpper"])
        self.assertIsNone(S.bound(10000,0,1,0,216)["conditionalFdrUpper"])
        self.assertFalse(b["certified"])
    def test_ordinal_cases_and_integrity(self):
        truth={1:dict(base="A",origin=1,manager="SEHAB",wave=1,cpf=False),
               2:dict(base="A",origin=2,manager="SMADS",wave=2,cpf=False),
               3:dict(base="B",origin=3,manager="SMS",wave=2,cpf=False)}
        def row(k,p):
            return dict(Id=k,Person=p,RawReason="X",RawStatus="RESOLVIDO",EffectiveReason="X",Method="FS_ASSOCIATION",
                        Score=.999,AssociationScore=.999,Fs=1,Candidates=1,SecureCandidates=1,PriorCardinality=1,
                        OnlyViaNonsecure=0,Best=p,LeaderId=k,GroupSize=1,GroupMinPosterior=None,SecureOrigin=False,CandidateIds=[p])
        _,state=S.M.assess([row(1,"x"),row(2,"y"),row(3,"y")],truth)
        q=S.quality(state,truth,216);self.assertEqual(q["fullyIntactTruePeople"],0)
        self.assertEqual(q["sourceFdr95Conditional"]["falseAcceptedSources"],2)
        self.assertEqual(q["acceptedPairs"],1)
        self.assertEqual(q["acceptedSources"],2)
        self.assertEqual(q["nonAnchorAcceptedSources"],1)
        self.assertEqual(q["nonAnchorFalseAcceptedSources"],1)
        self.assertEqual(q["nonAnchorErrorSourcePeople"],1)
        self.assertEqual(q["eligibleAnchorsRemoved"],1)
        self.assertEqual(q["anchorFallbackDestinations"],1)
        # Proven founder takes precedence over earlier ID, and CPF anchor
        # is already excluded from the original denominator.
        state["byid"][3]["Method"]="S4B_HYPOTHETICAL_FOUNDER"
        q2=S.quality(state,truth,216)
        self.assertEqual(q2["anchorFallbackDestinations"],0)
        self.assertEqual(q2["nonAnchorAcceptedSources"],1)
        truth[3]["cpf"]=True
        q3=S.quality(state,truth,216)
        self.assertEqual(q3["eligibleAnchorsRemoved"],0)
        self.assertEqual(q3["acceptedSources"],q3["nonAnchorAcceptedSources"])
        truth[3]["cpf"]=False
        state["byid"][3]["Method"]="FS_ASSOCIATION"
        self.assertEqual(q["errorSourcePeople"],2)
        self.assertEqual(q["contaminatedDestinations"],1)
        self.assertEqual(q["versionsPerPersonHistogram"],{1:1,2:1})
        self.assertEqual(q["sourceCapSensitivity"][-1]["cap"],8)
        self.assertTrue(q["sourceCapSensitivity"][0]["diagnosticOnly"])
        a=S.audit_cases(state,truth,None,{"obs":{1:1,2:2,3:3},"base":{"A":1,"B":2},"origin":{1:1,2:2,3:3}})
        self.assertEqual(len(a["splits"]),1);self.assertEqual(len(a["fusions"]),1)
        self.assertEqual(a["splits"][0]["person"],1)
        self.assertNotIn("y",a["fusions"][0]["members"][0]["candidateDestinations"])
