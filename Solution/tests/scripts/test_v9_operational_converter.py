"""V9 winner -> SQL operational representation, no loss or premature TEST."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WF = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
CORE = ROOT / "src/Jornada.Linkage.Core/V9OperationalSqlModelConverter.cs"
SQL = ROOT / "database/migrations/20261113_V9_Operational_Frozen_Model_Conversion.sql"
WORKER = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9OperationalModelConversionCli.cs"
TEST = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs"
PROD = ROOT / "src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs"
PROGRAM = ROOT / "src/Jornada.Linkage.Parameters.Worker/Program.cs"


class OperationalConverterContract(unittest.TestCase):
    def test_lossless_full_v3_tf4_joint_margins_and_no_activation(self):
        core = CORE.read_text()
        sql = SQL.read_text()
        self.assertIn('public const string ContractVersion = "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1"',core)
        self.assertIn("p.JointCells.Count != 81",core)
        self.assertIn("p.UnitTfCalibrations.Count != 4",core)
        self.assertIn("p.UnitTfCalibrations.Select",core)
        self.assertIn('p.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"',core)
        self.assertIn("p.GoldParentSnapshotSha256 is not null",core)
        self.assertIn("V9FittedSnapshot.Load(bytes, winner.ContentSha256)",core)
        self.assertIn("SerializeUtf8()",core)
        self.assertIn("ByteExactRoundTrip: true, Active: false, Published: false",core)
        self.assertIn("INSERT INTO identidade.modelo_linkage_v9_operacional(",core)
        self.assertIn("payload_fitted VARBINARY(MAX) NOT NULL",sql)
        self.assertIn("joint_cell_count=81",sql)
        self.assertIn("tf_slot_count=4",sql)
        self.assertIn("status=N'TEST_ONLY'",sql)
        self.assertIn("AFTER UPDATE, DELETE",sql)
        self.assertIn("THROW 52417",sql)
        self.assertNotIn("status=N'ATIVO'",sql)
        self.assertNotIn("UPDATE identidade.",core)
        self.assertNotIn("DELETE FROM identidade.",core)

    def test_production_side_v9_loader_and_test_use_same_operational_contract(self):
        core=CORE.read_text()
        worker=WORKER.read_text()
        test=TEST.read_text()
        prod=PROD.read_text()
        program=PROGRAM.read_text()
        self.assertIn("V9OperationalSqlModelConverter.MaterializeOnceAsync(",worker)
        self.assertIn("V9FittedSnapshotArtifact.ReadPinnedAsync(modelPath, sha)",worker)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(",test)
        self.assertIn("V9OperationalSqlModelConverter.ReadPinnedAsync(",prod)
        self.assertIn("LoadPinnedV9OperationalRuntimeAsync(",prod)
        self.assertIn("LoadPinnedV9GroupedCohortRuntimeAsync(",prod)
        self.assertIn("V9OperationalPinnedCohortRuntime.LoadPinnedAsync(",prod)
        self.assertIn("V9FittedSnapshotExecutionRuntime.Load(model.SerializeUtf8(), model.ContentSha256)",prod)
        self.assertIn("operationalModelByteExactRoundTrip = conversion.ByteExactRoundTrip",test)
        self.assertIn("productionRunnerFullPipelineParityCertified = false",test)
        self.assertNotIn("V9FittedSnapshotArtifact.ReadPinnedAsync",test)
        self.assertNotIn("selectedValidationSnapshotSha256",test)
        self.assertNotIn("SelectScientificWinner",test)
        self.assertIn('--v9-convert-winner-operational',program)
        self.assertIn('--v9-independent-test',program)
        self.assertIn('builder.InitialCatalog != "JornadaHarness"',worker)

    def test_dedicated_sql_smoke_is_not_skipped_by_general_unit_suite(self):
        # The physical SQL fixture intentionally ignores the test without an
        # ephemeral private SQL connection. It cannot count as an executed
        # unit test under jornada-ci's --forbid-skipped evidence gate.
        ci = (ROOT.parent / ".github/workflows/ci.yml").read_text()
        smoke = (ROOT.parent / ".github/workflows/v9-operational-model-sql-smoke.yml").read_text()
        fixture = (ROOT / "tests/Jornada.Tests/Unit/V9FittedSnapshotTests.cs").read_text()
        self.assertIn('TestCategory!=Integration&TestCategory!=SQLSmoke', ci)
        self.assertIn('[Test, Category("SQLSmoke")]', fixture)
        self.assertIn('JORNADA_V9_SQL_MODEL_SMOKE_CONNECTION', fixture)
        self.assertIn('Operational_SQL_roundtrip_uses_immutable_one_winner_row_and_same_frozen_FS_decisions', fixture)
        self.assertIn('FullyQualifiedName~V9FittedSnapshotTests.Operational_SQL_roundtrip_', smoke)
        self.assertIn('JORNADA_V9_SQL_MODEL_SMOKE_CONNECTION=', smoke)
        self.assertIn('v9-operational-converter-smoke', smoke)
        self.assertIn('dotnet test tests/Jornada.Tests/Jornada.Tests.csproj --no-build -c Release', smoke)

    def test_sql_conversion_precedes_test_decryption_and_winner_only_handoff(self):
        wf=WF.read_text()
        test=wf.split("\n  test-100k:\n",1)[1].split("\n  e2e-closeout:\n",1)[0]
        validation=wf.split("\n  validation-100k:\n",1)[1].split("\n  test-100k:\n",1)[0]
        self.assertIn("test-winner-only/*.fitted.json",validation)
        self.assertIn("v9-tf4-test-one-winner-",test)
        self.assertIn("20261113_V9_Operational_Frozen_Model_Conversion.sql",test)
        self.assertIn("--v9-convert-winner-operational",test)
        self.assertIn("--v9-independent-test",test)
        self.assertLess(test.index("Load versioned V9 SQL operational schema"),
                        test.index("Convert the ONLY SHA-pinned"))
        self.assertLess(test.index("Convert the ONLY SHA-pinned"),
                        test.index("Reserve single-use TEST seed"))
        self.assertLess(test.index("Reserve single-use TEST seed"),
                        test.index("v9-tf4-sealed-test-preparation.py unseal"))
        self.assertNotIn("finalist-validation.json",test)
        self.assertNotIn("plan.json",test)
        self.assertNotIn("selection.json",test)
        self.assertIn("V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",wf)
        self.assertIn("productionRunnerFullPipelineParityCertified",test)


if __name__ == "__main__":
    unittest.main()
