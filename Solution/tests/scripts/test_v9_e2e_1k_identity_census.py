import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts/v9-e2e-1k-identity-census.py"
SPEC = importlib.util.spec_from_file_location("census", SOURCE)
CENSUS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CENSUS)
U1 = "00000000-0000-4000-8000-000000000001"
U2 = "00000000-0000-4000-8000-000000000002"
RUN = "00000000-0000-4000-8000-000000000003"


def fixture():
    def truth(manager, package, source, base):
        return dict(Status="MATERIALIZADA", ExclusionReason=None, Partition="TRAIN",
                    GestorCodigo=manager, PackageFileName=package,
                    OpaquePersonId="SYNTH-" + source * 32, BasePersonId=base)
    def seal(rows):
        raw = ("\n".join(json.dumps(r) for r in rows) + "\n").encode()
        return dict(truthSidecar=dict(path="bridge-truth.jsonl",
                   containsSyntheticGroundTruth=True, allowedForScoring=False,
                   sha256=hashlib.sha256(raw).hexdigest().upper())), raw
    a = truth("SEHAB", "one.zip", "A", "BASE1")
    b = truth("SMADS", "two.zip", "B", "BASE1")
    c = truth("SMS", "three.zip", "C", "BASE2")
    d = truth("SEHAB", "four.zip", "A", "BASE1")
    def observed(t, origin, cpf, current, result=None):
        return dict(manager=t["GestorCodigo"], package=t["PackageFileName"],
                    opaque=t["OpaquePersonId"], originId=origin, hasCpf=cpf,
                    currentUuid=current, currentStatus="RESOLVIDO" if current else "NAO_RESOLVIDO",
                    goldState="REFERENCIA" if current else "INDEFINIDA",
                    currentMethod="CPF_DETERMINISTICO" if cpf else "LINKAGE_PROBABILISTICO",
                    runResult=result, resultRunId=None if cpf else RUN,
                    publishedUuid=None if cpf else current,
                    publicationReason=None if cpf else ("LINKAGE_INDEFINIDO" if current is None
                                                        else "ASSOCIACAO_EXISTENTE_LINKAGE"))
    waves = [seal([a, b, c]), seal([d])]
    rows = [observed(a, 1, True, U1), observed(b, 2, False, U1, "ASSOCIACAO_EXISTENTE"),
            observed(c, 3, False, None, "INDEFINIDA"), observed(d, 1, True, U1)]
    receipt = dict(mode="FULL", status="PUBLICADO", winnerSha256=CENSUS.WINNER,
                   scientificTest=False, activated=False, eligible=2, evaluated=2, runId=RUN)
    return waves, rows, receipt


class IdentityCensusTests(unittest.TestCase):
    def test_census_query_bytes_are_pinned_for_private_handoff(self):
        query = SOURCE.with_name("v9-e2e-1k-identity-census.sql")
        self.assertEqual(hashlib.sha256(query.read_bytes()).hexdigest(), "0b9a080ed9908384341d988e953bb03f4905f68152acaa09a97d2846e0d47ece")

    def test_cpf_versions_and_runner_universe(self):
        x = CENSUS.compare(*fixture(), required_identities=2)
        self.assertEqual(x["status"], "PASS")
        self.assertEqual((x["sourceVersions"], x["persistentOrigins"], x["runnerEligible"]), (4, 3, 2))
        self.assertEqual(x["cpfMultiversionOrigins"], 1)
        self.assertEqual(x["nonCpfCrossOriginAssociationsToCpf"], 1)
        self.assertEqual(x["pairMetrics"]["tp"], 3)
        self.assertFalse(x["candidateEdgeScoreOracleVerified"])
        self.assertNotIn("SYNTH-", str(x))
        self.assertNotIn("BASE1", str(x))

    def test_full_must_not_include_cpf(self):
        w, s, r = fixture()
        r.update(eligible=4, evaluated=4)
        with self.assertRaisesRegex(ValueError, "ELIGIBLE_CENSUS"):
            CENSUS.compare(w, s, r, 2)
        w, s, r = fixture()
        s[0]["resultRunId"] = RUN
        with self.assertRaisesRegex(ValueError, "CPF_MUST_NOT_ENTER_FULL"):
            CENSUS.compare(w, s, r, 2)

    def test_same_origin_versions_must_remain_same_origin(self):
        w, s, r = fixture()
        s[3]["originId"] = 4
        with self.assertRaisesRegex(ValueError, "ORIGIN_IDENTITY_DRIFT"):
            CENSUS.compare(w, s, r, 2)

    def test_missing_and_duplicate_version(self):
        w, s, r = fixture()
        with self.assertRaisesRegex(ValueError, "CENSUS_COUNT"):
            CENSUS.compare(w, s[:-1], r, 2)
        with self.assertRaisesRegex(ValueError, "VERSION_CENSUS"):
            CENSUS.compare(w, s[:-1] + [s[0]], r, 2)

    def test_false_fusion(self):
        w, s, r = fixture()
        s[2].update(currentUuid=U1, currentStatus="RESOLVIDO", goldState="REFERENCIA",
                    publishedUuid=U1, runResult="ASSOCIACAO_EXISTENTE",
                    publicationReason="ASSOCIACAO_EXISTENTE_LINKAGE")
        x = CENSUS.compare(w, s, r, 2)
        self.assertEqual(x["status"], "FAIL")
        self.assertEqual(x["falseFusedGoldIdentities"], 1)
        self.assertEqual(x["pairMetrics"]["fp"], 3)

    def test_truth_tamper_and_published_uuid_divergence(self):
        w, s, r = fixture()
        with self.assertRaisesRegex(ValueError, "TRUTH_SEAL"):
            CENSUS.compare([(w[0][0], w[0][1] + b" "), w[1]], s, r, 2)
        s[1]["publishedUuid"] = U2
        with self.assertRaisesRegex(ValueError, "PUBLISHED_CURRENT_UUID_DIVERGENCE"):
            CENSUS.compare(w, s, r, 2)

    def test_cross_origin_requires_exact_destination_in_other_origin(self):
        first, second = ("SEHAB", "A"), ("SMADS", "B")
        bases = {first: "BASE1", second: "BASE1"}
        self.assertFalse(CENSUS.has_cross_origin_cpf_reference(
            first, "BASE1", U1, bases, {first: {U1}, second: {U2}}))
        self.assertTrue(CENSUS.has_cross_origin_cpf_reference(
            first, "BASE1", U1, bases, {first: {U1}, second: {U1}}))

    def test_cpf_versions_of_same_origin_cannot_split(self):
        w, s, r = fixture()
        s[3]["currentUuid"] = U2
        with self.assertRaisesRegex(ValueError, "CPF_ORIGIN_UUID_DRIFT"):
            CENSUS.compare(w, s, r, 2)

    def test_no_cross_origin_match_is_not_pass(self):
        w, s, r = fixture()
        s[1].update(currentUuid=U2, publishedUuid=U2)
        x = CENSUS.compare(w, s, r, 2)
        self.assertEqual(x["status"], "FAIL")
        self.assertEqual(x["splitBaseIdentities"], 1)
        self.assertEqual(x["pairMetrics"]["fn"], 2)


if __name__ == "__main__":
    unittest.main()
