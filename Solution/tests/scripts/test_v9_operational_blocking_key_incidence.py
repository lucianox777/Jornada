import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class V9OperationalBlockingKeyIncidenceTests(unittest.TestCase):
    def test_real_source_incidence_is_read_only_snapshot_scoped(self):
        source = (ROOT / "V9OperationalBlockingKeyIncidence.cs").read_text(encoding="utf-8")
        for required in (
            "identidade.blocking_chave", "gold.pessoa",
            "vigencia_fim IS NULL", "estado_identidade = N'REFERENCIA'",
            "COUNT_BIG(*)", "AND EXISTS", "AND NOT EXISTS",
            "IsolationLevel.Snapshot", "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "matched != total - unmatched", "connection, snapshotTransaction",
        ):
            self.assertIn(required, source)
        for forbidden in ("JornadaHarness", "NOLOCK", "TOP (", "INSERT INTO",
                          "UPDATE ", "DELETE FROM", "IBGE", "PublishAsync("):
            self.assertNotIn(forbidden, source)

    def test_productive_guard_still_closed(self):
        boundary = (ROOT / "V9OperationalRunBoundary.cs").read_text(encoding="utf-8")
        self.assertIn("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED", boundary)


if __name__ == "__main__":
    unittest.main()
