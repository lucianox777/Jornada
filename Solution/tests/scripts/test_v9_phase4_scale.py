import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import os
spec=importlib.util.spec_from_file_location("scale",Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py")
S=importlib.util.module_from_spec(spec);spec.loader.exec_module(S)
class ScaleTests(unittest.TestCase):
    def test_v8_operational_fixture_is_fixed_inactive_and_fingerprint_exact(self):
        import hashlib
        import re
        from decimal import Decimal
        path=(Path(__file__).resolve().parents[2]/
              "database/tests/20261126_V8_Operational_Regression_Fixture.sql")
        sql=path.read_text(encoding="utf-8")
        self.assertIn("@model,@version,N'RASCUNHO'",sql)
        self.assertIn("SET status=N'INATIVO'",sql)
        self.assertNotIn("SET status=N'VALIDADO'",sql)
        self.assertNotIn("SET status=N'ATIVO'",sql)
        parameter_block=sql.split("INSERT identidade.parametro_linkage",1)[1].split(
            "INSERT identidade.linkage_ruleset",1)[0]
        pairs=re.findall(r"\(@model,N'([A-Z0-9_]+)',([0-9.]+)\)",parameter_block)
        self.assertEqual(len(pairs),55)
        self.assertEqual(len({name for name,_ in pairs}),55)
        def fixture_dotnet_decimal_g29(raw):
            """Mirror .NET G29 for the positive, <=29-digit literals in this fixture."""
            value=Decimal(raw).normalize()
            if value.is_zero():
                return "0"
            exponent=value.adjusted()
            if -4 <= exponent < 29:
                return format(value,"f")
            significand=format(value.scaleb(-exponent).normalize(),"f")
            return f"{significand}E{exponent:+03d}"
        self.assertEqual(
            fixture_dotnet_decimal_g29(dict(pairs)["PRIOR_BLOCK_MIN"]),"1E-06")
        canonical=("V8_OPERATIONAL_REGRESSION_RULESET_V1\n"
            "FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8\n-\n-\n"
            "PASS\tfull-name\nF\tname_full\n"+
            "".join(f"P\t{name}\t{fixture_dotnet_decimal_g29(value)}\n"
                    for name,value in sorted(pairs)))
        fingerprint=hashlib.sha256(canonical.encode()).hexdigest()
        self.assertEqual(fingerprint,"017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd")
        self.assertIn("'"+fingerprint+"'",sql)

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

    def test_v8_runner_failure_classifier_extracts_only_allowlisted_code(self):
        secret="PRIVATE_PERSON_AND_DATABASE_DETAIL"
        code=S.v8_runner_failure_code([
            "Unhandled exception. System.InvalidOperationException: ",
            "RUNNER_MODEL_CONFIGURATION_MISMATCH: "+secret+"\n"])
        self.assertEqual(code,"RUNNER_MODEL_CONFIGURATION_MISMATCH")
        self.assertNotIn(secret,code)
        self.assertEqual(S.v8_runner_failure_code([
            "Unhandled exception: RUNNER_PRIVATE_UNKNOWN: "+secret+"\n"]),"UNCLASSIFIED")
        self.assertEqual(S.v8_runner_failure_code([secret+"\n"]),"UNCLASSIFIED")
        for marker,expected in (
            ("V8 com TF exige TF_NOMINAL_FIRST_TOKEN_V1.","MODEL_V8_TF_CONTRACT_INVALID"),
            ("DUAL_THRESHOLD_CONFLICT_FLOOR_V2 sem DUAL_THRESHOLD_CONFLICT_FLOOR.",
             "MODEL_V8_FLOOR_CONTRACT_INVALID"),
            ("Ruleset possui passe inconsistente na ordem 3.","RULESET_PASS_INCONSISTENT"),
            ("Ruleset usa atributo dinâmico sem identidade de projeção física.",
             "RULESET_PROJECTION_IDENTITY_MISSING"),
            ("ConnectionStrings:Jornada não configurada.","CONNECTION_CONFIGURATION_MISSING"),
            ("Microsoft.Data.SqlClient.SqlException: "+secret,"SQL_CLIENT_FAILURE"),
        ):
            code=S.v8_runner_failure_code(["Unhandled exception: "+marker])
            self.assertEqual(code,expected)
            self.assertNotIn(secret,code)

    def test_v8_runner_failure_from_path_is_bounded_and_rejects_links(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            missing=root/"missing.log"
            self.assertEqual(S.v8_runner_failure_from_path(missing),"UNCLASSIFIED")
            private=root/"private.log"
            private.write_text(
                "Unhandled exception: RUNNER_MODEL_CONFIGURATION_MISSING: PRIVATE\n",
                encoding="utf-8")
            self.assertEqual(S.v8_runner_failure_from_path(private),
                             "RUNNER_MODEL_CONFIGURATION_MISSING")
            link=root/"linked.log";link.symlink_to(private)
            self.assertEqual(S.v8_runner_failure_from_path(link),"UNCLASSIFIED")
            private.write_bytes(b"X"*(4*1024*1024+1))
            self.assertEqual(S.v8_runner_failure_from_path(private),"UNCLASSIFIED")

    def test_v8_runner_failure_cli_needs_no_scale_environment_and_leaks_nothing(self):
        import subprocess
        import sys
        import tempfile
        script=Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py"
        secret="PRIVATE_PERSON_AND_DATABASE_DETAIL"
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/"v8-operational-runner.log").write_text(
                "Unhandled exception: RUNNER_MODEL_CONFIGURATION_MISMATCH: "+secret+"\n",
                encoding="utf-8")
            result=subprocess.run(
                [sys.executable,str(script),"v8-runner-failure","--root",str(root)],
                capture_output=True,text=True,env={"PYTHONDONTWRITEBYTECODE":"1"})
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(result.stdout,
                         "V8_OPERATIONAL_RUNNER_ERROR=RUNNER_MODEL_CONFIGURATION_MISMATCH\n")
        self.assertEqual(result.stderr,"")
        self.assertNotIn(secret,result.stdout+result.stderr)

    def test_v8_receipt_sql_failure_classifier_is_allowlisted_and_bounded(self):
        expected={
            52974:"SCHEMA_STAGE_INVALID",52975:"RUN_CARDINALITY",
            52976:"RUN_NOT_PUBLISHED",52977:"MODEL_NOT_FROZEN",
            52978:"FIXTURE_LIFECYCLE_FORBIDDEN",52979:"UNIVERSE_INVALID",
            52980:"RECEIPT_SIZE_INVALID",
        }
        secret="PRIVATE_PERSON_AND_DATABASE_DETAIL"
        for number,code in expected.items():
            actual=S.v8_receipt_sql_failure_code([
                f"Msg {number}, Level 16, State 1, Server {secret}, Line 42\n"])
            self.assertEqual(actual,code)
            self.assertNotIn(secret,actual)
        self.assertEqual(S.v8_receipt_sql_failure_code([
            f"Msg 52973, Level 16, State 1, Server {secret}\n"]),"UNCLASSIFIED")
        self.assertEqual(S.v8_receipt_sql_failure_code([secret+"\n"]),"UNCLASSIFIED")

        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            missing=root/"missing.log"
            self.assertEqual(S.v8_receipt_sql_failure_from_path(missing),"UNCLASSIFIED")
            private=root/"private.log"
            private.write_text(
                f"Msg 52973, Level 16, State 1, Server {secret}\n",encoding="utf-8")
            self.assertEqual(S.v8_receipt_sql_failure_from_path(private),"UNCLASSIFIED")
            private.write_text(
                f"Msg 52979, Level 16, State 1, Server {secret}\n",encoding="utf-8")
            self.assertEqual(S.v8_receipt_sql_failure_from_path(private),"UNIVERSE_INVALID")
            link=root/"linked.log";link.symlink_to(private)
            self.assertEqual(S.v8_receipt_sql_failure_from_path(link),"UNCLASSIFIED")
            private.write_bytes(b"X"*(1024*1024+1))
            self.assertEqual(S.v8_receipt_sql_failure_from_path(private),"UNCLASSIFIED")

    def test_v8_receipt_sql_failure_cli_leaks_no_private_text(self):
        import subprocess
        import sys
        import tempfile
        script=Path(__file__).resolve().parents[2]/"scripts/v9-phase4-scale.py"
        secret="PRIVATE_PERSON_AND_DATABASE_DETAIL"
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/"v8-operational-receipt.log").write_text(
                f"Msg 52978, Level 16, State 1, Server {secret}, Line 42\n",
                encoding="utf-8")
            result=subprocess.run(
                [sys.executable,str(script),"v8-receipt-sql-failure","--root",str(root)],
                capture_output=True,text=True,env={"PYTHONDONTWRITEBYTECODE":"1"})
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(result.stdout,
                         "V8_OPERATIONAL_RECEIPT_SQL_ERROR=FIXTURE_LIFECYCLE_FORBIDDEN\n")
        self.assertEqual(result.stderr,"")
        self.assertNotIn(secret,result.stdout+result.stderr)

    def test_v8_runner_failure_shell_never_greps_private_log(self):
        shell=(Path(__file__).resolve().parents[2]/
               "scripts/v9-phase4-scale-execute.sh").read_text(encoding="utf-8")
        failure=shell.split('>"$runner_log" 2>&1 || {',1)[1].split("}",1)[0]
        self.assertIn("v8-runner-failure --root",failure)
        self.assertNotIn("grep",failure)
        self.assertIn("V8_OPERATIONAL_RUNNER_ERROR=UNCLASSIFIED",failure)

    def test_v8_receipt_sqlcmd_preserves_json_without_incompatible_header_suppression(self):
        shell=(Path(__file__).resolve().parents[2]/
               "scripts/v9-phase4-scale-execute.sh").read_text(encoding="utf-8")
        invocation=shell.split('receipt_log="$root/v8-operational-receipt.log"',1)[1]
        invocation=invocation.split('>"$receipt_log" 2>&1',1)[0]
        self.assertIn("-w 65535 -y 0",invocation)
        self.assertNotIn("-h -1",invocation)

    def test_v8_operational_substages_fail_closed_with_fixed_codes(self):
        import json
        import re
        import subprocess
        import tempfile
        shell_path=(Path(__file__).resolve().parents[2]/
                    "scripts/v9-phase4-scale-execute.sh")
        shell=shell_path.read_text(encoding="utf-8")
        match=re.search(
            r"(?ms)^run_v8_operational_regression\(\) \{.*?^\}\n\nfull_sql_probe\(\)",
            shell)
        self.assertIsNotNone(match)
        function=match.group(0).rsplit("\n\nfull_sql_probe()",1)[0]
        harness=r'''
set -euo pipefail
root="$V8_TEST_ROOT"
sql_name=synthetic-sql
db=synthetic-db
pair_file() { :; }
docker() {
  if [[ " $* " == *" -Q "* ]]; then
    printf '%s\n' 'PRIVATE_PERSON_AND_DATABASE_DETAIL' >&2
    if [[ "$V8_TEST_STAGE" == query ]]; then
      printf '%s\n' 'PRIVATE_PERSON_AND_DATABASE_DETAIL'
      return 7
    fi
    if [[ "$V8_TEST_STAGE" == invalid-id ]]; then
      printf '%s\n' 'PRIVATE_PERSON_AND_DATABASE_DETAIL'
      return 0
    fi
    printf '%s\n' 10
    return 0
  fi
  if [[ "$V8_TEST_STAGE" == receipt-sql ]]; then
    printf '%s\n' 'Msg 52975, Level 16, State 1, Server PRIVATE_PERSON_AND_DATABASE_DETAIL, Line 42'
    return 7
  fi
  if [[ "$V8_TEST_STAGE" == receipt-parse ]]; then
    printf '%s\n' 'PRIVATE_PERSON_AND_DATABASE_DETAIL'
    return 0
  fi
  if [[ "$V8_TEST_STAGE" == receipt-non-object ]]; then
    printf '%s\n' 'V8_OPERATIONAL_PRIVATE_JSON=[]'
    return 0
  fi
  if [[ "$V8_TEST_STAGE" == receipt-invalid-json ]]; then
    printf '%s\n' 'V8_OPERATIONAL_PRIVATE_JSON={'
    return 0
  fi
  printf '%s\n' 'sqlcmd column header' '--------------------'
  printf '%s\n' 'V8_OPERATIONAL_PRIVATE_JSON={"schema":"V8_OPERATIONAL_REGRESSION_PRIVATE_V1"}'
  printf '%s\n' '(1 rows affected)'
}
env() {
  if [[ "$V8_TEST_STAGE" == marker ]]; then
    printf '%s\n' 'PRIVATE_PERSON_AND_DATABASE_DETAIL'
  else
    printf '%s\n' 'RUNNER_MODEL_PINNED modelId=88000000-0000-4000-8000-000000008008 version=8008 contractSha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
  fi
}
'''
        expected={
            "query":"V8_OPERATIONAL_OBSERVATION_QUERY_FAILED",
            "invalid-id":"V8_OPERATIONAL_OBSERVATION_ID_INVALID",
            "marker":"V8_OPERATIONAL_PINNED_MARKER_INVALID",
            "receipt-sql":"V8_OPERATIONAL_RECEIPT_SQL_ERROR=RUN_CARDINALITY",
            "receipt-parse":"V8_OPERATIONAL_RECEIPT_PARSE_FAILED",
            "receipt-non-object":"V8_OPERATIONAL_RECEIPT_PARSE_FAILED",
            "receipt-invalid-json":"V8_OPERATIONAL_RECEIPT_PARSE_FAILED",
        }
        solution=Path(__file__).resolve().parents[2]
        secret="PRIVATE_PERSON_AND_DATABASE_DETAIL"
        with tempfile.TemporaryDirectory() as directory:
            for stage,code in expected.items():
                root=Path(directory)/stage;root.mkdir()
                result=subprocess.run(
                    ["bash","-c",harness+function+"\nrun_v8_operational_regression\n"],
                    cwd=solution,capture_output=True,text=True,
                    env={**os.environ,"V8_TEST_STAGE":stage,"V8_TEST_ROOT":str(root)})
                self.assertNotEqual(result.returncode,0,(stage,result.stdout,result.stderr))
                self.assertIn(code,result.stdout,stage)
                self.assertNotIn(secret,result.stdout+result.stderr,stage)
            root=Path(directory)/"success";root.mkdir()
            result=subprocess.run(
                ["bash","-c",harness+function+"\nrun_v8_operational_regression\n"],
                cwd=solution,capture_output=True,text=True,
                env={**os.environ,"V8_TEST_STAGE":"success","V8_TEST_ROOT":str(root)})
            self.assertEqual(result.returncode,0,(result.stdout,result.stderr))
            self.assertIn("V8_OPERATIONAL_REAL_RUNASYNC_PUBLICATION_CAPTURED",result.stdout)
            self.assertEqual(
                json.loads((root/"v8-operational-private.json").read_text()),
                {"schema":"V8_OPERATIONAL_REGRESSION_PRIVATE_V1"})
            self.assertNotIn(secret,result.stdout+result.stderr)

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

    def test_operational_v8_pair_is_strict_and_detects_semantic_mutation(self):
        from copy import deepcopy
        import uuid
        run_a,run_b,person,initial=(str(uuid.UUID(int=i)) for i in (1,2,3,4))
        def receipt(stage,run_id):
            return dict(
                schema="V8_OPERATIONAL_REGRESSION_PRIVATE_V1",schemaStage=stage,
                entrypoint="Jornada.Linkage.Runner.Program/ProbabilisticLinkageBatchRunner.RunAsync",
                publish=True,fixtureLifecycle="NO_TRAIN_NO_VALIDATION_NO_ACTIVATION",runId=run_id,
                model=dict(modelId="88000000-0000-4000-8000-000000008008",version=8008,
                    status="INATIVO",algorithmVersion="FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8",
                    ruleSetVersion="V8_OPERATIONAL_REGRESSION_RULESET_V1",
                    ruleSetFingerprintSha256="017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd",
                    projectionSchemaVersion="PERSON_RESOLUTION_PROJECTION_V2",
                    projectionFingerprintSha256="d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4"),
                run=dict(mode="ON_DEMAND",status="PUBLICADO",observationId=10,highWatermark=20,
                    eligible=1,evaluated=1,resolved=1,unresolved=0,conflicts=0,noCandidateInBlock=0,
                    batchSize=100,maxParallelism=1,requestedBy="CI_V8_OPERATIONAL_REGRESSION",
                    reason="governed-v8-operational-publication-regression"),
                decisions=[dict(observationId=10,resolvedUuid=person,bestUuid=person,bestScore="0.98000000",
                    secondUuid=None,secondScore=None,margin=None,status="RESOLVIDO",reason=None,
                    publicationResult="ASSOCIACAO_EXISTENTE",publishedUuid=person,
                    publicationStatus="RESOLVIDO",publicationReason="REFERENCIA_PROGRESSIVA_CONFIRMADA",
                    publishedOriginId=30,progressiveVersion=1,
                    publicationPolicy="LINKAGE_PROGRESSIVE_PUBLICATION_V1",
                    universeReference=f"LINKAGE_RUN:{run_id};HW:20;ELIGIVEIS:1")],
                gold=[dict(personUuid=person,cpf=None,cpfStatus="SEM_CPF",fullName="SYNTH PERSON",
                    birthDate="1980-01-02",motherName="SYNTH MOTHER",distinctSources=1,
                    concordanceState="BASELINE_FONTE_UNICA",identityState="REFERENCIA",
                    nucleusCompleteness="COMPLETO",normalizedPublicationName=None,
                    publicationNameMethodVersion=None,publicationNameNormalizationVersion=None)],
                currentLinks=[dict(observationId=10,personUuid=person,method="LINKAGE_PROBABILISTICO",
                    score="0.98000000",status="RESOLVIDO",reason="REFERENCIA_PROGRESSIVA_CONFIRMADA",
                    modelId="88000000-0000-4000-8000-000000008008",exactRun=True)],
                progressiveOrigin=dict(originId=30,initialUuid=initial,canonicalUuid=person,legacyUuid=None,
                    state="REFERENCIA",version=1,lastExternalTargetUuid=person))
        baseline,migrated=receipt("BASELINE",run_a),receipt("20261126_V3",run_b)
        proof=S.verify_v8_operational_pair(baseline,migrated,("BASELINE","20261126_V3"))
        self.assertTrue(proof["operationalV8RegressionVerified"])
        self.assertTrue(proof["operationalV8MutationDetected"])
        self.assertEqual(proof["operationalV8DecisionRows"],1)
        self.assertEqual(proof["operationalV8GoldRows"],1)
        shape_cases=(
            ("decisionCollection",lambda r:r.update(decisions={})),
            ("decisionRowCount",lambda r:r.update(decisions=[])),
            ("decisionFieldSet",lambda r:r["decisions"][0].pop("margin")))
        for expected,mutate in shape_cases:
            with self.subTest(v8DecisionShape=expected):
                changed=deepcopy(migrated);mutate(changed)
                with self.assertRaises(S.V8DecisionPredicateError) as caught:
                    S.canonical_v8_operational_receipt(changed)
                self.assertEqual(caught.exception.fields,(expected,))
                self.assertEqual(str(caught.exception),S.V8_DECISION_ERROR_CODE)
        field_cases=(
            ("observationId",lambda d:d.update(observationId=11)),
            ("status",lambda d:d.update(status="CHANGED")),
            ("reason",lambda d:d.update(reason="CHANGED")),
            ("resolvedUuid",lambda d:d.update(resolvedUuid="not-a-uuid",
                bestUuid="not-a-uuid",publishedUuid="not-a-uuid")),
            ("bestUuid",lambda d:d.update(bestUuid=initial)),
            ("bestScore",lambda d:d.update(bestScore="1.00000001")),
            ("secondUuid",lambda d:d.update(secondUuid=initial)),
            ("secondScore",lambda d:d.update(secondScore="0.10000000")),
            ("margin",lambda d:d.update(margin="0.10000000")),
            ("publicationResult",lambda d:d.update(publicationResult="CHANGED")),
            ("publicationStatus",lambda d:d.update(publicationStatus="CHANGED")),
            ("publishedUuid",lambda d:d.update(publishedUuid=initial)),
            ("publicationReason",lambda d:d.update(publicationReason="CHANGED")),
            ("publishedOriginId",lambda d:d.update(publishedOriginId=True)),
            ("progressiveVersion",lambda d:d.update(progressiveVersion=0)),
            ("publicationPolicy",lambda d:d.update(publicationPolicy="CHANGED")),
            ("universeReference",lambda d:d.update(universeReference="CHANGED")))
        for expected,mutate in field_cases:
            with self.subTest(v8DecisionField=expected):
                changed=deepcopy(migrated);mutate(changed["decisions"][0])
                with self.assertRaises(S.V8DecisionPredicateError) as caught:
                    S.canonical_v8_operational_receipt(changed)
                self.assertEqual(caught.exception.fields,(expected,))
        changed=deepcopy(migrated)
        changed["decisions"][0]["bestScore"]="not-decimal"
        with self.assertRaises(S.V8DecisionPredicateError) as caught:
            S.canonical_v8_operational_receipt(changed)
        self.assertEqual(caught.exception.fields,("bestScore",))
        self.assertEqual(str(caught.exception),S.V8_DECIMAL_ERROR_CODE)
        changed["decisions"][0]["status"]="CHANGED"
        with self.assertRaises(S.V8DecisionPredicateError) as caught:
            S.canonical_v8_operational_receipt(changed)
        self.assertEqual(caught.exception.fields,("status","bestScore"))
        self.assertEqual(str(caught.exception),S.V8_DECISION_ERROR_CODE)
        changed=deepcopy(migrated)
        changed["decisions"][0].update(
            status="CHANGED",publicationReason="CHANGED",universeReference="CHANGED")
        with self.assertRaises(S.V8DecisionPredicateError) as caught:
            S.canonical_v8_operational_receipt(changed)
        self.assertEqual(caught.exception.fields,
            ("status","publicationReason","universeReference"))
        with self.assertRaises(ValueError):
            S.V8DecisionPredicateError(("private-value",))
        for mutate in (
            lambda r:r["gold"][0].update(fullName="CHANGED"),
            lambda r:r["decisions"][0].update(publicationReason="CHANGED"),
            lambda r:r["currentLinks"][0].update(score="0.97000000"),
            lambda r:r["progressiveOrigin"].update(lastExternalTargetUuid=None),
        ):
            changed=deepcopy(migrated);mutate(changed)
            with self.assertRaises(ValueError):
                S.verify_v8_operational_pair(baseline,changed,("BASELINE","20261126_V3"))
        with self.assertRaises(ValueError):
            S.verify_v8_operational_pair(baseline,receipt("BASELINE",run_b),
                ("BASELINE","20261126_V3"))
        with self.assertRaises(ValueError):
            S.verify_v8_operational_pair(baseline,receipt("20261126_V3",run_a),
                ("BASELINE","20261126_V3"))
        with self.assertRaises(ValueError):
            S.verify_v8_operational_pair(baseline,migrated,("20261126_V3","BASELINE"))

    def test_operational_v8_decision_diagnostics_emit_all_fixed_fields_then_reject(self):
        from contextlib import redirect_stdout
        from io import StringIO
        import sys
        error=S.V8DecisionPredicateError(
            ("status","publishedUuid","publicationReason","universeReference"))
        output=StringIO()
        with (patch.object(S,"profile",return_value=("optimized1k",(1000,355,"clean"))),
              patch.object(S,"pair_receipt",side_effect=error),
              patch.object(sys,"argv",["v9-phase4-scale.py","pair","--root","/tmp"]),
              redirect_stdout(output)):
            with self.assertRaises(SystemExit) as rejected:
                S.main()
        self.assertEqual(str(rejected.exception),
            "V9_PHASE4_SCALE_REJECTED_"+S.V8_DECISION_ERROR_CODE)
        self.assertEqual(output.getvalue().splitlines(),[
            "V8_OPERATIONAL_DECISION_FIELD=status",
            "V8_OPERATIONAL_DECISION_FIELD=publishedUuid",
            "V8_OPERATIONAL_DECISION_FIELD=publicationReason",
            "V8_OPERATIONAL_DECISION_FIELD=universeReference"])

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
                        normalizedIndependentDecisionGraphSha256=digest,
                        independentDecisionFieldSha256=S.decision_field_digests([graph], [rank_row]),
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

    def test_independent_tie_sets_preserve_members_decisions_and_strict_pairs(self):
        from contextlib import redirect_stdout
        from io import StringIO

        parity = S.module("parity", "v9-publication-scale-parity.py")
        digest = "a" * 64
        graph = dict(key=1, group=[1], candidates=[2, 3, 4, 5], neighbors=[], minimum=None, reason="SYNTH")
        rank = dict(Id=1, Best=2, BestScore="0.8", Second=3, SecondScore="0.8", Margin="0",
                    ScoreableRecords=3, PeopleWithoutScoreableEvidence=1, CandidateCount=4,
                    HasPass=True, Status="CONFLITO", Resolved=None, Reason="SYNTH")

        def row(profile, labels, tied_targets, changes=None, tied_resolved=0):
            raw_rank = dict(rank, Best=labels[0], Second=labels[1], **(changes or {}))
            tie_set = dict(logOdds="2", destinations=tied_targets)
            canonical = dict(raw_rank, Best=tie_set, Second=tie_set)
            return dict(profile=profile, versions=1, eligible=1, winnerSha256=digest,
                        pairMetrics={}, normalizedPublicationSha256=digest,
                        normalizedDecisionGraphSha256=digest,
                        normalizedIndependentDecisionGraphSha256=digest,
                        decisionFieldSha256=S.decision_field_digests([graph], [raw_rank]),
                        independentDecisionFieldSha256=S.decision_field_digests([graph], [canonical]),
                        goldRankDiagnostics=dict(normalizedGoldRecordScoreSha256=digest,
                            normalizedGoldScoreTiersSha256=digest, rawUuidRankVerified=True,
                            counts=dict(rows=1, scoreRecords=3, tiedBestRows=1, tiedSecondRows=1,
                                        tiedBestOrSecondRows=1, tiedResolvedRows=tied_resolved)))

        baseline = row("diagnostic1k", [2, 3], [2, 3, 4])
        reordered = row("optimized1k", [3, 2], [2, 3, 4])
        with redirect_stdout(StringIO()):
            self.assertTrue(parity.compare([baseline, reordered], independent=True))
            self.assertFalse(parity.compare([baseline, reordered]))
            self.assertFalse(parity.compare([baseline,
                row("optimized1k", [3, 2], [2, 3, 5])], independent=True))
            for changes in (dict(Status="INDEFINIDO"), dict(Resolved=3),
                            dict(Reason="OUTRA_DECISAO"), dict(Margin="0.01")):
                self.assertFalse(parity.compare([baseline,
                    row("optimized1k", [3, 2], [2, 3, 4], changes)], independent=True))
            resolved_tie = row("optimized1k", [2, 3], [2, 3, 4], dict(Resolved=2), tied_resolved=1)
            with self.assertRaisesRegex(SystemExit, "V9_PARITY_TIED_BEST_RESOLVED_DETERMINISM"):
                parity.compare([resolved_tie, resolved_tie], independent=True)

    def test_progressive_diagnostic_admission(self):
        env=dict(GITHUB_ACTIONS="true",GITHUB_REPOSITORY="lucianox777/Jornada",
                 GITHUB_EVENT_NAME="pull_request",JORNADA_RUNTIME_MODE="DEV",
                 JORNADA_V9_FULL_DIAGNOSTIC="true")
        for label,n in (("diagnostic1k",1000),("diagnostic2k",2000),("diagnostic4k",4000),
                        ("optimized1k",1000),("optimized2k",2000),("optimized4k",4000)):
            with patch.dict(os.environ,dict(env,JORNADA_V9_PHASE4_SCALE=label,
                           JORNADA_E2E_HTTP_VOLUME_COUNT=str(n)),clear=True):
                self.assertEqual(S.profile()[1][0],n)
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
    def test_gold_tie_set_includes_third_destination_and_uses_exact_decimals(self):
        from decimal import Decimal

        candidates = tuple(f"00000000-0000-4000-8000-{i:012d}" for i in (2, 3, 4))
        keys = {i: ("person", "SMS", i) for i in (1, 2, 3, 4)}
        destinations = {uuid: [keys[i]] for i, uuid in enumerate(candidates, 2)}
        scores = [dict(Source=1, Record=i, Person=uuid, Posterior=Decimal(".8"), LogOdds=Decimal("2"))
                  for i, uuid in enumerate(candidates, 2)]
        rank = dict(Id=1, CandidateCount=3, ScoreableRecords=3, PeopleWithoutScoreableEvidence=0,
                    Best=candidates[0], Second=candidates[1], BestScore=Decimal(".8"),
                    SecondScore=Decimal(".8"), Margin=Decimal("0"), Resolved=None)
        graph = dict(observationId=1, candidates=list(candidates))

        def classify():
            labels = {}
            gold = S.gold_rank_diagnostics(scores, [rank], [graph], keys, destinations.__getitem__, 3, labels)
            return gold["counts"], labels[keys[1]]

        counts, labels = classify()
        self.assertEqual(counts["tiedBestOrSecondRows"], 1)
        self.assertEqual(labels["Best"], {"logOdds": "2", "destinations": sorted(destinations.values())})
        self.assertEqual(labels["Best"], labels["Second"])

        scores[2]["LogOdds"] = Decimal("2.0000000000000000000000000001")
        rank.update(Best=candidates[2], Second=candidates[0], Margin=Decimal(".0000000000000000000000000001"))
        counts, labels = classify()
        self.assertEqual(counts["tiedBestRows"], 0)
        self.assertEqual(counts["tiedSecondRows"], 1)
        self.assertEqual(labels["Best"], destinations[candidates[2]])
        self.assertEqual(labels["Second"]["destinations"], [destinations[u] for u in candidates[:2]])
