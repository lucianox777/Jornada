import pathlib
import unittest

SOURCE = (pathlib.Path(__file__).resolve().parents[2] / "src" /
          "Jornada.Linkage.Runner" / "V9OperationalBlockingManifest.cs")


class OperationalManifestTests(unittest.TestCase):
    def test_complete_snapshot_source_manifest(self):
        code = SOURCE.read_text(encoding="utf-8")
        for required in (
            "IsolationLevel.Snapshot", "ReferenceEquals(snapshotTransaction.Connection, connection)",
            "COUNT_BIG(*)", "identidade.blocking_chave", "vigencia_fim IS NULL",
            "CommandBehavior.SequentialAccess", "IncrementalHash",
            "SHA256", "Latin1_General_100_BIN2",
            "seen != expected", "seen > expected", "checked(seen + 1)",
            "connection, snapshotTransaction",
        ):
            self.assertIn(required, code)
        for forbidden in ("NOLOCK", "TOP (", "OFFSET ", "JornadaHarness",
                          "INSERT INTO", "DELETE FROM", "UPDATE identidade",
                          "PublishAsync(", "IBGE"):
            self.assertNotIn(forbidden, code)

    def test_not_productive_certification(self):
        code = SOURCE.read_text(encoding="utf-8")
        self.assertIn("not V9 candidate graph completeness or certification", code)


if __name__ == "__main__":
    unittest.main()
