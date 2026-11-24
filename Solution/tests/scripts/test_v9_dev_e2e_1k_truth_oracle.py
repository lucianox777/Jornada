"""Unit negatives for stage7/8 1k external identity evidence."""
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("v9_dev_e2e_oracle",ROOT/"scripts/v9-e2e-dev-1k-truth-oracle.py")
assert SPEC and SPEC.loader
oracle=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(oracle)


def fixture():
    truth=[
        dict(Status="MATERIALIZADA",ExclusionReason=None,Partition="TEST",
             GestorCodigo="SEHAB",BasePersonId="BASE_A",OpaquePersonId="SYNTH-"+"A"*32),
        dict(Status="MATERIALIZADA",ExclusionReason=None,Partition="TEST",
             GestorCodigo="SMADS",BasePersonId="BASE_A",OpaquePersonId="SYNTH-"+"B"*32),
        dict(Status="MATERIALIZADA",ExclusionReason=None,Partition="TEST",
             GestorCodigo="SMS",BasePersonId="BASE_B",OpaquePersonId="SYNTH-"+"C"*32),
    ]
    raw=("\n".join(json.dumps(x,sort_keys=True) for x in truth)+"\n").encode()
    manifest={"truthSidecar":{"path":"bridge-truth.jsonl",
             "sha256":hashlib.sha256(raw).hexdigest().upper(),
             "containsSyntheticGroundTruth":True,"allowedForScoring":False}}
    runner={"schema":"V9_E2E_DEV_1K_FULL_R1_V1","status":"PASS",
            "scope":"HTTP_1K_PROCESSOR_TO_V9_FULL_DT10_DEV_ONLY",
            "mode":"FULL","publish":True,"scientificTest":False,
            "activation":False,"productionCertified":False,
            "eligible":3,"evaluated":3}
    first="00000000-0000-4000-8000-000000000001"
    sql="\n".join([
        f"SYNTH-{'A'*32}\t{first}\tASSOCIACAO_EXISTENTE",
        f"SYNTH-{'B'*32}\t{first}\tASSOCIACAO_EXISTENTE",
        f"SYNTH-{'C'*32}\t\tINDEFINIDA",
    ])
    return manifest,raw,sql,runner


class Stage7And8OracleTests(unittest.TestCase):
    def test_positive_identity_only_oracle(self):
        m,t,s,r=fixture()
        x=oracle.compare(m,t,s,r,required_identities=2)
        self.assertEqual(x["status"],"PASS")
        self.assertEqual(x["baseIdentities"],2)
        self.assertEqual(x["sourceObservations"],3)
        self.assertEqual(x["abstentions"],1)
        self.assertIs(x["scientificCertified"],False)

    def test_byte_seal_cannot_be_fixed_label(self):
        m,t,s,r=fixture()
        with self.assertRaisesRegex(ValueError,"byte SHA"):
            oracle.compare(m,t+b" ",s,r,required_identities=2)

    def test_missing_and_duplicate_sql_rows_fail(self):
        m,t,s,r=fixture()
        with self.assertRaisesRegex(ValueError,"census"):
            oracle.compare(m,t,"\n".join(s.splitlines()[:2]),r,required_identities=2)
        with self.assertRaisesRegex(ValueError,"duplicated"):
            oracle.compare(m,t,s+"\n"+s.splitlines()[0],r,required_identities=2)

    def test_false_fusion_detected_without_raw_ids_in_receipt(self):
        m,t,s,r=fixture()
        wrong=s.replace("\t\tINDEFINIDA",
                        "\t00000000-0000-4000-8000-000000000001\tASSOCIACAO_EXISTENTE")
        x=oracle.compare(m,t,wrong,r,required_identities=2)
        self.assertEqual(x["status"],"FAIL")
        self.assertEqual(x["falseFusedGoldIdentityCount"],1)
        self.assertNotIn("SYNTH-",str(x))
        self.assertNotIn("BASE_",str(x))

    def test_undefined_cannot_carry_uuid(self):
        m,t,s,r=fixture()
        with self.assertRaisesRegex(ValueError,"abstention"):
            oracle.compare(m,t,s.replace("\t\tINDEFINIDA",
                "\t00000000-0000-4000-8000-000000000001\tINDEFINIDA"),r,2)

    def test_opt_in_after_processor_and_productive_path_stays_blocked(self):
        local=(ROOT/"scripts/local-e2e.sh").read_text(encoding="utf-8")
        stage=(ROOT/"scripts/v9-e2e-dev-full-1k.sh").read_text(encoding="utf-8")
        program=(ROOT/"src/Jornada.Linkage.Runner/Program.cs").read_text(encoding="utf-8")
        self.assertLess(local.index('source "$ROOT/scripts/v9-e2e-ibge-http.sh"'),
                        local.index('source "$ROOT/scripts/v9-e2e-dev-full-1k.sh"'))
        self.assertIn('JORNADA_E2E_V9_DEV_FULL:-false',local)
        self.assertIn('JORNADA_E2E_HTTP_VOLUME_COUNT:-}',stage)
        self.assertIn('--v9-dev-e2e-1k-full',stage)
        self.assertIn('RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED',program)
        runner=(ROOT/"src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs").read_text(encoding="utf-8")
        self.assertIn('boundary.EnsureProductiveCertified()',runner)


if __name__=="__main__":
    unittest.main()
