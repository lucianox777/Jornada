import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class CensusInputParityTests(unittest.TestCase):
    def test_census_and_preview_reject_cpf_inputs(self):
        census = (ROOT / "V9OperationalSqlEdgeCensus.cs").read_text(encoding="utf-8")
        preview = (ROOT / "V9OperationalCohortGraphPreview.cs").read_text(encoding="utf-8")
        guard = "!string.IsNullOrWhiteSpace(source.Observation.Cpf)"
        self.assertIn(guard, census)
        self.assertIn(guard, preview)
        self.assertLess(census.index(guard), census.index("pinnedRuntime.BuildCandidateUuidQuery("))
        self.assertIn("RUNNER_V9_COHORT_INVALID_OR_DUPLICATE", census)


if __name__ == "__main__":
    unittest.main()
