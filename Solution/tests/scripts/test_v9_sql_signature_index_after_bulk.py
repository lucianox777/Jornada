#!/usr/bin/env python3
"""Structural regression checks for V9 physical SQL signature-index load order.

These tests do not claim a SQL Server benchmark or scientific P95 parity.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlAdaptiveCalibrationProbe.cs"
HOLDOUT = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs"
COORD = ROOT / "src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs"


def method(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class V9SqlSignatureIndexAfterBulkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.probe = PROBE.read_text(encoding="utf-8")
        cls.holdout = HOLDOUT.read_text(encoding="utf-8")
        cls.coord = COORD.read_text(encoding="utf-8")

    def test_empty_signature_table_is_created_without_index(self):
        create = method(self.probe, "private static async Task CreateTablesAsync(", 
                        "private static async Task BulkObservationsAsync(")
        self.assertIn("CREATE TABLE #v9_cal_sig(", create)
        self.assertNotIn("CREATE INDEX IX_v9_cal_sig", create)

    def test_index_created_once_after_complete_bulk_copy_and_before_return(self):
        bulk = method(self.probe, "private static async Task BulkSignaturesAsync(",
                      "public static async Task<IReadOnlyList<double>> RepeatPhysicalMaterializationAsync(")
        self.assertEqual(self.probe.count("CREATE INDEX IX_v9_cal_sig"), 1)
        self.assertIn("DestinationTableName = \"#v9_cal_sig\"", bulk)
        self.assertIn("BatchSize = 25_000", bulk)
        copy_at = bulk.index("await bulk.WriteToServerAsync(table, cancellationToken);")
        dispose_at = bulk.index("await using var index")
        index_at = bulk.index("CREATE INDEX IX_v9_cal_sig")
        execute_at = bulk.index("await index.ExecuteNonQueryAsync(cancellationToken);")
        self.assertLess(copy_at, dispose_at)
        self.assertLess(dispose_at, index_at)
        self.assertLess(index_at, execute_at)
        self.assertIn("using (var bulk = new SqlBulkCopy(connection)", bulk)

    def test_all_current_materialization_paths_pass_via_bulk_signatures(self):
        self.assertIn("await BulkSignaturesAsync(connection, signatures, cancellationToken);", self.probe)
        self.assertIn("await BulkSignaturesAsync(connection, replaySignatures, cancellationToken);", self.probe)
        self.assertIn("await BulkSignaturesAsync(connection, signatures, cancellationToken);", self.holdout)
        frozen = method(self.holdout, "private static async Task<IReadOnlyList<(int A, int B)>> PrepareFrozenValidationPairsAsync(",
                        "\n}")
        self.assertLess(frozen.index("await CreateTablesAsync("),
                        frozen.index("await BulkSignaturesAsync("))
        self.assertLess(frozen.index("await BulkSignaturesAsync("),
                        frozen.index("await MaterializePairsAsync("))

    def test_holdout_repetitions_and_index_on_pairs_are_preserved(self):
        self.assertIn("iteration < Math.Max(1, execution.EndToEndRepetitions)", self.holdout)
        self.assertIn("await PrepareFrozenValidationPairsAsync(", self.holdout)
        self.assertIn("CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs", self.probe)
        self.assertEqual(self.coord.count(
            "await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync("), 1)
        self.assertIn("c.Execution with { TotalComparedConfigurations = 1 }", self.coord)


if __name__ == "__main__":
    unittest.main()
