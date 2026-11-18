import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class V9OperationalSourceSnapshotPreflightTests(unittest.TestCase):
    def test_real_operational_tables_read_only_and_snapshot_pinned(self):
        source = (ROOT / "V9OperationalSourceSnapshotPreflight.cs").read_text(encoding="utf-8")
        for required in (
            "gold.pessoa", "identidade.blocking_chave",
            "estado_identidade = N'REFERENCIA'", "vigencia_fim IS NULL",
            "COUNT_BIG(*)", "IsolationLevel.Snapshot",
            "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "connection, snapshotTransaction", "RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED",
        ):
            self.assertIn(required, source)
        for forbidden in ("JornadaHarness", "NOLOCK", "TOP (", "INSERT INTO",
                          "UPDATE ", "DELETE FROM", "IBGE", "PublishAsync("):
            self.assertNotIn(forbidden, source)

    def test_coverage_is_partitioned_without_sampling(self):
        source = (ROOT / "V9OperationalSourceSnapshotPreflight.cs").read_text(encoding="utf-8")
        for required in (
            "GoldReferencesWithActiveBlockingKeys",
            "GoldReferencesWithoutActiveBlockingKeys",
            "AND EXISTS (SELECT 1 FROM identidade.blocking_chave",
            "AND NOT EXISTS (SELECT 1 FROM identidade.blocking_chave",
            "b.pessoa_uuid = g.pessoa_uuid",
            "covered != gold - uncovered",
            "new Receipt(gold, blocking, covered, uncovered)",
        ):
            self.assertIn(required, source)

    def test_not_productive_certification(self):
        source = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", source)


if __name__ == "__main__":
    unittest.main()
