"""Offline 100k capacity reproduction: exact plan/CSV hashes, NO SQL/fitting."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT.parent / ".github/workflows/v9-github-standard-runner-100k-resource-profile.yml"
PHYSICAL = ROOT.parent / ".github/workflows/v9-multiparameter-100k-technical.yml"
ENTRY = ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs"
CLI = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9OfflineCalibratorCapacityCli.cs"
PROBE = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlAdaptiveCalibrationProbe.cs"


class Offline100kPreSqlTests(unittest.TestCase):
    def test_only_auto_100k_is_generation_with_exact_offline_non_sql_capacity(self):
        text = WORKFLOW.read_text()
        self.assertIn("runs-on: ubuntu-24.04", text)
        self.assertIn("github.event_name == 'push'", text)
        self.assertIn("--people 100000 --seed 42", text)
        self.assertIn("--v9-offline-train-capacity", text)
        self.assertIn("--artifact-id 13654549483", text)
        self.assertIn("5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7", text)
        self.assertIn("offline-presql-capacity.json", text)
        self.assertIn("bound/binding-receipt.json", text)
        self.assertNotIn("--v9-calibrate", text)
        self.assertNotIn("v9-tf-start-disposable-sql.sh", text)
        self.assertNotIn("MSSQL_SA_PASSWORD", text)
        self.assertNotIn("connection-string", text.lower())
        self.assertNotRegex(text, r"(?m)^\\s+pull_request:\\s*$")

    def test_original_physical_100k_remains_manual_and_security_preflight(self):
        t = PHYSICAL.read_text()
        self.assertIn("workflow_dispatch:", t)
        self.assertIn("security_preflight_approved == true", t)
        self.assertIn("github.actor == github.repository_owner", t)
        self.assertNotRegex(t, r"(?m)^\\s+push:\\s*$")
        self.assertNotIn("--v9-offline-train-capacity", t)

    def test_csharp_reader_is_train_only_and_offline_receipt_cannot_certify(self):
        c = CLI.read_text()
        program = ENTRY.read_text()
        self.assertIn('"--v9-offline-train-capacity"', program)
        self.assertIn('onlyPartition: "TRAIN"', c)
        self.assertIn('V9SyntheticObservationCapEvidence.ValidateManifest', c)
        self.assertIn('V9InternalTrainingSplit.Create', c)
        self.assertIn('InspectOfflinePairCapacity', c)
        self.assertIn('"PRE_SQL_CAPACITY_ADMITTED_NOT_FITTED"', c)
        self.assertIn('"DECLARED_PAIR_INCIDENCE_CAP_EXCEEDED_NO_FIT"', c)
        self.assertIn('"DECLARED_SIGNATURE_CAP_EXCEEDED_NO_FIT"', c)
        for literal in ("physicalSqlExecuted = false", "testUsed = false",
                        "goldUsed = false", "activated = false",
                        "scientificPlanModified = false"):
            self.assertIn(literal, c)
        self.assertNotIn('new SqlConnection', c)
        self.assertNotIn('OpenAsync', c)
        self.assertNotIn('EvaluateFrozenHoldoutAsync', c)
        self.assertNotIn('exception.Message', c)
        probe = PROBE.read_text()
        self.assertIn('public static OfflinePairCapacity InspectOfflinePairCapacity(', probe)
        self.assertIn('BuildSignatures(observations, canonical,', probe)
        self.assertIn('CountPairIncidences(signatures, observations,', probe)


if __name__ == "__main__":
    unittest.main()
