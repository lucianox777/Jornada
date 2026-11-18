import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class SnapshotSourceAuthWiringTests(unittest.TestCase):
    def test_authentication_precedes_graph_and_receipt(self):
        code = (ROOT / "V9OperationalSnapshotGraphVerification.cs").read_text(encoding="utf-8")
        verify = code.split("public static async Task<Receipt> VerifyAsync(", 1)[1].split(
            "public static async Task<Receipt> VerifyWithSnapshotCensusAsync(", 1)[0]
        auth = verify.index("V9OperationalSilverSourceAuthentication.VerifyAsync(")
        gold = verify.index("V9OperationalGoldCohortMembership.VerifyAsync(")
        graph = verify.index("V9OperationalCohortGraphPreview.ReadAsync(")
        receipt = verify.index("V9OperationalGraphReceiptVerification.Verify(")
        self.assertLess(gold, auth)
        self.assertLess(auth, graph)
        self.assertLess(graph, receipt)
        self.assertIn("connection, snapshotTransaction, frozenCohort, ct", verify)
        self.assertIn("return await VerifyAsync(", code)
        self.assertIn("return VerifyWithSnapshotCensusAsync(", code)


if __name__ == "__main__":
    unittest.main()
